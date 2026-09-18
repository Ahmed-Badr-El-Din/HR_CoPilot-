using HR.Application.Abstractions.Models;
using HR.Domain.Errors;
using HR.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Tests;

public sealed class ResilientProviderTests
{
    private static CompletionRequest Request() => new("test", "system", new[] { new ChatMessage(ModelRoles.User, "hi") });

    [Fact]
    public async Task Retries_the_same_provider_with_backoff_before_succeeding()
    {
        var flaky = new FlakyProvider("flaky", failuresBeforeSuccess: 2);
        var provider = new ResilientModelProvider(
            new IModelProvider[] { flaky },
            new LlmOptions { MaxRetries = 2, RetryBackoffBaseMs = 1 },
            NullLogger<ResilientModelProvider>.Instance);

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("flaky", result.ProviderName);
        Assert.Equal(3, flaky.Attempts);
    }

    [Fact]
    public async Task Falls_through_to_the_next_provider_when_the_first_keeps_failing()
    {
        var alwaysFails = new FlakyProvider("broken", failuresBeforeSuccess: int.MaxValue);
        var healthy = new FlakyProvider("healthy", failuresBeforeSuccess: 0);
        var provider = new ResilientModelProvider(
            new IModelProvider[] { alwaysFails, healthy },
            new LlmOptions { MaxRetries = 0 },
            NullLogger<ResilientModelProvider>.Instance);

        var result = await provider.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal("healthy", result.ProviderName);
    }

    [Fact]
    public async Task Throws_provider_unavailable_when_every_provider_fails()
    {
        var broken = new FlakyProvider("broken", failuresBeforeSuccess: int.MaxValue);
        var provider = new ResilientModelProvider(
            new IModelProvider[] { broken },
            new LlmOptions { MaxRetries = 0 },
            NullLogger<ResilientModelProvider>.Instance);

        await Assert.ThrowsAsync<ProviderUnavailableError>(() => provider.CompleteAsync(Request(), CancellationToken.None));
    }

    private sealed class FlakyProvider(string name, int failuresBeforeSuccess) : IModelProvider
    {
        public int Attempts { get; private set; }

        public string Name => name;
        public bool IsHosted => false;
        public ProviderCapabilities Capabilities => ProviderCapabilities.Completion;
        public string Description => "test provider";

        public Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
                throw new ProviderUnavailableError($"{name} simulated outage {Attempts}");
            return Task.FromResult(new CompletionResult("ok", name, name, 1, 1, 0m));
        }

        public async IAsyncEnumerable<StreamingDelta> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ToolCallResult> CompleteWithToolsAsync(CompletionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<EmbeddingResult> EmbedManyAsync(IReadOnlyList<string> texts, string? modelOverride = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
