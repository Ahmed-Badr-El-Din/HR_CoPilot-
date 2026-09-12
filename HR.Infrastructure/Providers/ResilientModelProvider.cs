using HR.Application.Abstractions.Models;
using HR.Domain.Errors;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Providers;

/// <summary>
/// Provider abstraction satisfied across multiple implementations and a fallback
/// chain (T2-adjacent by design; required for "free tier runs out"). Each provider
/// is retried with exponential backoff (R-5) before the chain falls through to the
/// next candidate; when no tool-calling provider succeeds the call degrades
/// gracefully to plain completion. Intentionally implements IModelProvider so
/// swapping chains or single providers is pure configuration + one adapter.
/// </summary>
public sealed class ResilientModelProvider(
    IReadOnlyList<IModelProvider> order,
    LlmOptions options,
    ILogger<ResilientModelProvider> logger) : IModelProvider
{
    private int MaxAttempts => Math.Max(1, options.MaxRetries + 1);

    private TimeSpan Backoff(int failedAttempt)
        => TimeSpan.FromMilliseconds(options.RetryBackoffBaseMs * Math.Pow(2, failedAttempt - 1));

    public string Name => $"resilient[{string.Join("|", order.Select(p => p.Name))}]";
    public bool IsHosted => order.Any(p => p.IsHosted);
    public string Description => $"Fallback chain over {order.Count} provider(s): {string.Join(", ", order.Select(p => p.Description))}";
    public ProviderCapabilities Capabilities => order.Aggregate(ProviderCapabilities.None, (acc, p) => acc | p.Capabilities);
    public IReadOnlyList<IModelProvider> Underlying => order;

    private async Task<T> RetryAsync<T>(IModelProvider provider, string operation, Func<Task<T>> action, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                if (attempt < MaxAttempts)
                {
                    logger.LogWarning(ex,
                        "Provider {Provider} {Operation} attempt {Attempt}/{Max} failed; retrying after {Delay}ms backoff.",
                        provider.Name, operation, attempt, MaxAttempts, Backoff(attempt).TotalMilliseconds);
                    await Task.Delay(Backoff(attempt), ct);
                }
            }
        }

        throw last!;
    }

    public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var p in order)
        {
            if (request.ModelOverride is not null && !p.IsHosted) continue;
            try
            {
                var r = await RetryAsync(p, "completion", () => p.CompleteAsync(request, ct), ct);
                logger.LogDebug("Provider {Provider} completed prompt {PromptId}", p.Name, request.PromptId);
                return r;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                logger.LogWarning(ex, "Provider {Provider} failed after retries; trying next.", p.Name);
            }
        }

        throw new ProviderUnavailableError($"All {order.Count} provider(s) failed. Last error: {last?.Message}");
    }

    public async IAsyncEnumerable<StreamingDelta> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Exception? last = null;
        foreach (var p in order)
        {
            // Retry only until the stream starts producing; once deltas have been
            // yielded a retry would duplicate output, so fall through instead.
            IAsyncEnumerator<StreamingDelta>? enumerator = null;
            for (var attempt = 1; attempt <= MaxAttempts && enumerator is null; attempt++)
            {
                try
                {
                    enumerator = p.StreamAsync(request, ct).GetAsyncEnumerator(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                    if (attempt < MaxAttempts)
                    {
                        logger.LogWarning(ex, "Provider {Provider} stream failed to start ({Attempt}/{Max}); retrying after backoff.", p.Name, attempt, MaxAttempts);
                        await Task.Delay(Backoff(attempt), ct);
                    }
                }
            }

            if (enumerator is null)
            {
                logger.LogWarning("Provider {Provider} stream could not start; trying next.", p.Name);
                continue;
            }

            var streamed = false;
            while (true)
            {
                StreamingDelta current;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    current = enumerator.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                    logger.LogWarning(ex, "Provider {Provider} stream failed; trying next.", p.Name);
                    break;
                }

                streamed = true;
                yield return current;
            }

            await enumerator.DisposeAsync();
            if (streamed) yield break;
        }

        throw new ProviderUnavailableError($"All {order.Count} provider(s) failed to stream. Last error: {last?.Message}");
    }

    public async Task<ToolCallResult> CompleteWithToolsAsync(CompletionRequest request, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var p in order)
        {
            try
            {
                if ((p.Capabilities & ProviderCapabilities.ToolCalling) == 0) continue;
                return await RetryAsync(p, "tool-call", () => p.CompleteWithToolsAsync(request, ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                logger.LogWarning(ex, "Provider {Provider} tool-call failed after retries; trying next.", p.Name);
            }
        }

        // Graceful degradation: answer without tools.
        if (last is not null)
            logger.LogWarning("No tool-calling provider succeeded ({Error}); degrading to plain completion.", last.Message);
        var plain = await CompleteAsync(request with { Tools = null }, ct);
        return new ToolCallResult(null, plain.Text, plain.Model, plain.ProviderName, plain.PromptTokens, plain.CompletionTokens, plain.CostUsd);
    }

    public async Task<EmbeddingResult> EmbedManyAsync(IReadOnlyList<string> texts, string? modelOverride = null, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var p in order)
        {
            try
            {
                return await RetryAsync(p, "embedding", () => p.EmbedManyAsync(texts, modelOverride, ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                logger.LogWarning(ex, "Provider {Provider} embedding failed after retries; trying next.", p.Name);
            }
        }

        throw new ProviderUnavailableError($"All {order.Count} provider(s) failed to embed. Last error: {last?.Message}");
    }
}
