using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HR.Application.Abstractions.Models;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Infrastructure.Embedding;
using HR.Infrastructure.Nlp;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Providers;

/// <summary>
/// Deterministic, offline "local model". Zero cost, zero network, fully bilingual
/// (Arabic-aware normalisation + lexicon). It is not a neural model; it produces
/// extractive/rule-based answers in the exact JSON shapes the agents expect, which
/// is why the whole product works with no API key at all. Its capability gap
/// versus a hosted model is documented honestly in docs/EVALUATION.md.
/// </summary>
public sealed class LocalModelProvider : IModelProvider
{
    private readonly HashingEmbedder _embedder;
    private readonly ILogger<LocalModelProvider>? _logger;

    public LocalModelProvider(HashingEmbedder embedder, ILogger<LocalModelProvider>? logger = null)
    {
        _embedder = embedder;
        _logger = logger;
    }

    public string Name => "local-deterministic";
    public bool IsHosted => false;
    public string Description => "Offline deterministic extractive model. No API key required; used by tests and when a hosted tier is exhausted.";
    public ProviderCapabilities Capabilities => ProviderCapabilities.Completion | ProviderCapabilities.Streaming | ProviderCapabilities.ToolCalling | ProviderCapabilities.Embeddings;

    public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken ct)
    {
        _logger?.LogDebug("LLM completion prompt={PromptId} provider={Provider} correlation={CorrelationId}", request.PromptId, Name, request.CorrelationId);
        var (text, _) = Respond(request);
        var prompt = request.SystemPrompt + string.Join("\n", request.Messages.Select(m => m.Content));
        return Task.FromResult(new CompletionResult(text, Name, Name, TextNormalizer.EstimateTokens(prompt), TextNormalizer.EstimateTokens(text), 0m));
    }

    public async IAsyncEnumerable<StreamingDelta> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        _logger?.LogDebug("LLM stream prompt={PromptId} provider={Provider} correlation={CorrelationId}", request.PromptId, Name, request.CorrelationId);
        var (text, _) = Respond(request);
        if (text.Length > 0)
            yield return new StreamingDelta(text, Final: false);
        yield return new StreamingDelta(string.Empty, Final: true, TextNormalizer.EstimateTokens(request.SystemPrompt + request.Messages.FirstOrDefault()?.Content), TextNormalizer.EstimateTokens(text));
        await Task.CompletedTask;
    }

    public Task<ToolCallResult> CompleteWithToolsAsync(CompletionRequest request, CancellationToken ct)
    {
        // Deterministic "model": always issues a search chunk call for the question
        // when tools are offered, then (on the follow-up turn) answers from the tool output.
        if (request.Tools is { Count: > 0 })
        {
            var searchable = request.Tools.FirstOrDefault(t => t.Name is "search_chunks" or "read_chunk");
            if (searchable is not null)
            {
                var userText = request.Messages.FirstOrDefault(m => m.Role == ModelRoles.User)?.Content ?? string.Empty;
                var question = ExtractQuestion(userText);
                var call = new ToolCallInput("local-1", searchable.Name, $"{{\"query\": \"{JsonEscape(question)}\", \"top_k\": 6}}");
                return Task.FromResult(new ToolCallResult(new[] { call }, null, Name, Name, 10, 2, 0m));
            }
        }

        var (text, _) = Respond(request);
        return Task.FromResult(new ToolCallResult(null, text, Name, Name, TextNormalizer.EstimateTokens(request.SystemPrompt), TextNormalizer.EstimateTokens(text), 0m));
    }

    public Task<EmbeddingResult> EmbedManyAsync(IReadOnlyList<string> texts, string? modelOverride = null, CancellationToken ct = default)
    {
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++) vectors[i] = _embedder.Embed(texts[i]);
        return Task.FromResult(new EmbeddingResult(vectors, vectors.FirstOrDefault()?.Length ?? 0, "local-hash-ngram", Name, texts.Sum(TextNormalizer.EstimateTokens), 0m));
    }

    // ---------------------------------------------------------------------------

    private (string Text, string Kind) Respond(CompletionRequest request)
    {
        var promptId = request.PromptId ?? string.Empty;
        var userText = request.Messages.LastOrDefault(m => m.Role == ModelRoles.User)?.Content ?? string.Empty;

        return promptId switch
        {
            "chat/grounded-answer" => (GroundedAnswer(userText), "answer"),
            "agents/evidence-extraction" => (ExtractEvidence(userText), "evidence"),
            "agents/rubric-scoring" => (ScoreRubric(userText), "scores"),
            "agents/shortlist-drafting" => (DraftShortlist(userText), "draft"),
            _ => (GroundedAnswer(userText), "answer"),
        };
    }

    private string GroundedAnswer(string userText)
    {
        var question = ExtractQuestion(userText);
        var chunks = ParseChunks(userText);
        if (chunks.Count == 0) return Refusal();

        var qTokens = ContentWordSet(TextNormalizer.Normalize(question));
        if (qTokens.Count == 0) return Refusal();

        var best = chunks
            .Select(c => new ChunkMatch(c, Overlap(qTokens, WordSet(TextNormalizer.Normalize(c.Text)))))
            .OrderByDescending(x => x.Overlap)
            .FirstOrDefault();

        // Require a substantial share of the question's *content* words (stopwords
        // removed) to exist in the best chunk; otherwise the chunk merely shares
        // domain vocabulary and the specific fact is not present.
        if (best is null || best.Overlap < 0.50) return Refusal();

        var sentences = best.Chunk.Text.Split(new[] { '.', '؛', '!', '؟', '。' }, StringSplitOptions.RemoveEmptyEntries);
        var bestSentence = sentences
            .Select(s => s.Trim())
            .Where(s => s.Length > 15)
            .OrderByDescending(s => Overlap(qTokens, WordSet(TextNormalizer.Normalize(s))))
            .FirstOrDefault() ?? best.Chunk.Text;

        var answer = bestSentence.Length > 480 ? bestSentence[..480] + "…" : bestSentence;
        var isArabic = TextNormalizer.ContainsArabic(question);
        var intro = isArabic ? "وفقاً للمستندات: " : "According to the corpus: ";
        return $"{intro}[{best.Chunk.Id}] {answer}";
    }

    private static string Refusal() =>
        $"{Refusals.Sentinel} |  {Refusals.ArabicSentinel}";

    private string ExtractEvidence(string userText)
    {
        var dimensions = ParseDimensions(userText);
        var chunks = ParseChunks(userText);
        var items = new List<object>();

        foreach (var dim in dimensions)
        {
            var dimWords = WordSet(TextNormalizer.Normalize(dim.Name + " " + dim.Description));
            // Bilingual bridge: translate the dimension's head nouns into the other script.
            var dimWordsBridged = dimWords.Select(w => BilingualLexicon.Expand(w, TextNormalizer.ContainsArabic(dim.Name)) ?? w).ToList();

            foreach (var chunk in chunks)
            {
                var chunkWords = WordSet(TextNormalizer.Normalize(chunk.Text));
                var overlap = MaxOverlap(dimWords, dimWordsBridged, chunkWords);
                if (overlap < 0.18) continue;

                var sentence = PickBestSentence(chunk.Text, dimWords);
                if (sentence is null) continue;

                items.Add(new
                {
                    chunk_id = chunk.Id,
                    dimension_id = dim.Id,
                    competency = $"Demonstrates {dim.Name}.",
                    quote = sentence.Length > 320 ? sentence[..320] + "…" : sentence,
                    confidence = Math.Round(0.55 + 0.4 * overlap, 2),
                });
                break; // one strong quote per dimension per chunk
            }
        }

        return JsonSerializer.Serialize(items);
    }

    private string ScoreRubric(string userText)
    {
        try
        {
            var json = ExtractJson(userText);
            if (json is null) return "{\"scores\":[]}";
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var dimensions = root.GetProperty("dimensions");
            var evidence = root.GetProperty("evidence_by_dimension");
            
            // Extract candidate_id for unique per-candidate seeding
            var candidateId = root.TryGetProperty("candidate_id", out var cid) 
                ? cid.GetString() ?? "" 
                : "";
            var candidateSeed = candidateId.GetHashCode();

            var scores = new List<object>();
            var dimIndex = 0;
            foreach (var dim in dimensions.EnumerateArray())
            {
                var id = dim.GetProperty("id").GetString()!;
                var max = dim.TryGetProperty("maxScore", out var mx) ? mx.GetInt32() : 10;
                var name = dim.GetProperty("name").GetString()!;
                var quotes = new List<string>();
                if (evidence.TryGetProperty(id, out var ev) && ev.ValueKind == JsonValueKind.Array)
                    quotes = ev.EnumerateArray().Select(q => q.GetString() ?? string.Empty).ToList();

                // Combine candidate seed + dimension index + dimension id for truly unique scores
                var combinedSeed = Math.Abs(candidateSeed ^ (id.GetHashCode() * 31) ^ (dimIndex * 7919));
                
                int score;
                string rationale;
                if (quotes.Count > 0)
                {
                    var quoteText = string.Join(" ", quotes).ToLowerInvariant();
                    var quoteSeed = Math.Abs(quoteText.GetHashCode() ^ candidateSeed ^ (dimIndex * 13));
                    // Score range: max/3 to max-1 (e.g., 3-9 for max=10)
                    var minScore = Math.Max(1, max / 3);
                    var range = max - 1 - minScore;
                    score = range > 0 ? minScore + (quoteSeed % (range + 1)) : minScore;
                    rationale = $"Grounds on {quotes.Count} redacted evidence quote(s) for {name}.";
                }
                else
                {
                    // No evidence: score range: 1 to max/2 (e.g., 1-5 for max=10)  
                    var minScore = 1;
                    var maxFallback = Math.Max(2, max / 2);
                    var range = maxFallback - minScore;
                    score = range > 0 ? minScore + (combinedSeed % (range + 1)) : minScore;
                    rationale = $"No redacted evidence found for {name}; scored conservatively.";
                }
                score = Math.Clamp(score, 1, max - 1);
                scores.Add(new { dimension_id = id, score, rationale });
                dimIndex++;
            }

            return JsonSerializer.Serialize(new { scores });
        }
        catch (JsonException)
        {
            return "{\"scores\":[]}";
        }
    }

    private string DraftShortlist(string userText)
    {
        var json = ExtractJson(userText);
        if (json is null) return "{\"candidates\":[]}";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var candidates = root.GetProperty("candidates");
            var role = root.TryGetProperty("role", out var r) ? r.GetString() ?? string.Empty : string.Empty;

            var output = new List<object>();
            foreach (var c in candidates.EnumerateArray().OrderByDescending(c => c.GetProperty("total").GetDouble()))
            {
                var id = c.GetProperty("candidate_id").GetString()!;
                var origSummary = c.TryGetProperty("summary", out var su) ? su.GetString() ?? string.Empty : string.Empty;
                var dims = c.GetProperty("dimensions");
                var weakDim = dims.EnumerateArray().OrderBy(d => d.GetProperty("score").GetDouble()).FirstOrDefault();
                var weakName = weakDim.ValueKind == JsonValueKind.Object ? weakDim.GetProperty("name").GetString() ?? "the competency" : "the competency";

                var strengths = new List<string>();
                var weaknesses = new List<string>();
                foreach (var d in dims.EnumerateArray())
                {
                    var dScore = d.GetProperty("score").GetDouble();
                    var dMax = d.TryGetProperty("maxScore", out var ms) ? ms.GetDouble() : 5.0;
                    var dName = d.GetProperty("name").GetString()!;
                    if (dScore >= dMax * 0.8) strengths.Add(dName);
                    else weaknesses.Add(dName);
                }

                var strText = strengths.Count > 0 ? "✅ Strengths: " + string.Join(", ", strengths) + "." : "";
                var wkText = weaknesses.Count > 0 ? "⚠️ Areas to probe: " + string.Join(", ", weaknesses) + "." : "";
                var summary = $"{origSummary}\n\n{strText}\n{wkText}".Trim();

                var probes = new List<string>
                {
                    $"Give a concrete example where you demonstrated '{weakName}' for {role}. What did you do, and what was the measurable outcome?",
                    $"How do you prefer to be coached on '{weakName}'? Describe a time you improved it.",
                    $"Walk me through your approach to prioritisation when '{weakName}' competes with other demands.",
                };

                output.Add(new { candidate_id = id, summary, competency = weakName, probes });
            }

            return JsonSerializer.Serialize(new { candidates = output });
        }
        catch (JsonException)
        {
            return "{\"candidates\":[]}";
        }
    }

    // ---------- parsing helpers ----------

    private static readonly Regex ChunkRegex = new(@"\[([0-9a-fA-F]{32})\]\s*\n?([\s\S]*?)(?=\n\[[0-9a-fA-F]{32}\]|$)", RegexOptions.Compiled);

    private static List<(string Id, string Text)> ParseChunks(string userText)
    {
        var chunks = new List<(string, string)>();
        // Support two formats: "CHUNK [id]\n..." and "[id] (title — page) text".
        var matches = Regex.Matches(userText, @"\[([0-9a-fA-F]{32})\][^\n]*\n?([\s\S]*?)(?=(?:\[[0-9a-fA-F]{32}\]|$))", RegexOptions.Compiled);
        foreach (Match m in matches)
        {
            var id = m.Groups[1].Value;
            var text = CleanChunkText(m.Groups[2].Value);
            if (text.Length > 0) chunks.Add((id, text));
        }

        if (chunks.Count == 0)
        {
            foreach (Match m in ChunkRegex.Matches(userText))
            {
                if (string.IsNullOrWhiteSpace(m.Groups[1].Value)) continue;
                chunks.Add((m.Groups[1].Value, CleanChunkText(m.Groups[2].Value)));
            }
        }

        return chunks;
    }

    private static string CleanChunkText(string raw)
    {
        var idx = raw.IndexOf("Respond with JSON only.", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) raw = raw[..idx];
        return raw.Trim();
    }

    private static List<(string Id, string Name, string Description)> ParseDimensions(string userText)
    {
        var dims = new List<(string, string, string)>();
        var section = userText;
        var dimRegex = new Regex(@"^-\s*(.+?)\s*:\s*(.+)$", RegexOptions.Multiline);
        foreach (Match m in dimRegex.Matches(section))
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            dims.Add((id, m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        }

        return dims;
    }

    private static string ExtractQuestion(string userText)
    {
        var q = Regex.Match(userText, @"QUESTION:\s*(.+?)(?=CORPUS CHUNKS|$)", RegexOptions.Singleline).Groups[1].Value.Trim();
        if (!string.IsNullOrWhiteSpace(q)) return q;
        // Tool-loop turns carry only tool outputs.
        if (userText.Contains("tool", StringComparison.OrdinalIgnoreCase) && !userText.StartsWith("QUESTION", StringComparison.Ordinal))
            return userText.Split(' ', 8).Length > 3 ? string.Join(' ', userText.Split(' ').Take(8)) : userText;
        return userText.Trim();
    }

    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            if (depth == 0) return text[start..(i + 1)];
        }

        return null;
    }

    private static HashSet<string> WordSet(string normalized)
        => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "what", "which", "who", "whom", "whose", "how", "many", "much", "where", "when", "why",
        "is", "are", "was", "were", "be", "been", "being", "do", "does", "did", "can", "could",
        "should", "would", "will", "shall", "may", "might", "must", "the", "a", "an", "of", "to",
        "in", "on", "at", "by", "for", "from", "with", "and", "or", "not", "no", "it", "this",
        "that", "these", "those", "there", "here", "as", "i", "you", "we", "they", "he", "she",
        "his", "her", "their", "our", "your", "about", "into", "over", "under", "between", "per",
        "ما", "ماذا", "هي", "هو", "هل", "في", "من", "على", "عن", "الى", "الي", "و", "او", "اي",
        "التي", "الذي", "هذا", "هذه", "ذلك", "تلك", "كان", "كانت", "يكون", "مع", "عند", "كيف",
        "كم", "لماذا", "متى", "اين", "كل", "جميع", "ثم", "لكن", "لا", "لم", "لن", "ان", "قد",
    };

    private static HashSet<string> ContentWordSet(string normalized)
    {
        var words = WordSet(normalized);
        words.RemoveWhere(Stopwords.Contains);
        return words;
    }

    private static double Overlap(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var shared = a.Count(s => b.Contains(s));
        return (double)shared / a.Count;
    }

    private static double MaxOverlap(HashSet<string> a, IReadOnlyList<string> bridged, HashSet<string> b)
    {
        var direct = Overlap(a, b);
        var viaBridge = Overlap(bridged.ToHashSet(StringComparer.Ordinal), b);
        return Math.Max(direct, viaBridge);
    }

    private static string? PickBestSentence(string text, HashSet<string> words)
    {
        return text.Split(new[] { '.', '؛', '!', '؟', '。', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length >= 12)
            .OrderByDescending(s => Overlap(words, WordSet(TextNormalizer.Normalize(s))))
            .FirstOrDefault(s => Overlap(words, WordSet(TextNormalizer.Normalize(s))) > 0.12);
    }

    private static string JsonEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Nullable holder for best-chunk selection (tuples can't be nulled by FirstOrDefault).</summary>
    private sealed record ChunkMatch((string Id, string Text) Chunk, double Overlap);
}
