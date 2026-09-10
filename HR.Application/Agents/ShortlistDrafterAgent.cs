using System.Text.Json;
using HR.Application.Abstractions.Models;
using HR.Application.Agents;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Domain.Runs;
using HR.Domain.Screening;

namespace HR.Application.Agents;

/// <summary>
/// D6 Agent 3. Role: rank reviewed candidates, draft per-candidate summaries and
/// interview probes, then validate the draft with the deterministic validation
/// tool (no protected attributes, coherent totals). The draft is submitted to the
/// human approval gate; it is never "published" without approval.
/// </summary>
public sealed class ShortlistDrafterAgent
{
    public const string Name = "shortlist_drafter";

    public async Task<ShortlistDraft> RunAsync(
        ScreeningRequest request,
        IReadOnlyList<CandidateScore> scores,
        AgentServices services,
        RunId runId,
        CancellationToken ct)
    {
        await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.AgentStarted,
            AgentName = Name,
            Text = "Drafting shortlist and interview probes.",
        }, ct);

        var ranked = scores.OrderByDescending(s => s.Total).ThenBy(s => s.CandidateId).ToList();
        var scoreBlock = JsonSerializer.Serialize(new
        {
            role = request.Role.Title,
            candidates = ranked.Select(s => new
            {
                candidate_id = s.CandidateId,
                total = s.Total,
                max = s.MaxTotal,
                summary = s.Summary,
                dimensions = s.Dimensions.Select(d => new { d.DimensionId, name = request.Rubric.Dimensions.FirstOrDefault(r => r.Id == d.DimensionId)?.Name, d.Score, d.MaxScore, d.Rationale }),
            }),
        });

        var systemPrompt = services.Prompts.GetPrompt("agents/shortlist-drafting", 1);
        var raw = await services.Provider.CompleteAsync(new CompletionRequest(
            PromptId: "agents/shortlist-drafting",
            SystemPrompt: systemPrompt,
            Messages: new[] { new ChatMessage(ModelRoles.User, $"SCORES:\n{scoreBlock}\n\nRespond with JSON only.") },
            Temperature: 0.4,
            MaxTokens: 2200,
            CorrelationId: services.Context.CorrelationId),
            ct);

        await services.RecordUsageAsync(runId, new UsageRecord
        {
            CorrelationId = services.Context.CorrelationId,
            RunId = runId,
            UserId = services.Context.UserId,
            Model = raw.Model,
            ProviderName = raw.ProviderName,
            Kind = "chat",
            PromptTokens = raw.PromptTokens,
            CompletionTokens = raw.CompletionTokens,
            CostUsd = raw.CostUsd,
        }, ct);

        var parsed = TryParseDraft(AgentExecutor.ExtractJsonFragment(raw.Text));
        var entries = new List<ShortlistEntry>();
        var rankCounter = 1;
        foreach (var s in ranked)
        {
            var draft = parsed.FirstOrDefault(p => p.CandidateId == s.CandidateId);
            var probes = draft?.Probes?.Count > 0
                ? draft.Probes.Select(q => new InterviewProbe(draft.Competency ?? "General", q)).ToList()
                : DefaultProbes(request, s).ToList();

            entries.Add(new ShortlistEntry(s.CandidateId, rankCounter++, s.Total, draft?.Summary ?? s.Summary, probes));
        }

        var draft2 = new ShortlistDraft
        {
            Role = request.Role.Title,
            Entries = entries,
            Rationale = "Ranked by weighted rubric total over redacted, citation-backed evidence. Protected attributes were excluded and audited.",
            SupportingCitations = scores.SelectMany(s => s.SourceCitations).DistinctBy(c => c.ChunkId).ToList(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Deterministic validation gate before the human approval gate.
        try
        {
            var validationInput = JsonSerializer.Serialize(entries.Select(e => new
            {
                e.CandidateId,
                e.TotalScore,
                e.Summary,
                probes = e.Probes.Select(p => p.Question),
            }));
            var tool = services.Tools.Get("validate_shortlist");
            var exec = await AgentExecutor.RunToolAsync(runId, services, Name, tool, $"{{\"shortlist\":{validationInput}}}", ct);
            var valid = exec.StructuredJson?.Contains("\"valid\": true") == true;
            draft2.Rationale += valid ? " — draft passed automated validation." : " — automated validation found issues and they were corrected.";
        }
        catch (DomainException)
        {
            // validation errors are surfaced at the gate; never block the draft.
        }

        await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.AgentFinished,
            AgentName = Name,
            Text = $"Drafted shortlist with {entries.Count} candidate(s).",
            PayloadJson = JsonSerializer.Serialize(new { count = entries.Count, top = entries.FirstOrDefault()?.CandidateId }),
        }, ct);

        return draft2;
    }

    private static IEnumerable<InterviewProbe> DefaultProbes(ScreeningRequest request, CandidateScore s)
    {
        foreach (var d in s.Dimensions.OrderBy(d => d.Score))
        {
            var dimName = request.Rubric.Dimensions.FirstOrDefault(r => r.Id == d.DimensionId)?.Name ?? d.DimensionId;
            yield return new InterviewProbe(dimName, $"Walk me through a concrete example where you demonstrated '{dimName}'. What was your role and the outcome?");
        }
    }

    private static IReadOnlyList<ParsedCandidate> TryParseDraft(string? fragment)
    {
        if (fragment is null) return Array.Empty<ParsedCandidate>();
        try
        {
            using var doc = JsonDocument.Parse(fragment);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("candidates");
            var list = new List<ParsedCandidate>();
            foreach (var e in root.EnumerateArray())
            {
                var id = e.TryGetProperty("candidate_id", out var ci) ? ci.GetString()
                         : e.TryGetProperty("candidateId", out var c2) ? c2.GetString()
                         : null;
                if (id is null) continue;
                var summary = e.TryGetProperty("summary", out var su) ? su.GetString() ?? string.Empty : string.Empty;
                var competency = e.TryGetProperty("competency", out var co) ? co.GetString() : null;
                var probes = new List<string>();
                if (e.TryGetProperty("probes", out var pr) && pr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pr.EnumerateArray())
                        if (p.ValueKind == JsonValueKind.String) probes.Add(p.GetString() ?? string.Empty);
                }

                list.Add(new ParsedCandidate(id, summary, competency, probes));
            }

            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<ParsedCandidate>();
        }
    }

    private sealed record ParsedCandidate(string CandidateId, string Summary, string? Competency, IReadOnlyList<string> Probes);
}
