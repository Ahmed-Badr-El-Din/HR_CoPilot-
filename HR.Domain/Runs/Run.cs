using HR.Domain.Common;

namespace HR.Domain.Runs;

public enum RunKind
{
    Screening = 0,
    Ask = 1,
}

public enum RunStatus
{
    Queued = 0,
    Running = 1,
    AwaitingApproval = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}

public sealed class Run
{
    public RunId Id { get; set; } = RunId.New();
    public RunKind Kind { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Queued;
    public UserId OwnerUserId { get; set; } = new(string.Empty);
    public string CorrelationId { get; set; } = string.Empty;
    public string RequestJson { get; set; } = "{}";
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public bool DegradedToPlainRag { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? ApprovalRequestedAt { get; set; }
}

public enum RunEventKind
{
    RunStarted = 0,
    AgentStarted = 1,
    AgentFinished = 2,
    ToolInvoked = 3,
    TokenDelta = 4,
    CitationFound = 5,
    ApprovalRequired = 6,
    ApprovalResolved = 7,
    StepCompleted = 8,
    Message = 9,
    Degraded = 10,
    Error = 11,
    RunFinished = 12,
    Cancelled = 13,
}

public sealed class RunEvent
{
    public long Id { get; set; }
    public RunId RunId { get; set; }
    public int Sequence { get; set; }
    public RunEventKind Kind { get; set; }
    public string? AgentName { get; set; }
    public string? ToolName { get; set; }
    public string? PayloadJson { get; set; }
    public string? Text { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UsageRecord
{
    public long Id { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public RunId? RunId { get; set; }
    public UserId UserId { get; set; } = new(string.Empty);
    public string Model { get; set; } = string.Empty;
    public string Kind { get; set; } = "chat"; // chat | embedding
    public string ProviderName { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
