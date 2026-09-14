using HR.Domain.Common;
using HR.Domain.Documents;

namespace HR.Infrastructure.Retrieval;

public sealed record KeywordHit(ChunkId ChunkId, DocumentId DocumentId, double Score);

public abstract class ChunkSource
{
    public abstract Task<long> GetChunkCountAsync(CancellationToken ct = default);
    public abstract Task<IReadOnlyList<DocumentChunk>> GetAllChunksAsync(CancellationToken ct = default);
}

/// <summary>
/// In-memory BM25-flavoured keyword index. The key twist: terms are tokenised from
/// already-normalised text (Arabic diacritics stripped, alef/hamza/taa-marbuta
/// folded), so keyword search works in both scripts. Rebuilt lazily whenever the
/// stored corpus changes (cheap and always correct for this corpus size).
/// </summary>
public sealed class KeywordIndex(ChunkSource source)
{
    private sealed class Entry
    {
        public required ChunkId ChunkId;
        public required DocumentId DocumentId;
        public required Dictionary<string, int> TermFreq;
        public int Length;
        public required DocLanguage Language;
    }

    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, int> _docFreq = new(StringComparer.Ordinal);
    private long _entryCount = -1;

    public async Task<IReadOnlyList<KeywordHit>> SearchAsync(
        string normalizedQuery,
        int topK,
        IReadOnlyCollection<DocumentId>? documentIds,
        DocLanguage? language,
        CancellationToken ct)
    {
        await RefreshIfChangedAsync(ct);
        lock (_lock)
        {
            var terms = Tokenize(normalizedQuery);
            if (terms.Count == 0 || _entries.Count == 0) return Array.Empty<KeywordHit>();

            const double k1 = 1.2;
            const double b = 0.75;
            var avgLength = _entries.Average(e => (double)e.Length);

            var scored = new List<(Entry Entry, double Score)>();
            foreach (var entry in _entries)
            {
                if (documentIds is not null && !documentIds.Contains(entry.DocumentId)) continue;
                if (language is not null && entry.Language != language) continue;

                double score = 0;
                foreach (var t in terms)
                {
                    if (!entry.TermFreq.TryGetValue(t, out var tf)) continue;
                    var df = _docFreq.TryGetValue(t, out var d) ? d : 1;
                    var idf = Math.Log(1 + ((_entries.Count - df + 0.5) / (df + 0.5)));
                    var dl = Math.Max(entry.Length, 1);
                    score += idf * ((tf * (k1 + 1)) / (tf + k1 * (1 - b + (b * dl / avgLength))));
                }

                if (score > 0) scored.Add((entry, score));
            }

            return scored
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .Select(s => new KeywordHit(s.Entry.ChunkId, s.Entry.DocumentId, s.Score))
                .ToArray();
        }
    }

    private async Task RefreshIfChangedAsync(CancellationToken ct)
    {
        var total = await source.GetChunkCountAsync(ct);
        if (total == _entryCount) return;

        var chunks = await source.GetAllChunksAsync(ct);
        lock (_lock)
        {
            _entries.Clear();
            _docFreq.Clear();
            foreach (var chunk in chunks)
            {
                var terms = Tokenize(chunk.Text);
                var tf = terms.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
                foreach (var t in tf.Keys)
                    _docFreq[t] = _docFreq.GetValueOrDefault(t) + 1;

                _entries.Add(new Entry
                {
                    ChunkId = chunk.Id,
                    DocumentId = chunk.DocumentId,
                    TermFreq = tf,
                    Length = terms.Count,
                    Language = chunk.Language,
                });
            }

            _entryCount = _entries.Count;
        }
    }

    public static List<string> Tokenize(string normalizedText)
    {
        var split = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tokens = new List<string>();
        foreach (var word in split)
        {
            tokens.Add(word);
            if (word.Length > 5) tokens.Add(word[..5]);
        }

        return tokens;
    }
}
