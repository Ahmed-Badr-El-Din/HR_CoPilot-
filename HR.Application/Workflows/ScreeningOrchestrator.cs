using System.Text.Json;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Abstractions.Tools;
using HR.Application.Agents;
using HR.Domain.Approvals;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Domain.Runs;
using HR.Domain.Screening;

namespace HR.Application.Workflows;

/// <summary>
/// State-machine orchestration for the D6 flow:
///   queued → running → [evidence extractor] → [rubric scorer] → [shortlist drafter]
///   → awaiting-approval → (approve|reject|edit-and-approve) → completed|failed
/// Human approval is mandatory before the shortlist is published — the write tool
/// rejects any invocation that is not backed by an approved ApprovalRequest.
/// </summary>
public sealed class ScreeningOrchestrator(
    IModelProvider modelProvider,
    IRetrievalService retriever,
    IToolRegistry toolRegistry,
    IPromptCatalog prompts,
    IHrUnitOfWork store,
    IBiasWriter biasWriter,
    IEventEmitter emitter,
    IClock clock)
{
    private const string ApprovalRole = "HiringManager";
    private const int MaxIterations = 5;
    private static readonly TimeSpan PerStepTimeout = TimeSpan.FromSeconds(60);

    public async Task<Run> StartAsync(ScreeningRequest request, ICorrelationContext ctx, CancellationToken ct)
    {
        var run = new Run
        {
            Kind = RunKind.Screening,
            Status = RunStatus.Running,
            OwnerUserId = ctx.UserId,
            CorrelationId = ctx.CorrelationId,
            RequestJson = JsonSerializer.Serialize(request),
        };
        await store.Runs.AddAsync(run);
        await store.SaveChangesAsync(ct);
        await PersistAndBroadcastAsync(run.Id, new RunEvent { Kind = RunEventKind.RunStarted, Text = "Screening workflow started." }, ct);

        var services = BuildServices(ctx, run.Id);

        // ---- Agent 1: Evidence extraction (per candidate). ----
        var extractions = new List<EvidenceExtractionResult>();
        foreach (var candidate in request.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            var extractor = new EvidenceExtractorAgent();
            var extraction = await WithGuard(run, () => extractor.RunAsync(request, candidate, services, run.Id, ct), ct);
            extractions.Add(extraction);
        }

        // ---- Agent 2: Rubric scoring from redacted evidence. ----
        var scores = new List<CandidateScore>();
        var scorer = new RubricScorerAgent();
        foreach (var candidate in request.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            var extraction = extractions.First(x => x.CandidateId == candidate.Id);
            var score = await WithGuard(run, () => scorer.RunAsync(request, candidate.Id, extraction, services, run.Id, ct), ct);
            scores.Add(score);
        }

        // ---- Agent 3: Shortlist drafting + validation. ----
        var drafter = new ShortlistDrafterAgent();
        var draft = await WithGuard(run, () => drafter.RunAsync(request, scores, services, run.Id, ct), ct);

        // ---- Human approval gate. ----
        var approval = new ApprovalRequest
        {
            RunId = run.Id,
            StepName = "shortlist_publish",
            PayloadJson = JsonSerializer.Serialize(draft),
            AssignedRole = ApprovalRole,
            SlaDeadline = clock.UtcNow.AddHours(24),
        };
        await store.Approvals.AddAsync(approval);
        run.Status = RunStatus.AwaitingApproval;
        run.ApprovalRequestedAt = clock.UtcNow;
        await store.Runs.UpdateAsync(run);
        await store.SaveChangesAsync(ct);

        await PersistAndBroadcastAsync(run.Id, new RunEvent
        {
            Kind = RunEventKind.ApprovalRequired,
            AgentName = "shortlist_drafter",
            Text = $"Shortlist for {request.Role.Title} awaiting {ApprovalRole} approval.",
            PayloadJson = JsonSerializer.Serialize(new { approval_id = approval.Id.ToString(), role = request.Role.Title, candidate_count = draft.Entries.Count }),
        }, ct);

        return run;
    }

    public async Task<Run> ResolveApprovalAsync(ApprovalId approvalId, ApprovalDecision decision, ICorrelationContext ctx, CancellationToken ct)
    {
        var approval = await store.Approvals.GetByIdAsync(approvalId, ct)
            ?? throw new ApprovalNotFoundError(approvalId.ToString());
        if (approval.Status != ApprovalStatus.Pending)
            throw new InvalidStateError($"Approval '{approvalId:N}' is not pending.");

        var run = await store.Runs.GetByIdAsync(approval.RunId, ct) ?? throw new RunNotFoundError(approval.RunId.ToString());
        if (run.Status != RunStatus.AwaitingApproval)
            throw new InvalidStateError($"Run '{run.Id:N}' is not awaiting approval.");

        var services = BuildServices(ctx, run.Id);
        approval.Status = decision.Status;
        approval.ResponderUserId = ctx.UserId;
        approval.RespondedAt = clock.UtcNow;
        approval.Comment = decision.Comment;
        approval.EditedPayloadJson = decision.EditedPayloadJson;
        await store.Approvals.UpdateAsync(approval);

        var finalPayload = decision.Status == ApprovalStatus.EditedAndApproved ? decision.EditedPayloadJson : approval.PayloadJson;

        var emitPayload = new
        {
            approval_id = approval.Id.ToString(),
            decision = decision.Status.ToString(),
            comment = decision.Comment,
            reviewer = ctx.UserId.ToString(),
        };

        switch (decision.Status)
        {
            case ApprovalStatus.Approved:
            case ApprovalStatus.EditedAndApproved:
                {
                    // The write tool refuses to run unless the approval record says "approved".
                    var publishTool = toolRegistry.Get("publish_shortlist");
                    var args = JsonSerializer.Serialize(new { approval_id = approval.Id.ToString(), shortlist_json = finalPayload });
                    var expectedAgent = new[] { "__orchestrator__" };
                    if (!publishTool.AllowedAgents.SequenceEqual(expectedAgent))
                        throw new InvalidStateError("publish_shortlist must be orchestrator-only.");

                    var exec = await AgentExecutor.RunToolAsync(run.Id, services, "__orchestrator__", publishTool, args, ct);
                    run.ResultJson = exec.StructuredJson;
                    run.Status = RunStatus.Completed;
                    run.FinishedAt = clock.UtcNow;
                    await store.Runs.UpdateAsync(run);
                    await store.SaveChangesAsync(ct);

                    await PersistAndBroadcastAsync(run.Id, new RunEvent
                    {
                        Kind = RunEventKind.ApprovalResolved,
                        ToolName = publishTool.Name,
                        Text = decision.Status.ToString(),
                        PayloadJson = JsonSerializer.Serialize(emitPayload),
                    }, ct);
                    await PersistAndBroadcastAsync(run.Id, new RunEvent { Kind = RunEventKind.RunFinished, Text = "Shortlist published and workflow completed." }, ct);
                    break;
                }
            case ApprovalStatus.Rejected:
                {
                    run.Status = RunStatus.Failed;
                    run.Error = $"Shortlist rejected{(!string.IsNullOrWhiteSpace(decision.Comment) ? $": {decision.Comment}" : ".")}";
                    run.FinishedAt = clock.UtcNow;
                    await store.Runs.UpdateAsync(run);
                    await store.SaveChangesAsync(ct);

                    await PersistAndBroadcastAsync(run.Id, new RunEvent
                    {
                        Kind = RunEventKind.ApprovalResolved,
                        Text = ApprovalStatus.Rejected.ToString(),
                        PayloadJson = JsonSerializer.Serialize(emitPayload),
                    }, ct);
                    await PersistAndBroadcastAsync(run.Id, new RunEvent { Kind = RunEventKind.Error, Text = run.Error }, ct);
                    break;
                }
            default:
                throw new ValidationError($"Unsupported decision {decision.Status}.");
        }

        return run;
    }

    private AgentServices BuildServices(ICorrelationContext ctx, RunId runId)
        => new(
            Provider: modelProvider,
            Retriever: retriever,
            Tools: toolRegistry,
            Prompts: prompts,
            Context: ctx,
            BiasWriter: biasWriter,
            ResponseLanguage: "mixed",
            EmitAsync: PersistAndBroadcastAsync,
            RecordUsageAsync: (_, usage, _) => UsageAsync(usage),
            MaxIterations: MaxIterations,
            PerStepTimeout: PerStepTimeout);

    private Task UsageAsync(UsageRecord usage) => store.Usage.AddAsync(usage, CancellationToken.None);

    private static async Task<T> WithGuard<T>(Run run, Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Cancelled;
            run.FinishedAt = DateTimeOffset.UtcNow;
            throw;
        }
    }

    private async Task PersistAndBroadcastAsync(RunId runId, RunEvent ev, CancellationToken ct)
    {
        ev.RunId = runId;
        emitter.Publish($"run:{runId}", new EventPacket($"run.{ev.Kind.ToString().ToLowerInvariant()}", JsonSerializer.Serialize(ev), clock.UtcNow));
        await store.Runs.AppendEventAsync(ev);
        await store.SaveChangesAsync(ct);
    }
}
