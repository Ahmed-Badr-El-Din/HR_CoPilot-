using HR.Domain.Common;

namespace HR.Application.Abstractions;

/// <summary>Correlation ID + identity flowing through the whole request (FR-9).</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }
    UserId UserId { get; }
    IReadOnlyList<string> Roles { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Versioned prompt assets — prompts live in files, not string literals (FR-4/architecture).
/// </summary>
public interface IPromptCatalog
{
    string GetPrompt(string promptId, int majorVersion);
    string GetPrompt(string promptId);
    IReadOnlyList<PromptAssetInfo> List();
}

public sealed record PromptAssetInfo(string Id, string Version, string Language, string Description);

public interface IEventEmitter
{
    /// <summary>Subscribes to raw events for a run; returns a channel the consumer reads from.</summary>
    IAsyncEnumerable<EventPacket> SubscribeAsync(string channel, CancellationToken ct = default);

    /// <summary>Publishes an event packet on a channel (fire and forget).</summary>
    void Publish(string channel, EventPacket packet);
}

/// <summary>Rough serialisable envelope used for SSE and websocket forwarders.</summary>
public sealed record EventPacket(string Type, string PayloadJson, DateTimeOffset At);
