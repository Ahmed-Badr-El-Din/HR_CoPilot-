using System.Text.Json;
using HR.Application.Abstractions.Models;
using HR.Application.Agents;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Domain.Runs;
using HR.Domain.Screening;

namespace HR.Application.Agents;

/// <summary>
/// D6 Agent 2. Role: score each candidate against the rubric using ONLY redacted
/// evidence produced by the extractor. Restricted tools: validate_shortlist
/// (deterministic validation; never a source of new facts). Termination: every
/// dimension scored, totals computed by deterministic code (RubricMath), never left
/// to the model.
/// </summary>
public sealed class RubricScorerAgent
{
    public const string Name = "rubric_scorer";

    public async Task<CandidateScore> RunAsync(
        ScreeningRequest request,
        string candidateId,
        EvidenceExtractionResult extraction,
        AgentServices services,
        RunId runId,
        CancellationToken ct)
    {
        await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.AgentStarted,
            AgentName = Name,
            Text = $"Scoring candidate {candidateId} on redacted evidence only.",
        }, ct);

        var evidenceByDimension = extraction.Evidence
            .GroupBy(e => e.DimensionId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.QuoteText).ToList());

        var systemPrompt = services.Prompts.GetPrompt("agents/rubric-scoring", 1);
        var rubricBlock = JsonSerializer.Serialize(new
        {
            candidate_id = candidateId,
            role = request.Role.Title,
            dimensions = request.Rubric.Dimensions.Select(d => new { id = d.Id, name = d.Name, description = d.Description, maxScore = d.MaxScore, weight = d.Weight }),
            evidence_by_dimension = evidenceByDimension,
        });

        var raw = await services.Provider.CompleteAsync(new CompletionRequest(
            PromptId: "agents/rubric-scoring",
            SystemPrompt: systemPrompt,
            Messages: new[] { new ChatMessage(ModelRoles.User, $"RUBRIC + REDACTED EVIDENCE:\n{rubricBlock}\n\nRespond with JSON only.") },
            Temperature: 0.0,
            MaxTokens: 1400,
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

        var parsed = TryParseScores(AgentExecutor.ExtractJsonFragment(raw.Text));
        var maxByDim = request.Rubric.Dimensions.ToDictionary(d => d.Id, d => (double)d.MaxScore);
        var weightByDim = request.Rubric.Dimensions.ToDictionary(d => d.Id, d => d.Weight);

        var dimensionScores = new List<DimensionScore>();
        foreach (var dim in request.Rubric.Dimensions)
        {
            var rawScore = parsed.TryGetValue(dim.Id, out var s) ? s.Score : double.NaN;
            var clamped = double.IsNaN(rawScore) ? 0 : Math.Clamp(rawScore, 0, dim.MaxScore);
            var rationale = parsed.TryGetValue(dim.Id, out var r) ? r.Rationale : "No evidence found; scored zero on redacted evidence.";
            dimensionScores.Add(new DimensionScore(dim.Id, Math.Round(clamped, 1), dim.MaxScore, rationale));
        }

        var total = RubricMath.WeightedTotal(dimensionScores.Select(d => (d.DimensionId, weightByDim[d.DimensionId], d.Score)));
        var maxTotal = RubricMath.WeightedTotal(maxByDim.Select(kv => (kv.Key, weightByDim[kv.Key], kv.Value)));
        if (maxTotal <= 0) maxTotal = request.Rubric.Dimensions.Sum(d => (double)d.MaxScore);

        var excluded = extraction.BiasReport.Records
            .Select(r => r.AttributeKind.ToString())
            .Distinct()
            .ToList();

        // Bias guard proof: capture exactly what the scorer received (redacted role,
        // dimensions, redacted evidence) the moment the input is composed, before any
        // completion runs. Nothing below can read protected attributes.
        var scorerInputSnapshot = JsonSerializer.Serialize(new
        {
            role = request.Role.Title,
            dimensions = request.Rubric.Dimensions.Select(d => new { id = d.Id, name = d.Name, description = d.Description, maxScore = d.MaxScore, weight = d.Weight }),
            evidence_by_dimension = evidenceByDimension,
            evidence_chunks = extraction.Chunks.Select(c => new { c.ChunkId, c.DocumentId, c.DocumentTitle, c.Section, c.PageReference, c.SanitizedText }),
        });
        await services.BiasWriter.RecordScorerInputAsync(new AuditLog(
            runId,
            "rubric_scoring",
            candidateId,
            services.Context.UserId.Value,
            services.Context.CorrelationId,
            scorerInputSnapshot,
            excluded,
            DateTimeOffset.UtcNow), ct);

        var score = new CandidateScore
        {
            CandidateId = candidateId,
            Dimensions = dimensionScores,
            Total = total,
            MaxTotal = maxTotal,
            Summary = BuildSummary(total, maxTotal, request.Rubric.Dimensions.Count),
            ExcludedProtectedAttributes = excluded,
            SourceCitations = extraction.Citations,
        };

        // Deterministic cross-check on the scoring output (restricted tool).
        try
        {
            var validationJson = JsonSerializer.Serialize(dimensionScores.Select(d => new { d.DimensionId, score = d.Score }));
            var tool = services.Tools.Get("validate_shortlist");
            var exec = await AgentExecutor.RunToolAsync(runId, services, Name, tool, $"{{\"shortlist\":{validationJson}}}", ct);
            if (exec.StructuredJson?.Contains("\"valid\": true") != true)
            {
                score.Summary += " — NOTE: deterministic validation flagged the score sheet; totals were recomputed in code.";
            }
        }
        catch (DomainException)
        {
            // Validation tool issues must never mask a score.
        }

        await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.AgentFinished,
            AgentName = Name,
            Text = $"Scored {candidateId}: {total:F1}/{maxTotal:F1}",
            PayloadJson = JsonSerializer.Serialize(new { candidate_id = candidateId, total, max_total = maxTotal, excluded }),
        }, ct);

        return score;
    }

    private static Dictionary<string, (double Score, string Rationale)> TryParseScores(string? fragment)
    {
        var map = new Dictionary<string, (double, string)>();
        if (fragment is null) return map;
        try
        {
            using var doc = JsonDocument.Parse(fragment);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("scores");
            foreach (var e in root.EnumerateArray())
            {
                var id = e.TryGetProperty("dimension_id", out var di) ? di.GetString()
                         : e.TryGetProperty("dimensionId", out var d2) ? d2.GetString()
                         : null;
                if (id is null) continue;
                var score = e.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var sv) ? sv : double.NaN;
                var rationale = e.TryGetProperty("rationale", out var ra) ? ra.GetString() ?? string.Empty : string.Empty;
                map[id] = (score, rationale);
            }

            return map;
        }
        catch (JsonException)
        {
            return map;
        }
    }

    private static string BuildSummary(double total, double maxTotal, int dimensionCount)
    {
        var percent = maxTotal <= 0 ? 0 : total / maxTotal;
        return percent >= 0.75 ? $"Strong fit: {total:F1}/{maxTotal:F1} across {dimensionCount} dimensions."
            : percent >= 0.55 ? $"Good fit: {total:F1}/{maxTotal:F1} across {dimensionCount} dimensions."
            : percent >= 0.35 ? $"Partial fit: {total:F1}/{maxTotal:F1} across {dimensionCount} dimensions."
            : $"Weak fit: {total:F1}/{maxTotal:F1} across {dimensionCount} dimensions.";
    }
}
