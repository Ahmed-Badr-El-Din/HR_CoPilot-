using System.Text.Json;
using HR.Application.Abstractions.Persistence;
using HR.Domain.Approvals;
using HR.Domain.Bias;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Domain.Runs;
using HR.Domain.Screening;
using HR.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

public sealed class UnitOfWork(HrDbContext db) : IHrUnitOfWork
{
    public IDocumentRepository Documents { get; } = new DocumentRepository(db);
    public IRunRepository Runs { get; } = new RunRepository(db);
    public IApprovalRepository Approvals { get; } = new ApprovalRepository(db);
    public ISessionRepository Sessions { get; } = new SessionRepository(db);
    public IUsageRepository Usage { get; } = new UsageRepository(db);
    public IBiasAuditRepository BiasAudits { get; } = new BiasAuditRepository(db);
    public IAuditLogRepository AuditLogs { get; } = new AuditLogRepository(db);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

internal sealed class DocumentRepository(HrDbContext db) : IDocumentRepository
{
    public async Task<Document?> GetByIdAsync(DocumentId id, CancellationToken ct = default)
        => await db.Documents.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<Document?> GetByHashAsync(string contentHash, CancellationToken ct = default)
        => db.Documents.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.ContentHash == contentHash, ct);

    public async Task<IReadOnlyList<Document>> ListAsync(int skip, int take, CancellationToken ct = default)
        => await db.Documents.OrderByDescending(d => d.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

    public Task AddAsync(Document document, CancellationToken ct = default) => db.Documents.AddAsync(document, ct).AsTask();

    public Task UpdateAsync(Document document, CancellationToken ct = default)
    {
        db.Documents.Update(document);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Document document, CancellationToken ct = default)
    {
        db.Documents.Remove(document);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken ct = default) => db.Documents.CountAsync(ct);

    public async Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(DocumentId id, CancellationToken ct = default)
        => await db.Chunks.Where(c => c.DocumentId == id).OrderBy(c => c.Ordinal).ToListAsync(ct);
}

internal sealed class RunRepository(HrDbContext db) : IRunRepository
{
    public async Task<Run?> GetByIdAsync(RunId id, CancellationToken ct = default)
        => await db.Runs.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task AddAsync(Run run, CancellationToken ct = default) => db.Runs.AddAsync(run, ct).AsTask();
    public Task UpdateAsync(Run run, CancellationToken ct = default) { db.Runs.Update(run); return Task.CompletedTask; }

    public Task AppendEventAsync(RunEvent ev, CancellationToken ct = default) => db.RunEvents.AddAsync(ev, ct).AsTask();

    public async Task<IReadOnlyList<RunEvent>> GetEventsAsync(RunId id, CancellationToken ct = default)
        => await db.RunEvents.Where(e => e.RunId == id).OrderBy(e => e.Sequence).ThenBy(e => e.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Run>> ListByUserAsync(UserId userId, int skip, int take, CancellationToken ct = default)
        => await db.Runs.Where(r => r.OwnerUserId == userId).OrderByDescending(r => r.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<Run>> ListAllAsync(int skip, int take, CancellationToken ct = default)
        => await db.Runs.OrderByDescending(r => r.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

    public async Task<Run?> GetLatestAwaitingForRunAsync(RunId id, CancellationToken ct = default)
        => await db.Runs.FirstOrDefaultAsync(r => r.Id == id && r.Status == RunStatus.AwaitingApproval, ct);
}

internal sealed class ApprovalRepository(HrDbContext db) : IApprovalRepository
{
    public async Task<ApprovalRequest?> GetByIdAsync(ApprovalId id, CancellationToken ct = default)
        => await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<ApprovalRequest?> GetPendingForRunAsync(RunId runId, CancellationToken ct = default)
        => await db.ApprovalRequests.FirstOrDefaultAsync(a => a.RunId == runId && a.Status == ApprovalStatus.Pending, ct);

    public Task AddAsync(ApprovalRequest request, CancellationToken ct = default) => db.ApprovalRequests.AddAsync(request, ct).AsTask();
    public Task UpdateAsync(ApprovalRequest request, CancellationToken ct = default) { db.ApprovalRequests.Update(request); return Task.CompletedTask; }

    public async Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(CancellationToken ct = default)
        => await db.ApprovalRequests.Where(a => a.Status == ApprovalStatus.Pending).OrderBy(a => a.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<ApprovalRequest>> ListByRequesterAsync(UserId userId, int skip, int take, CancellationToken ct = default)
        => await db.ApprovalRequests.OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
}

internal sealed class SessionRepository(HrDbContext db) : ISessionRepository
{
    public async Task<ChatSession?> GetByIdAsync(SessionId id, CancellationToken ct = default)
        => await db.ChatSessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<ChatSession>> ListByUserAsync(UserId userId, CancellationToken ct = default)
        => await db.ChatSessions.Where(s => s.OwnerUserId == userId).OrderByDescending(s => s.UpdatedAt).ToListAsync(ct);

    public Task AddAsync(ChatSession session, CancellationToken ct = default) => db.ChatSessions.AddAsync(session, ct).AsTask();
    public Task UpdateAsync(ChatSession session, CancellationToken ct = default) { db.ChatSessions.Update(session); return Task.CompletedTask; }
    public Task AddMessageAsync(SessionMessage message, CancellationToken ct = default) => db.SessionMessages.AddAsync(message, ct).AsTask();
}

internal sealed class UsageRepository(HrDbContext db) : IUsageRepository
{
    public Task AddAsync(UsageRecord record, CancellationToken ct = default) => db.UsageRecords.AddAsync(record, ct).AsTask();

    public async Task<IReadOnlyList<UsageRecord>> ListByUserAsync(UserId userId, int skip, int take, CancellationToken ct = default)
        => await db.UsageRecords.Where(u => u.UserId == userId).OrderByDescending(u => u.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

    public async Task<IReadOnlyList<UsageRecord>> ListAllAsync(int skip, int take, CancellationToken ct = default)
        => await db.UsageRecords.OrderByDescending(u => u.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);

    public async Task<(int promptTokens, int completionTokens, decimal cost)> TotalsAsync(CancellationToken ct = default)
    {
        var records = await db.UsageRecords.AsNoTracking().ToListAsync(ct);
        return (records.Sum(u => u.PromptTokens), records.Sum(u => u.CompletionTokens), records.Sum(u => u.CostUsd));
    }
}

internal sealed class BiasAuditRepository(HrDbContext db) : IBiasAuditRepository
{
    public Task AddAsync(BiasAuditRecord record, CancellationToken ct = default)
    {
        var entity = new BiasAuditEntity
        {
            CandidateId = record.CandidateId,
            AttributeKind = record.AttributeKind,
            Occurrences = record.Occurrences,
            Pattern = record.Pattern,
            Timestamp = record.Timestamp,
        };
        return db.BiasAuditEntities.AddAsync(entity, ct).AsTask();
    }

    public async Task<IReadOnlyList<BiasAuditRecord>> ListByCandidateAsync(string candidateId, CancellationToken ct = default)
    {
        var entities = await db.BiasAuditEntities.Where(b => b.CandidateId == candidateId).OrderBy(b => b.Timestamp).ToListAsync(ct);
        return entities.Select(e => new BiasAuditRecord(e.CandidateId, e.AttributeKind, e.Occurrences, e.Pattern, e.Timestamp)).ToList();
    }
}

internal sealed class AuditLogRepository(HrDbContext db) : IAuditLogRepository
{
    public Task AddAsync(AuditLog log, CancellationToken ct = default)
    {
        var entity = new AuditLogEntity
        {
            RunId = log.RunId.Value.ToString(),
            StepName = log.StepName,
            CandidateId = log.CandidateId,
            ActorUserId = log.ActorUserId,
            CorrelationId = log.CorrelationId,
            InputSnapshotJson = log.InputSnapshotJson,
            ExcludedProtectedAttributesJson = JsonSerializer.Serialize(log.ExcludedProtectedAttributes),
            Timestamp = log.CreatedAt,
        };
        return db.AuditLogs.AddAsync(entity, ct).AsTask();
    }
}
