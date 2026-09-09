using HR.Application.Abstractions;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Abstractions.Tools;
using HR.Domain.Bias;
using HR.Domain.Common;
using HR.Domain.Runs;
using HR.Domain.Screening;

namespace HR.Application.Agents;

/// <summary>Bias audit writer port so redactions are persisted (D6 audit trail).</summary>
public interface IBiasWriter
{
    Task RecordAsync(string candidateId, IReadOnlyList<BiasAuditRecord> records, CancellationToken ct = default);

    /// <summary>Persists an immutable snapshot of a scoring step's exact input.</summary>
    Task RecordScorerInputAsync(AuditLog log, CancellationToken ct = default);
}

/// <summary>
/// Everything an agent is allowed to see and do. Emits domain run events that
/// are persisted and mirrored to connected SSE clients (FR-6, FR-9).
/// </summary>
public sealed record AgentServices(
    IModelProvider Provider,
    IRetrievalService Retriever,
    IToolRegistry Tools,
    IPromptCatalog Prompts,
    ICorrelationContext Context,
    IBiasWriter BiasWriter,
    string ResponseLanguage,
    Func<RunId, RunEvent, CancellationToken, Task> EmitAsync,
    Func<RunId, UsageRecord, CancellationToken, Task> RecordUsageAsync,
    int MaxIterations,
    TimeSpan PerStepTimeout);
