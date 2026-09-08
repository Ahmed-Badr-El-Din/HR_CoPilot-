using HR.Domain.Approvals;
using HR.Domain.Bias;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Domain.Runs;
using HR.Domain.Screening;
using HR.Domain.Sessions;

namespace HR.Application.Abstractions.Persistence;

public sealed record VectorItem(
    ChunkId ChunkId,
    DocumentId DocumentId,
    float[] Vector,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record VectorHit(ChunkId ChunkId, DocumentId DocumentId, double Score, IReadOnlyDictionary<string, string> Metadata);

public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyCollection<VectorItem> items, CancellationToken ct = default);
    Task<IReadOnlyList<VectorHit>> SearchAsync(float[] queryVector, int topK, IReadOnlyCollection<DocumentId>? documentIds = null, CancellationToken ct = default);
    Task<int> DeleteByDocumentAsync(DocumentId documentId, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(DocumentId id, CancellationToken ct = default);
    Task<Document?> GetByHashAsync(string contentHash, CancellationToken ct = default);
    Task<IReadOnlyList<Document>> ListAsync(int skip, int take, CancellationToken ct = default);
    Task AddAsync(Document document, CancellationToken ct = default);
    Task UpdateAsync(Document document, CancellationToken ct = default);
    Task DeleteAsync(Document document, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(DocumentId id, CancellationToken ct = default);
}

public interface IRunRepository
{
    Task<Run?> GetByIdAsync(RunId id, CancellationToken ct = default);
    Task AddAsync(Run run, CancellationToken ct = default);
    Task UpdateAsync(Run run, CancellationToken ct = default);
    Task AppendEventAsync(RunEvent ev, CancellationToken ct = default);
    Task<IReadOnlyList<RunEvent>> GetEventsAsync(RunId id, CancellationToken ct = default);
    Task<IReadOnlyList<Run>> ListByUserAsync(UserId userId, int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<Run>> ListAllAsync(int skip, int take, CancellationToken ct = default);
    Task<Run?> GetLatestAwaitingForRunAsync(RunId id, CancellationToken ct = default);
}

public interface IApprovalRepository
{
    Task<ApprovalRequest?> GetByIdAsync(ApprovalId id, CancellationToken ct = default);
    Task<ApprovalRequest?> GetPendingForRunAsync(RunId runId, CancellationToken ct = default);
    Task AddAsync(ApprovalRequest request, CancellationToken ct = default);
    Task UpdateAsync(ApprovalRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalRequest>> ListByRequesterAsync(UserId userId, int skip, int take, CancellationToken ct = default);
}

public interface ISessionRepository
{
    Task<ChatSession?> GetByIdAsync(SessionId id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSession>> ListByUserAsync(UserId userId, CancellationToken ct = default);
    Task AddAsync(ChatSession session, CancellationToken ct = default);
    Task UpdateAsync(ChatSession session, CancellationToken ct = default);
    Task AddMessageAsync(SessionMessage message, CancellationToken ct = default);
}

public interface IUsageRepository
{
    Task AddAsync(UsageRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<UsageRecord>> ListByUserAsync(UserId userId, int skip, int take, CancellationToken ct = default);
    Task<IReadOnlyList<UsageRecord>> ListAllAsync(int skip, int take, CancellationToken ct = default);
    Task<(int promptTokens, int completionTokens, decimal cost)> TotalsAsync(CancellationToken ct = default);
}

public interface IBiasAuditRepository
{
    Task AddAsync(BiasAuditRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<BiasAuditRecord>> ListByCandidateAsync(string candidateId, CancellationToken ct = default);
}

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
}

/// <summary>Single unit-of-work facade over the relational store.</summary>
public interface IHrUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    IDocumentRepository Documents { get; }
    IRunRepository Runs { get; }
    IApprovalRepository Approvals { get; }
    ISessionRepository Sessions { get; }
    IUsageRepository Usage { get; }
    IBiasAuditRepository BiasAudits { get; }
    IAuditLogRepository AuditLogs { get; }
}
