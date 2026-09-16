using System.Text.Json;
using HR.API.Endpoints;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Persistence;
using HR.Application.Authorization;
using HR.Application.Workflows;
using HR.Domain.Approvals;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Domain.Runs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HR.API.Endpoints;

public sealed record ApprovalDecisionRequest(string Decision, string? Comment, string? EditedPayloadJson);

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/approvals/pending", ListPendingApprovalsAsync).RequireAuthorization(Roles.CanApprove);
        app.MapPost("/api/approvals/{id:guid}/decide", DecideApprovalAsync).RequireAuthorization(Roles.CanApprove);

        app.MapPost("/api/workflows/screening", StartScreeningAsync).RequireAuthorization(Roles.CanScreen);
        app.MapGet("/api/runs", ListRunsAsync).RequireAuthorization();
        app.MapGet("/api/runs/{id:guid}", RunDetailAsync).RequireAuthorization();
        app.MapGet("/api/runs/{id:guid}/events", RunEventsStreamAsync).RequireAuthorization();
        app.MapGet("/api/runs/{id:guid}/trace", RunTraceAsync).RequireAuthorization();
    }

    // ------------------------------------------------------------------ run

    private static async Task<Ok<object>> StartScreeningAsync(
        HR.Domain.Screening.ScreeningRequest request,
        ICorrelationContext ctx,
        ScreeningOrchestrator orchestrator,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Role.Title))
            throw new ValidationError("role is required.");
        if (request.Candidates.Count == 0)
            throw new ValidationError("At least one candidate is required.");

        var run = await orchestrator.StartAsync(request, ctx, ct);
        return TypedResults.Ok<object>(new
        {
            run_id = run.Id.ToString(),
            status = run.Status.ToString(),
            approval_requested = run.Status == RunStatus.AwaitingApproval,
        });
    }

    private static async Task<Ok<object>> ListRunsAsync(
        ICorrelationContext ctx,
        IHrUnitOfWork store,
        int skip = 0,
        int take = 50)
    {
        var isSupervisor = ctx.Roles.Any(r => r is "Admin" or "Auditor");
        var runs = isSupervisor
            ? await store.Runs.ListAllAsync(skip, Math.Clamp(take, 1, 200))
            : await store.Runs.ListByUserAsync(ctx.UserId, skip, Math.Clamp(take, 1, 200));

        return TypedResults.Ok<object>(new { runs = runs.Select(ToRunDto).ToArray() });
    }

    private static async Task<Ok<object>> RunDetailAsync(
        string id,
        ICorrelationContext ctx,
        IHrUnitOfWork store)
    {
        var run = await RequireRunAsync(id, ctx, store);
        var events = await store.Runs.GetEventsAsync(run.Id);
        return TypedResults.Ok<object>(ToRunDetailDto(run, events));
    }

    private static async Task<Ok<object>> RunTraceAsync(
        string id,
        ICorrelationContext ctx,
        IHrUnitOfWork store)
    {
        var run = await RequireRunAsync(id, ctx, store);
        var events = await store.Runs.GetEventsAsync(run.Id);
        var usage = await store.Usage.ListAllAsync(0, 1000);
        return TypedResults.Ok<object>(new
        {
            run = ToRunDetailDto(run, events),
            usage = usage
                .Where(u => u.RunId is not null && u.RunId.Value.Value == run.Id.Value)
                .Select(u => new
                {
                    correlation_id = u.CorrelationId,
                    kind = u.Kind,
                    model = u.Model,
                    provider = u.ProviderName,
                    prompt_tokens = u.PromptTokens,
                    completion_tokens = u.CompletionTokens,
                    cost_usd = u.CostUsd,
                    created_at = u.CreatedAt,
                })
                .ToArray(),
        });
    }

    private static async Task<Run> RequireRunAsync(string id, ICorrelationContext ctx, IHrUnitOfWork store)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new ValidationError($"Invalid run id '{id}'.");
        var run = await store.Runs.GetByIdAsync(new RunId(guid))
            ?? throw new RunNotFoundError(id);
        AuthorizationService.EnsureResourceOwnerOrAuditor(run.OwnerUserId, ctx, "run");
        return run;
    }

    // ---------------------------------------------------------- live events

    private static async Task RunEventsStreamAsync(
        string id,
        HttpContext http,
        ICorrelationContext ctx,
        IHrUnitOfWork store,
        IEventEmitter emitter,
        CancellationToken ct)
    {
        var run = await RequireRunAsync(id, ctx, store);

        var response = http.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Replay already-persisted events, then tail live ones. A client that
        // connects late still sees the full picture (FR-9 "trace replayable").
        foreach (var ev in await store.Runs.GetEventsAsync(run.Id, ct))
            await SessionEndpoints.SendSseAsync(response, "event", ToEventDto(ev));

        await foreach (var packet in emitter.SubscribeAsync($"run:{run.Id}", ct))
            await SessionEndpoints.SendSseAsync(response, packet.Type, JsonDocument.Parse(packet.PayloadJson).RootElement);
    }

    // ------------------------------------------------------------ approvals

    private static async Task<Ok<object>> ListPendingApprovalsAsync(ICorrelationContext ctx, IHrUnitOfWork store)
    {
        var pending = await store.Approvals.ListPendingAsync();
        var byRole = pending.Where(a => string.IsNullOrEmpty(a.AssignedRole) || ctx.Roles.Contains(a.AssignedRole)).ToArray();
        var runs = new List<object>();
        foreach (var a in byRole)
        {
            var run = await store.Runs.GetByIdAsync(a.RunId);
            runs.Add(new
            {
                approval_id = a.Id.ToString(),
                run_id = a.RunId.ToString(),
                step = a.StepName,
                role = a.AssignedRole,
                created_at = a.CreatedAt,
                sla_deadline = a.SlaDeadline,
                requested_by = run?.OwnerUserId.Value,
                payload = ParseJson(a.PayloadJson),
            });
        }

        return TypedResults.Ok<object>(new { pending = runs });
    }

    private static async Task<Ok<object>> DecideApprovalAsync(
        string id,
        ApprovalDecisionRequest request,
        ICorrelationContext ctx,
        IHrUnitOfWork store,
        ScreeningOrchestrator orchestrator,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new ValidationError($"Invalid approval id '{id}'.");

        var approval = await store.Approvals.GetByIdAsync(new ApprovalId(guid))
            ?? throw new ApprovalNotFoundError(id);
        if (!string.IsNullOrEmpty(approval.AssignedRole) && !ctx.Roles.Contains(approval.AssignedRole))
            throw new PolicyViolationError($"Only {approval.AssignedRole} may resolve this approval.");

        var status = request.Decision.Trim().ToLowerInvariant() switch
        {
            "approved" or "approve" => ApprovalStatus.Approved,
            "rejected" or "reject" => ApprovalStatus.Rejected,
            "edited_and_approved" or "edit_and_approve" => ApprovalStatus.EditedAndApproved,
            _ => throw new ValidationError($"Unsupported decision '{request.Decision}'. Use approve, reject or edit_and_approve."),
        };

        var decision = new ApprovalDecision(status, request.Comment, request.EditedPayloadJson);
        var run = await orchestrator.ResolveApprovalAsync(new ApprovalId(guid), decision, ctx, ct);
        return TypedResults.Ok<object>(new
        {
            run_id = run.Id.ToString(),
            status = run.Status.ToString(),
            error = run.Error,
        });
    }

    // ---------------------------------------------------------------- dto

    private static object ToRunDto(Run r) => new
    {
        id = r.Id.ToString(),
        kind = r.Kind.ToString(),
        status = r.Status.ToString(),
        correlation_id = r.CorrelationId,
        owner = r.OwnerUserId.Value,
        degraded = r.DegradedToPlainRag,
        created_at = r.CreatedAt,
        finished_at = r.FinishedAt,
    };

    private static object ToRunDetailDto(Run r, IReadOnlyList<RunEvent> events) => new
    {
        id = r.Id.ToString(),
        kind = r.Kind.ToString(),
        status = r.Status.ToString(),
        correlation_id = r.CorrelationId,
        owner = r.OwnerUserId.Value,
        request = ParseJson(r.RequestJson),
        result = ParseJson(r.ResultJson),
        error = r.Error,
        degraded = r.DegradedToPlainRag,
        created_at = r.CreatedAt,
        started_at = r.StartedAt,
        finished_at = r.FinishedAt,
        approval_requested_at = r.ApprovalRequestedAt,
        events = events.Select(ToEventDto).ToArray(),
    };

    private static object ToEventDto(RunEvent ev) => new
    {
        kind = ev.Kind.ToString(),
        agent = ev.AgentName,
        tool = ev.ToolName,
        text = ev.Text,
        payload = ParseJson(ev.PayloadJson),
        prompt_tokens = ev.PromptTokens,
        completion_tokens = ev.CompletionTokens,
        cost_usd = ev.CostUsd,
        created_at = ev.CreatedAt,
    };

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
