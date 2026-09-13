using System.Text;

namespace HR.Infrastructure.Embedding;

/// <summary>
/// Deterministic, offline, script-agnostic text embedding: hash character n-grams
/// (and word shingles) into a signed count vector with L2 normalisation. Works for
/// Arabic and English without any hosted model, which is what makes the free/local
/// path of this project actually runnable. Deterministic by construction, so
/// retrieval is reproducible across restarts.
/// </summary>
public sealed class HashingEmbedder(int dimensions = 384)
{
    private int Dimensions { get; } = dimensions;

    public float[] Embed(string text)
    {
        var normalized = HR.Infrastructure.Nlp.TextNormalizer.Normalize(text);
        var vector = new float[Dimensions];
        int count = 0;
        bool isArabic = HR.Infrastructure.Nlp.TextNormalizer.ContainsArabic(normalized);

        void AddFeature(string feature)
        {
            if (feature.Length == 0) return;
            var hash = StableHash(feature);
            var bucket = Math.Abs(hash % Dimensions);
            var sign = hash < 0 ? -1f : 1f;
            vector[bucket] += sign;
            count++;
        }

        // Whole words (both scripts) — captures exact HR terminology.
        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            AddFeature(word);

        // Arabic character bigrams+trigrams capture morphology without stemming.
        if (isArabic)
        {
            AddShingles(normalized, 2, AddFeature);
            AddShingles(normalized, 3, AddFeature);
        }
        else
        {
            // Latin: char trigrams capture misspellings / inflection, whole words add precision.
            AddShingles(normalized, 3, AddFeature);
        }

        // L2 normalise (never divide by zero).
        var norm = 0f;
        foreach (var v in vector) norm += v * v;
        norm = MathF.Sqrt(norm);
        if (norm > 1e-9f)
        {
            for (var i = 0; i < Dimensions; i++) vector[i] /= norm;
        }

        return vector;
    }

    private static void AddShingles(string text, int length, Action<string> add)
    {
        var chars = text.Where(c => c != ' ').ToArray();
        for (var i = 0; i + length <= chars.Length; i++)
        {
            var shingle = new string(chars, i, length);
            if (shingle.Length == length) add(shingle);
        }
    }

    /// <summary>FNV-1a (64-bit) with a salt, deterministic across processes.</summary>
    internal static long StableHash(string feature)
    {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var c in feature)
        {
            hash ^= c;
            hash *= prime;
        }

        // Mix so that similar-but-different shingles spread; second pass over bytes.
        var bytes = Encoding.UTF8.GetBytes(feature);
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= prime;
        }

        return (long)(hash ^ (hash >> 32));
    }
}
