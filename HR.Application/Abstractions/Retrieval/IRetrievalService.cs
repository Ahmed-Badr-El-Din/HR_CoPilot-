using HR.Domain.Common;
using HR.Domain.Documents;

namespace HR.Application.Abstractions.Retrieval;

public sealed record RetrievalQuery(
    string Text,
    int TopK = 8,
    IReadOnlyList<DocumentId>? DocumentIds = null,
    IReadOnlyList<string>? Tags = null,
    DocLanguage? Language = null,
    bool ExpandBilingual = true);

public sealed record RetrievedChunk(
    ChunkId ChunkId,
    DocumentId DocumentId,
    string DocumentTitle,
    DocLanguage Language,
    string Section,
    string PageReference,
    string Text,
    double Score);

public sealed record RetrievalResult(
    IReadOnlyList<RetrievedChunk> Chunks,
    double? DenseConfidence,
    double KeywordConfidence,
    string Strategy);

public interface IRetrievalService
{
    Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default);
}

/// <summary>Handles token budgets and cost routing (used by ingestion and generation).</summary>
public interface IBilingualTextService
{
    DocLanguage DetectLanguage(string text);
    string Normalize(string text);
}
