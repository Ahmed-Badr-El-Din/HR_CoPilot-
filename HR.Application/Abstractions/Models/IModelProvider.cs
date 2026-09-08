namespace HR.Application.Abstractions.Models;

/// <summary>Role for chat messages exchanged with a model provider.</summary>
public static class ModelRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

public sealed record ChatMessage(string Role, string Content);

public sealed record ToolDefinition(string Name, string Description, string JsonSchema);

public sealed record ToolCallInput(string Id, string Name, string ArgumentsJson);

public sealed record CompletionRequest(
    string PromptId,
    string SystemPrompt,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    double Temperature = 0.2,
    int MaxTokens = 1024,
    string? ModelOverride = null,
    string? CorrelationId = null);

public sealed record CompletionResult(
    string Text,
    string Model,
    string ProviderName,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd);

public sealed record ToolCallResult(
    IReadOnlyList<ToolCallInput>? ToolCalls,
    string? TextContent,
    string Model,
    string ProviderName,
    int PromptTokens,
    int CompletionTokens,
    decimal CostUsd);

public sealed record StreamingDelta(string Text, bool Final, int? PromptTokens = null, int? CompletionTokens = null);

public sealed record EmbeddingResult(
    float[][] Vectors,
    int Dimensions,
    string Model,
    string ProviderName,
    int TokensUsed,
    decimal CostUsd);

/// <summary>Declares which operations a provider can actually perform.</summary>
[Flags]
public enum ProviderCapabilities
{
    None = 0,
    Completion = 1 << 0,
    Streaming = 1 << 1,
    ToolCalling = 1 << 2,
    Embeddings = 1 << 3,
}

public interface IModelProvider
{
    string Name { get; }

    bool IsHosted { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>Optional human description shown in the UI/observability.</summary>
    string Description { get; }

    Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken ct = default);

    IAsyncEnumerable<StreamingDelta> StreamAsync(CompletionRequest request, CancellationToken ct = default);

    Task<ToolCallResult> CompleteWithToolsAsync(CompletionRequest request, CancellationToken ct = default);

    Task<EmbeddingResult> EmbedManyAsync(IReadOnlyList<string> texts, string? modelOverride = null, CancellationToken ct = default);
}
