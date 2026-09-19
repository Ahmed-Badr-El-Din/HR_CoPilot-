using System.Text;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Workflows;
using HR.Domain.Common;
using HR.Domain.Sessions;

namespace HR.Evaluation;

public sealed record CaseResult(
    string Id,
    string Language,
    string Expectation,
    bool Refused,
    bool Answered,
    int CitationCount,
    string? ExpectedDocument,
    bool? RetrievedExpectedAt5,
    int? ExpectedRank,
    bool Leaked,
    double Groundedness,
    long LatencyMs,
    string Answer);

public sealed record LanguageMetrics(
    string Language,
    int Cases,
    int RetrievalCases,
    double HitAt1,
    double HitAt5,
    double Mrr,
    double AnswerRate,
    double RefusalAccuracy,
    double Groundedness,
    double CitationCoverage);

public sealed record EvalReport(
    DateTimeOffset GeneratedAt,
    string Provider,
    int CorpusDocuments,
    int CorpusPages,
    int TotalCases,
    int AdversarialCases,
    double DirectInjectionRefusalRate,
    double LeakRate,
    double AverageLatencyMs,
    IReadOnlyList<LanguageMetrics> ByLanguage,
    LanguageMetrics Overall,
    IReadOnlyList<CaseResult> Cases);

/// <summary>
/// Runs the golden set through the real retrieval and ask pipelines and produces
/// honest metrics. Nothing is simulated: the corpus is seeded through the normal
/// ingestion path, retrieval is the production hybrid retriever, and answers come
/// from the configured provider (offline local provider by default).
/// </summary>
public sealed class EvaluationRunner(
    IRetrievalService retriever,
    AskService ask,
    IHrUnitOfWork store,
    ICorrelationContext context)
{
    private const int TopK = 8;

    public async Task<EvalReport> RunAsync(IReadOnlyList<GoldenCase> cases, string providerName, int corpusDocs, int corpusPages, CancellationToken ct = default)
    {
        var session = new ChatSession { OwnerUserId = context.UserId, Title = "evaluation", PreferredLanguage = "auto" };
        await store.Sessions.AddAsync(session, ct);
        await store.SaveChangesAsync(ct);

        var results = new List<CaseResult>();
        foreach (var testCase in cases)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await RunCaseAsync(testCase, session.Id, providerName, ct));
        }

        return Aggregate(results, providerName, corpusDocs, corpusPages);
    }

    private async Task<CaseResult> RunCaseAsync(GoldenCase testCase, SessionId sessionId, string providerName, CancellationToken ct)
    {
        var retrieval = await retriever.RetrieveAsync(new RetrievalQuery(testCase.Question, TopK, Language: null), ct);
        int? rank = null;
        if (testCase.ExpectedDocument is not null)
        {
            var index = retrieval.Chunks
                .Select((c, i) => (c.DocumentTitle, i))
                .Where(x => string.Equals(x.DocumentTitle, testCase.ExpectedDocument, StringComparison.OrdinalIgnoreCase))
                .Select(x => (int?)x.i + 1)
                .FirstOrDefault();
            rank = index;
        }

        var runId = await ask.StartAsync(sessionId, testCase.Question, testCase.Language, context, ct);
        var answer = new StringBuilder();
        var refused = false;
        var citations = 0;
        var start = DateTimeOffset.UtcNow;
        await foreach (var ev in ask.StreamAsync(sessionId, testCase.Question, testCase.Language, context, runId, ct))
        {
            switch (ev.Kind)
            {
                case AskEventKind.Token:
                    answer.Append(ev.Text);
                    break;
                case AskEventKind.Refused:
                    refused = true;
                    break;
                case AskEventKind.Citation:
                    citations++;
                    break;
            }
        }

        var latency = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
        var text = answer.ToString();
        var leaked = testCase.ForbiddenSubstrings?.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)) ?? false;
        var grounded = Groundedness(text, retrieval.Chunks);

        return new CaseResult(
            testCase.Id,
            testCase.Language,
            testCase.Expectation.ToString(),
            refused,
            !refused && text.Length > 0,
            citations,
            testCase.ExpectedDocument,
            rank is <= 5,
            rank,
            leaked,
            grounded,
            latency,
            text.Length <= 400 ? text : text[..400]);
    }

    private static double Groundedness(string answer, IReadOnlyList<RetrievedChunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(answer) || chunks.Count == 0) return 0;
        var answerTokens = Tokenize(answer);
        if (answerTokens.Count == 0) return 0;
        var contextTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks) contextTokens.UnionWith(Tokenize(chunk.Text));
        var supported = answerTokens.Count(t => contextTokens.Contains(t));
        return Math.Round((double)supported / answerTokens.Count, 4);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c)) buffer.Append(c);
            else if (buffer.Length > 0)
            {
                tokens.Add(buffer.ToString());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0) tokens.Add(buffer.ToString());
        return tokens;
    }

    private static EvalReport Aggregate(IReadOnlyList<CaseResult> results, string provider, int corpusDocs, int corpusPages)
    {
        var direct = results.Where(r => r.Id.StartsWith("adv-direct", StringComparison.Ordinal)).ToList();
        var adversarial = results.Where(r => r.Id.StartsWith("adv-", StringComparison.Ordinal)).ToList();
        var byLanguage = new[] { "en", "ar" }
            .Select(lang => MetricsFor(lang, results.Where(r => r.Language == lang).ToList()))
            .ToList();
        var overall = MetricsFor("overall", results);

        return new EvalReport(
            DateTimeOffset.UtcNow,
            provider,
            corpusDocs,
            corpusPages,
            results.Count,
            adversarial.Count,
            Ratio(direct, r => r.Refused),
            adversarial.Count == 0 ? 0 : Math.Round(adversarial.Count(r => r.Leaked) / (double)adversarial.Count, 4),
            Math.Round(results.Average(r => r.LatencyMs), 1),
            byLanguage,
            overall,
            results);
    }

    private static LanguageMetrics MetricsFor(string label, IReadOnlyList<CaseResult> subset)
    {
        var retrieval = subset.Where(r => r.ExpectedDocument is not null).ToList();
        var answerable = subset.Where(r => r.Expectation == nameof(Expectation.Answer)).ToList();
        var unanswerable = subset.Where(r => r.Expectation == nameof(Expectation.Refuse)).ToList();
        var answered = answerable.Where(r => r.Answered).ToList();
        var ranked = retrieval.Where(r => r.ExpectedRank is not null).ToList();

        return new LanguageMetrics(
            label,
            subset.Count,
            retrieval.Count,
            Ratio(retrieval, r => r.ExpectedRank == 1),
            Ratio(retrieval, r => r.RetrievedExpectedAt5 == true),
            ranked.Count == 0 ? 0 : Math.Round(ranked.Average(r => 1.0 / r.ExpectedRank!.Value), 4),
            Ratio(answerable, r => r.Answered),
            Ratio(unanswerable, r => r.Refused),
            answered.Count == 0 ? 0 : Math.Round(answered.Average(r => r.Groundedness), 4),
            answered.Count == 0 ? 0 : Math.Round(answered.Count(r => r.CitationCount > 0) / (double)answered.Count, 4));
    }

    private static double Ratio(IReadOnlyList<CaseResult> subset, Func<CaseResult, bool> predicate)
        => subset.Count == 0 ? 0 : Math.Round(subset.Count(predicate) / (double)subset.Count, 4);
}

