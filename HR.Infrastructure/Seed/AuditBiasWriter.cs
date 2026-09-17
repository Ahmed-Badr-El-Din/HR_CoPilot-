using HR.Application.Abstractions.Persistence;
using HR.Application.Agents;
using HR.Domain.Bias;
using HR.Domain.Screening;

namespace HR.Infrastructure.Seed;

/// <summary>Persists bias-exclusion audit records immediately (D6 audit trail).</summary>
public sealed class AuditBiasWriter(IHrUnitOfWork store) : IBiasWriter
{
    public async Task RecordAsync(string candidateId, IReadOnlyList<BiasAuditRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0) return;
        foreach (var r in records) await store.BiasAudits.AddAsync(r, ct);
        await store.SaveChangesAsync(ct);
    }

    public Task RecordScorerInputAsync(AuditLog log, CancellationToken ct = default)
    {
        store.AuditLogs.AddAsync(log, ct);
        return store.SaveChangesAsync(ct);
    }
}

public sealed class SystemCorrelationContext : HR.Application.Abstractions.ICorrelationContext
{
    public SystemCorrelationContext(string correlationId) => CorrelationId = correlationId;
    public string CorrelationId { get; }
    public HR.Domain.Common.UserId UserId => new("system");
    public IReadOnlyList<string> Roles { get; } = new[] { "Admin" };
}
