using System.Runtime.CompilerServices;
using System.Text.Json;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Domain.Errors;
using HR.Domain.Runs;
using HR.Domain.Security;
using HR.Domain.Sessions;

namespace HR.Application.Workflows;

public enum AskEventKind
{
    Starter,
    Token,
    Citation,
    Refused,
    Done,
    Error,
}

public sealed record AskEvent(AskEventKind Kind, string Text, Citation? Citation = null);

/// <summary>
/// Grounded Q&amp;A over the corpus (FR-2 + FR-6). Streaming over an async
/// sequence which the transport layer translates to SSE; client cancellation flows
/// into the cancellation token and actually stops the provider call. Refuses on
/// low evidence rather than guessing.
/// </summary>
public sealed class AskService(IHrUnitOfWork store, IModelProvider provider, IRetrievalService retriever, IPromptCatalog prompts, IClock clock)
{
    private const double RefusalThreshold = 0.36;
    private const int TopK = 8;

    public async Task<RunId> StartAsync(SessionId sessionId, string question, string preferredLanguage, ICorrelationContext ctx, CancellationToken ct)
    {
        var run = new Run
        {
            Kind = RunKind.Ask,
            Status = RunStatus.Running,
            OwnerUserId = ctx.UserId,
            CorrelationId = ctx.CorrelationId,
            RequestJson = JsonSerializer.Serialize(new { question, session_id = sessionId.ToString() }),
        };
        await store.Runs.AddAsync(run);
        await store.SaveChangesAsync(ct);
        return run.Id;
    }

    public async IAsyncEnumerable<AskEvent> StreamAsync(
        SessionId sessionId,
        string question,
        string preferredLanguage,
        ICorrelationContext ctx,
        RunId runId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var run = await store.Runs.GetByIdAsync(runId, ct)
            ?? throw new RunNotFoundError(runId.ToString());
        var session = await store.Sessions.GetByIdAsync(sessionId, ct)
            ?? throw new SessionNotFoundError(sessionId.ToString());

        run.StartedAt ??= clock.UtcNow;
        run.Status = RunStatus.Running;
        await store.Runs.UpdateAsync(run);
        await store.SaveChangesAsync(ct);

        var queryLanguage = ResolveLanguage(preferredLanguage, question);

        // OWASP LLM-01: a direct instruction-override attempt is refused outright.
        if (PromptInjectionDetector.Scan(question).IsSuspicious)
        {
            var injectionRefusal = ComposeInjectionRefusal(queryLanguage);
            await FinishAsync(run, session, injectionRefusal, "refused", Array.Empty<Citation>(), ctx, 0, 0, 0m, ct);
            yield return new AskEvent(AskEventKind.Refused, injectionRefusal);
            yield return new AskEvent(AskEventKind.Done, "refused");
            yield break;
        }

        var retrieval = await retriever.RetrieveAsync(new RetrievalQuery(question, TopK, Language: null), ct);

        yield return new AskEvent(AskEventKind.Starter, retrieval.Strategy);

        var maxScore = retrieval.Chunks.Count > 0 ? retrieval.Chunks.Max(c => c.Score) : 0.0;
        if (maxScore < RefusalThreshold)
        {
            var refusal = ComposeRefusal(queryLanguage);
            await FinishAsync(run, session, refusal, "refused", Array.Empty<Citation>(), ctx, 0, 0, 0m, ct);
            yield return new AskEvent(AskEventKind.Refused, refusal);
            yield return new AskEvent(AskEventKind.Done, "refused");
            yield break;
        }

        var contextBlock = BuildContextBlock(retrieval.Chunks);
        var systemPrompt = prompts.GetPrompt("chat/grounded-answer", 1);
        var userPrompt = $"QUESTION:\n{question}\n\nCORPUS CHUNKS (answer from these; cite the bracket IDs):\n{contextBlock}";
        var request = new CompletionRequest(
            "chat/grounded-answer", systemPrompt, new[] { new ChatMessage(ModelRoles.User, userPrompt) }, Temperature: 0.1, MaxTokens: 900, CorrelationId: ctx.CorrelationId);

        var answerBuilder = new List<string>();
        int promptTokens = 0;
        int completionTokens = 0;
        decimal cost = 0m;
        string providerName = provider.Name;

        // Live token stream, restructured for iterator legality: `yield` is forbidden
        // inside try/catch, so the first MoveNext (where provider outage surfaces) lives
        // in the guarded block, and every subsequent delta is yielded outside it. This
        // keeps true token-level streaming while still degrading to plain RAG when the
        // provider is unavailable (FR-5).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var enumerator = provider.StreamAsync(request, cts.Token).GetAsyncEnumerator(cts.Token);
        string? fallback = null;
        var hasFirst = false;

        try
        {
            // The first MoveNext both primes the stream and surfaces a provider
            // outage. Its delta must still be emitted, otherwise the first token of
            // every answer is silently dropped (the whole answer for extractive
            // providers that return a single delta).
            hasFirst = await enumerator.MoveNextAsync();
            if (!hasFirst)
            {
                await enumerator.DisposeAsync();
                fallback = ExtractiveAnswer(retrieval.Chunks);
            }
        }
        catch (ProviderUnavailableError)
        {
            await enumerator.DisposeAsync();
            fallback = ExtractiveAnswer(retrieval.Chunks);
        }

        if (fallback is not null)
        {
            // Graceful degradation to plain RAG (FR-5): answer directly from the top
            // retrieved chunks, no model involved.
            var plainRag = fallback;
            answerBuilder.Clear();
            answerBuilder.Add(plainRag);
            run.DegradedToPlainRag = true;
            providerName = "plain-rag";
            yield return new AskEvent(AskEventKind.Token, plainRag);
        }
        else
        {
            try
            {
                var delta = enumerator.Current;
                while (true)
                {
                    if (!string.IsNullOrEmpty(delta.Text))
                    {
                        answerBuilder.Add(delta.Text);
                        yield return new AskEvent(AskEventKind.Token, delta.Text);
                    }

                    if (delta.Final)
                    {
                        promptTokens = delta.PromptTokens ?? 0;
                        completionTokens = delta.CompletionTokens ?? 0;
                    }

                    if (!await enumerator.MoveNextAsync()) break;
                    delta = enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
        }

        var citations = retrieval.Chunks.Select(ToCitation).ToArray();

        // A provider may decline at generation time (the local extractive model does
        // when word overlap is too low, and the system prompt instructs hosted models
        // to reply with the sentinel). Promote that to a first-class refusal so the
        // run, the client and the metrics agree that no answer was produced.
        var finalAnswer = string.Concat(answerBuilder);
        if (Refusals.IsRefusal(finalAnswer))
        {
            await FinishAsync(run, session, finalAnswer, "refused", Array.Empty<Citation>(), ctx, promptTokens, completionTokens, cost, ct);
            yield return new AskEvent(AskEventKind.Refused, finalAnswer);
            yield return new AskEvent(AskEventKind.Done, "refused");
            yield break;
        }

        foreach (var c in citations)
            yield return new AskEvent(AskEventKind.Citation, c.DocumentTitle, c);

        await FinishAsync(run, session, finalAnswer, "assistant", citations, ctx, promptTokens, completionTokens, cost, ct);

        yield return new AskEvent(AskEventKind.Done, "done");
    }

    private async Task FinishAsync(
        Run run,
        ChatSession session,
        string answer,
        string messageRole,
        IReadOnlyList<Citation> citations,
        ICorrelationContext ctx,
        int promptTokens,
        int completionTokens,
        decimal cost,
        CancellationToken ct)
    {
        run.Status = RunStatus.Completed;
        run.FinishedAt = clock.UtcNow;
        run.ResultJson = JsonSerializer.Serialize(new { answer, citations });
        await store.Runs.UpdateAsync(run);

        await store.Sessions.AddMessageAsync(new SessionMessage
        {
            SessionId = session.Id,
            Role = messageRole,
            Content = answer,
            CitationsJson = JsonSerializer.Serialize(citations),
            RunId = run.Id,
            Refused = messageRole == "refused",
        });

        await store.Usage.AddAsync(new UsageRecord
        {
            CorrelationId = ctx.CorrelationId,
            RunId = run.Id,
            UserId = ctx.UserId,
            Model = run.DegradedToPlainRag ? "plain-rag" : "chat-route",
            ProviderName = provider.Name,
            Kind = "chat",
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CostUsd = cost,
        });

        await store.SaveChangesAsync(ct);
    }

    private static DocLanguage ResolveLanguage(string preferred, string question)
        => preferred switch
        {
            "ar" => DocLanguage.Ar,
            "en" => DocLanguage.En,
            _ => ContainsArabic(question) ? DocLanguage.Ar : DocLanguage.En,
        };

    private static bool ContainsArabic(string s)
    {
        foreach (var c in s)
            if (c >= '\u0600' && c <= '\u06FF') return true;
        return false;
    }

    private static string ComposeRefusal(DocLanguage language)
        => language == DocLanguage.Ar
            ? "لا توجد معلومات كافية في قاعدة المستندات للإجابة على هذا السؤال. لم يتم العثور على مقطع مصدري يدعم الرد، ولن أخمّن."
            : "Not enough information in the corpus to answer this question. No trustworthy source chunk was retrieved, so I will not guess.";

    private static string ComposeInjectionRefusal(DocLanguage language)
        => language == DocLanguage.Ar
            ? "تم رفض الطلب لأنه يحتوي على تعليمات تحاول تجاوز قواعد النظام. لا أنفذ أوامر مضمّنة في الرسائل أو المستندات."
            : "Request refused: it contains instructions that attempt to override the system rules. I do not execute instructions embedded in messages or documents.";

    private static string BuildContextBlock(IReadOnlyList<RetrievedChunk> chunks)
        => string.Join("\n\n", chunks.Select(c => $"[{c.ChunkId}] ({c.DocumentTitle} — {c.PageReference}) {PromptInjectionDetector.Neutralize(c.Text)}"));

    private static Citation ToCitation(RetrievedChunk c)
        => new(c.DocumentId, c.ChunkId, c.DocumentTitle, c.Section, c.PageReference, c.Text.Length <= 220 ? c.Text : c.Text[..220], c.Score);

    /// <summary>Plain-RAG fallback: longest sentence of the top chunk.</summary>
    private static string ExtractiveAnswer(IReadOnlyList<RetrievedChunk> chunks)
    {
        var top = chunks.FirstOrDefault();
        if (top is null) return "Not enough information in the corpus.";
        var sentences = top.Text.Split(new[] { '.', '؛', '!', '؟', '。' }, StringSplitOptions.RemoveEmptyEntries);
        var best = sentences.OrderByDescending(s => s.Length).FirstOrDefault() ?? top.Text;
        return best.Trim().Length <= 420 ? best.Trim() : best.Trim()[..420] + "…";
    }
}
