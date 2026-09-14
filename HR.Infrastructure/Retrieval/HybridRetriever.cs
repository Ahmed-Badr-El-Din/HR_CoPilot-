using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Infrastructure.Nlp;

namespace HR.Infrastructure.Retrieval;

/// <summary>
/// FR-2 hybrid retrieval. Dense (embedding cos-sim via IVectorStore) and keyword
/// (BM25-flavoured, bilingual) results are fused with Reciprocal Rank Fusion.
/// Justified enhancement (see ADR-003): bilingual query expansion — the query is
/// translated into the counterpart script and searched in both, so an English
/// question can find Arabic curriculum-vitae chunks (Twist T1) on any provider.
/// </summary>
public sealed class HybridRetriever(
    IVectorStore vectorStore,
    IModelProvider provider,
    IChunkLookup chunkLookup,
    KeywordIndex keywordIndex,
    IArabicNormalizer normalizer) : IRetrievalService
{
    private const int RrfConstant = 60;

    public async Task<RetrievalResult> RetrieveAsync(RetrievalQuery query, CancellationToken ct)
    {
        var queryVector = await EmbedAsync(query.Text, ct);
        var normalized = normalizer.Normalize(query.Text);

        // Bilingual expansion (T1): search the counterpart script too.
        var expanded = new List<string> { normalized };
        var targetAr = !normalizer.ContainsArabic(query.Text);
        var counterpart = BilingualLexicon.Expand(query.Text, targetAr);
        if (!string.IsNullOrWhiteSpace(counterpart) && counterpart != normalized)
            expanded.Add(counterpart);

        var docIds = query.DocumentIds;
        IReadOnlyList<DocumentId>? filter = query.DocumentIds;

        // ---- dense ---- 
        var dense = await vectorStore.SearchAsync(queryVector, query.TopK * 4, filter, ct);

        // ---- keyword ----
        var keyword = new List<KeywordHit>();
        foreach (var q in expanded.Distinct())
        {
            var hits = await keywordIndex.SearchAsync(q, query.TopK * 4, filter, query.Language, ct);
            keyword.AddRange(hits);
        }

        var fused = RerankFuse(dense, keyword, query.TopK);

        var chunks = new List<RetrievedChunk>();
        foreach (var (chunkId, score) in fused)
        {
            var doc = await chunkLookup.GetChunkAsync(chunkId, ct);
            if (doc is null) continue;
            chunks.Add(new RetrievedChunk(
                doc.Id,
                doc.DocumentId,
                doc.DocumentTitle,
                doc.Language,
                doc.Section,
                doc.PageReference,
                doc.Text,
                score));
        }

        var denseConfidence = dense.Count > 0 ? dense.Max(h => h.Score) : 0;
        var keywordConfidence = keyword.Count > 0 ? keyword.Max(h => h.Score) : 0;
        var strategy = "hybrid-rrf" + (expanded.Count > 1 ? "+bilingual-expansion" : string.Empty);
        return new RetrievalResult(chunks, denseConfidence, keywordConfidence, strategy);
    }

    private static List<(ChunkId ChunkId, double Score)> RerankFuse(
        IReadOnlyList<VectorHit> dense,
        IReadOnlyList<KeywordHit> keyword,
        int topK)
    {
        var rankMap = new Dictionary<ChunkId, double>();
        void AddScore(ChunkId id, double rrfScore)
        {
            rankMap[id] = rankMap.GetValueOrDefault(id) + rrfScore;
        }

        var denseRank = 1;
        foreach (var d in dense.DistinctBy(d => d.ChunkId)) AddScore(d.ChunkId, 1.0 / (RrfConstant + denseRank++));
        var keywordRank = 1;
        foreach (var k in keyword.DistinctBy(k => k.ChunkId)) AddScore(k.ChunkId, 1.0 / (RrfConstant + keywordRank++));

        return rankMap
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => (kv.Key, RrfToProbability(kv.Value)))
            .ToList();
    }

    /// <summary>
    /// Maps summed RRF contributions to a 0..1 confidence. A chunk ranked first by
    /// both the dense and keyword lists is the maximum possible (2 / (k+1)) and maps
    /// to 1.0; a chunk that is first in only one list maps to 0.5. Without this
    /// normalisation the raw RRF sum tops out near 0.20, so the refusal threshold
    /// could never be reached and every question would be refused.
    /// </summary>
    private static double RrfToProbability(double rrfSum)
        => Math.Min(1.0, rrfSum * (RrfConstant + 1) / 2.0);

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
        => (await provider.EmbedManyAsync(new[] { text }, null, ct)).Vectors[0];
}

public sealed record ChunkRecord(
    ChunkId Id,
    DocumentId DocumentId,
    string DocumentTitle,
    DocLanguage Language,
    string Section,
    string PageReference,
    string Text);

public interface IChunkLookup
{
    Task<ChunkRecord?> GetChunkAsync(ChunkId id, CancellationToken ct = default);
}
