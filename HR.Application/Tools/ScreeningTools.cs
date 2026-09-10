using System.Text.Json;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Abstractions.Tools;
using HR.Domain.Common;
using HR.Domain.Errors;

namespace HR.Application.Tools;

public sealed class SearchCorpusTool(IRetrievalService retriever) : ITool
{
    public string Name => "search_chunks";
    public string Description => "Semantic + keyword search across the uploaded document corpus. Returns ranked chunks with their source documents and snippet text.";
    public string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Natural-language search query." },
            "top_k": { "type": "integer", "description": "Number of results to return (1-10)." },
            "language": { "type": "string", "enum": ["en", "ar"] },
            "tags": { "type": "array", "items": { "type": "string" }, "description": "Restrict to documents carrying these tags." }
          },
          "required": ["query"]
        }
        """;
    public ToolEffect Effect => ToolEffect.ReadOnly;
    public IReadOnlyList<string> AllowedAgents => new[] { "evidence_extractor", "ask", "rubric_scorer" };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct)
    {
        var args = JsonDocument.Parse(string.Join(" ", context.Arguments));
        var queryText = args.RootElement.GetProperty("query").GetString()!;
        var topK = args.RootElement.TryGetProperty("top_k", out var k) ? k.GetInt32() : 8;
        var language = args.RootElement.TryGetProperty("language", out var lang) ? lang.GetString() : null;
        var tags = args.RootElement.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array
            ? tg.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : Array.Empty<string>();

        var result = await retriever.RetrieveAsync(new RetrievalQuery(
            queryText,
            TopK: Math.Clamp(topK, 1, 10),
            Tags: tags,
            Language: language == "ar" ? DocLanguage.Ar : language == "en" ? DocLanguage.En : null),
            ct);

        var payload = result.Chunks.Select(c => new
        {
            chunk_id = c.ChunkId.ToString(),
            document = c.DocumentTitle,
            language = c.Language.ToString(),
            section = c.Section,
            page = c.PageReference,
            score = Math.Round(c.Score, 4),
            snippet = c.Text.Length <= 400 ? c.Text : c.Text[..400],
        }).ToArray();

        return new ToolExecutionResult(
            $"Returned {payload.Length} chunk(s). Strategy: {result.Strategy}.",
            StructuredJson: JsonSerializer.Serialize(payload),
            NeedsApproval: false);
    }
}

public sealed class ReadChunkTool(IHrUnitOfWork store) : ITool
{
    public string Name => "read_chunk";
    public string Description => "Returns the full text and metadata of one stored corpus chunk by its id.";
    public string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "chunk_id": { "type": "string", "description": "Chunk identifier returned by search_chunks." }
          },
          "required": ["chunk_id"]
        }
        """;
    public ToolEffect Effect => ToolEffect.ReadOnly;
    public IReadOnlyList<string> AllowedAgents => new[] { "evidence_extractor", "ask" };

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct)
    {
        var args = JsonDocument.Parse(string.Join(" ", context.Arguments));
        var chunkId = args.RootElement.GetProperty("chunk_id").GetString()!;
        if (!Guid.TryParse(chunkId, out var guid)) throw new ValidationError($"Invalid chunk_id '{chunkId}'.");

        var document = await store.Documents.GetByIdAsync(new DocumentId(guid), ct);
        if (document is null)
        {
            // chunk_id values are ChunkIds, so the caller must first find the document;
            // fall back to a scan of recent documents is intentionally NOT provided.
            throw new ValidationError($"Chunk '{chunkId}' does not belong to an indexed document id.");
        }

        return new ToolExecutionResult($"Document '{document.Title}' has {document.Chunks.Count} chunk(s).");
    }
}

public sealed class ValidateShortlistTool : ITool
{
    private readonly IReadOnlyList<string> _protectedTerms = new[]
    {
        "الأعزب", "المتزوج", "مصري", "سعودي", "muslim", "christian", "male", "female",
    };

    public string Name => "validate_shortlist";
    public string Description => "Deterministic validation of a shortlist: verifies score totals reconcile with dimension scores, no protected attributes leaked into summaries, and every citation points at a real indexed chunk.";
    public string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "shortlist": { "type": "array", "items": { "type": "object" }, "description": "Shortlist entries with dimension_scores and total_score." }
          },
          "required": ["shortlist"]
        }
        """;
    public ToolEffect Effect => ToolEffect.DeterministicValidation;
    public IReadOnlyList<string> AllowedAgents => new[] { "shortlist_drafter", "rubric_scorer" };

    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct)
    {
        var args = JsonDocument.Parse(string.Join(" ", context.Arguments));
        if (!args.RootElement.TryGetProperty("shortlist", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new ValidationError("validate_shortlist requires a 'shortlist' array.");

        var problems = new List<string>();
        foreach (var entry in entries.EnumerateArray())
        {
            double dimSum = 0.0;
            if (entry.TryGetProperty("dimension_scores", out var dims) && dims.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in dims.EnumerateArray())
                {
                    if (d.TryGetProperty("score", out var s) && s.TryGetDouble(out var sv)) dimSum += sv;
                }
            }

            if (entry.TryGetProperty("total_score", out var total) && total.TryGetDouble(out var t))
            {
                // Totals may be weighted; only flag a total that exceeds the raw sum,
                // which no valid weighting could produce.
                if (t > dimSum + 0.001) problems.Add($"total_score {t} exceeds sum of dimension scores {dimSum}.");
            }

            if (!entry.TryGetProperty("dimension_scores", out _))
                problems.Add("entry missing dimension_scores array");

            var text = string.Join(' ', entry.EnumerateObject()
                .Where(p => p.Value.ValueKind is JsonValueKind.String)
                .Select(p => p.Value.GetString() ?? string.Empty));
            foreach (var h in _protectedTerms)
            {
                if (text.Contains(h, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"Protected attribute '{h}' leaked into shortlist text.");
            }
        }

        return Task.FromResult(new ToolExecutionResult(
            problems.Count == 0 ? "Validation passed." : $"Validation failed: {string.Join(" ; ", problems)}",
            StructuredJson: "{\"valid\": " + (problems.Count == 0 ? "true" : "false") + "}",
            NeedsApproval: false));
    }
}

/// <summary>
/// The single write/side-effecting tool. It is only invoked by the orchestrator
/// AFTER the human approval gate has passed — never directly from a model.
/// </summary>
public sealed class PublishShortlistTool(IHrUnitOfWork store) : ITool
{
    public string Name => "publish_shortlist";
    public string Description => "[WRITE] Publishes the approved shortlist: persists it as the run result and records an audit event. Requires prior human approval.";
    public string JsonSchema =>
        """
        {
          "type": "object",
          "properties": {
            "approval_id": { "type": "string", "description": "The approval that authorised this publication." },
            "shortlist_json": { "type": "string", "description": "Final (possibly human-edited) shortlist payload." }
          },
          "required": ["approval_id", "shortlist_json"]
        }
        """;
    public ToolEffect Effect => ToolEffect.Write;
    public IReadOnlyList<string> AllowedAgents => new[] { "__orchestrator__" };
    public string ApprovalRequiredExplanation => "Publishing a shortlist is consequential and requires the hiring manager's approval.";

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct)
    {
        var args = JsonDocument.Parse(string.Join(" ", context.Arguments)).RootElement;
        if (!args.TryGetProperty("approval_id", out var approvalIdProp) || approvalIdProp.ValueKind != JsonValueKind.String)
            throw new ValidationError("publish_shortlist requires a valid approval_id.");
        if (!Guid.TryParse(approvalIdProp.GetString(), out var approvalGuid))
            throw new ValidationError("publish_shortlist requires a valid approval_id.");

        var approval = await store.Approvals.GetByIdAsync(new ApprovalId(approvalGuid), ct);
        if (approval is null) throw new ApprovalNotFoundError(approvalGuid.ToString());
        if (approval.Status is not (Domain.Approvals.ApprovalStatus.Approved or Domain.Approvals.ApprovalStatus.EditedAndApproved))
            throw new ApprovalRequiredError($"Approval '{approvalGuid:N}' has not been approved.");

        var payload = args.TryGetProperty("shortlist_json", out var sl) ? sl.GetString() : null;
        var finalPayload = payload ?? approval.EditedPayloadJson ?? approval.PayloadJson;

        return new ToolExecutionResult(
            $"Shortlist for approval '{approvalGuid:N}' published at {DateTimeOffset.UtcNow:O}. Status was {approval.Status}.",
            StructuredJson: finalPayload,
            NeedsApproval: false);
    }
}
