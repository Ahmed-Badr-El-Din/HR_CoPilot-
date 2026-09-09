using System.Text.Json;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Agents;
using HR.Domain.Bias;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Domain.Runs;
using HR.Domain.Screening;
using HR.Domain.Security;

namespace HR.Application.Agents;

public sealed class EvidenceExtractionResult
{
    public string CandidateId { get; set; } = string.Empty;
    public List<ExtractedEvidence> Evidence { get; set; } = new();
    public List<EvidenceChunk> Chunks { get; set; } = new();
    public List<Citation> Citations { get; set; } = new();
    public BiasExclusionReport BiasReport { get; set; } = new();
}

/// <summary>
/// D6 Agent 1. Role: gather competency evidence from the candidate's document for
/// each rubric dimension. Restricted tools: search_chunks (read-only). Applicable
/// bias control: all candidate text is redacted before it becomes evidence; the
/// redaction report is persisted and surfaced to the scorer and the audit trail.
/// Termination: all dimensions produce >= 1 evidence item, or no relevant content
/// can be found for the candidate.
/// </summary>
public sealed class EvidenceExtractorAgent
{
    public const string Name = "evidence_extractor";

    public async Task<EvidenceExtractionResult> RunAsync(ScreeningRequest request, CandidateRef candidate, AgentServices services, RunId runId, CancellationToken ct)
    {
        await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.AgentStarted,
            AgentName = Name,
            Text = $"Extracting evidence for candidate {candidate.Id}.",
        }, ct);

        var result = new EvidenceExtractionResult { CandidateId = candidate.Id };

        // 1. Retrieve the candidate's own document chunks via the retrieval pipeline.
        var chunkPool = new Dictionary<ChunkId, RetrievedChunk>();
        var queries = new List<string> { candidate.Name.IfNotEmpty() ?? request.Role.Title };
        queries.AddRange(request.Rubric.Dimensions.Select(d => $"{request.Role.Title} {d.Name} {d.Description}"));

        foreach (var q in queries.Distinct())
        {
            var retrieved = await services.Retriever.RetrieveAsync(new RetrievalQuery(
                Text: q,
                TopK: 6,
                DocumentIds: new[] { candidate.DocumentId },
                Language: null),
                ct);

            foreach (var c in retrieved.Chunks) chunkPool.TryAdd(c.ChunkId, c);
        }

        // 2. Bias control: redact protected attributes before the evidence is built.
        var redactedByChunk = new Dictionary<ChunkId, string>();
        var biasRecords = new List<BiasAuditRecord>();
        foreach (var chunk in chunkPool.Values)
        {
            var report = ProtectedAttributeDetector.Exclude(candidate.Id, chunk.Text);
            redactedByChunk[chunk.ChunkId] = report.RedactedSnippet;
            biasRecords.AddRange(report.Records);
            if (report.Records.Count == 0) continue;
            result.BiasReport.Records.AddRange(report.Records);
        }

        result.BiasReport.CandidateId = candidate.Id;
        await services.BiasWriter.RecordAsync(candidate.Id, biasRecords, ct);

        if (chunkPool.Count == 0)
        {
            await EmitFinishedAsync(runId, services, result, candidate.Id, "No candidate content found; evidence empty.", ct);
            return result;
        }

        // 3. Ask the model to extract evidence (strict JSON response).
        var dims = request.Rubric.Dimensions.Select(d => $"- {d.Name}: {d.Description}").ToList();
        // Indirect prompt injection (OWASP LLM-01): candidate chunks are untrusted data,
        // so embedded override instructions are neutralised before the model reads them.
        var contextBlock = string.Join("\n\n", redactedByChunk.Select(kv => $"CHUNK [{kv.Key}]\n{PromptInjectionDetector.Neutralize(kv.Value)}"));
        var systemPrompt = services.Prompts.GetPrompt("agents/evidence-extraction", 1);
        var userPrompt = $"ROLE: {request.Role.Title}\n\nRUBRIC DIMENSIONS:\n{string.Join("\n", dims)}\n\nCANDIDATE CHUNKS:\n{contextBlock}\n\nRespond with JSON only.";

        var raw = await services.Provider.CompleteAsync(new CompletionRequest(
            PromptId: "agents/evidence-extraction",
            SystemPrompt: systemPrompt,
            Messages: new[] { new ChatMessage(ModelRoles.User, userPrompt) },
            Temperature: 0.0,
            MaxTokens: 2000,
            CorrelationId: services.Context.CorrelationId),
            ct);

        await RecordUsage(runId, services, raw);

        var fragment = AgentExecutor.ExtractJsonFragment(raw.Text);

        // 4. Grounding pass: only keep evidence that references a real chunk and whose
        //    quote overlaps that chunk's text. Anything else is dropped (never guessed).
        var parsed = TryParseEvidence(fragment);
        foreach (var item in parsed)
        {
            if (!redactedByChunk.TryGetValue(item.ChunkId, out var redactedText)) continue;
            if (!Overlaps(item.QuoteText, redactedText)) continue;

            var chunk = chunkPool[item.ChunkId];
            // Citation snippets are cut from the redacted text so a shortlist can never
            // carry a protected attribute back out through its supporting citations.
            var snippet = redactedText.Length <= 220 ? redactedText : redactedText[..220];
            var citation = new Citation(chunk.DocumentId, chunk.ChunkId, chunk.DocumentTitle, chunk.Section, chunk.PageReference, snippet, chunk.Score);
            var evidence = new ExtractedEvidence(candidate.Id, item.DimensionId, item.Competency, item.QuoteText, citation, item.Confidence);
            result.Evidence.Add(evidence);
            result.Citations.Add(citation);
            result.Chunks.Add(new EvidenceChunk(chunk.DocumentId, chunk.ChunkId, chunk.DocumentTitle, chunk.Section, chunk.PageReference, redactedText));
        }

        await EmitFinishedAsync(runId, services, result, candidate.Id, $"Found {result.Evidence.Count} grounded evidence items across {request.Rubric.Dimensions.Count} dimensions.", ct);
        return result;
    }

    private static async Task EmitFinishedAsync(RunId runId, AgentServices services, EvidenceExtractionResult result, string candidateId, string text, CancellationToken ct)
        => await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.AgentFinished,
            AgentName = Name,
            Text = text,
            PayloadJson = JsonSerializer.Serialize(new { candidate_id = candidateId, evidence_count = result.Evidence.Count, bias_records = result.BiasReport.TotalOccurrencesRemoved }),
        }, ct);

    private static Task RecordUsage(RunId runId, AgentServices services, CompletionResult raw)
        => services.RecordUsageAsync(runId, new UsageRecord
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
        }, CancellationToken.None);

    private static IReadOnlyList<ParsedEvidenceItem> TryParseEvidence(string? fragment)
    {
        if (fragment is null) return Array.Empty<ParsedEvidenceItem>();
        try
        {
            using var doc = JsonDocument.Parse(fragment);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("evidence");
            var list = new List<ParsedEvidenceItem>();
            foreach (var e in root.EnumerateArray())
            {
                var chunkIdRaw = ReadString(e, "chunk_id") ?? ReadString(e, "chunkId");
                if (!TryParseChunkId(chunkIdRaw, out var chunkId)) continue;
                var confidence = e.TryGetProperty("confidence", out var conf) && conf.TryGetDouble(out var c) ? Math.Clamp(c, 0.0, 1.0) : 0.5;
                list.Add(new ParsedEvidenceItem(
                    chunkId,
                    ReadString(e, "dimension_id") ?? ReadString(e, "dimensionId") ?? string.Empty,
                    ReadString(e, "competency") ?? ReadString(e, "claim") ?? string.Empty,
                    ReadString(e, "quote") ?? ReadString(e, "quote_text") ?? string.Empty,
                    confidence));
            }

            return list;
        }
        catch (JsonException)
        {
            return Array.Empty<ParsedEvidenceItem>();
        }
    }

    private static bool Overlaps(string quote, string chunkText)
    {
        if (string.IsNullOrWhiteSpace(quote)) return false;
        var normQuote = NormalizeForOverlap(quote);
        var normChunk = NormalizeForOverlap(chunkText);
        if (normQuote.Length < 12) return normChunk.Contains(normQuote, StringComparison.Ordinal);
        var window = Math.Min(normQuote.Length, 80);
        var probe = normQuote[..window];
        return normChunk.Contains(probe, StringComparison.Ordinal)
               || normChunk.Replace(" ", string.Empty).Contains(normQuote.Replace(" ", string.Empty), StringComparison.Ordinal);
    }

    public static bool TryParseChunkId(string? raw, out ChunkId chunkId)
    {
        chunkId = default;
        if (!Guid.TryParse(raw, out var g)) return false;
        chunkId = new ChunkId(g);
        return true;
    }

    private static string NormalizeForOverlap(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            var lower = char.ToLowerInvariant(c);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9')
                || (lower >= '\u0600' && lower <= '\u06FF') || (c >= '\u0660' && c <= '\u0669'))
                sb.Append(lower);
        }

        return sb.ToString();
    }

    private static string? ReadString(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record ParsedEvidenceItem(ChunkId ChunkId, string DimensionId, string Competency, string QuoteText, double Confidence);
}

internal static class StringEng
{
    public static string? IfNotEmpty(this string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
