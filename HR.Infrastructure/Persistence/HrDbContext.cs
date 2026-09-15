using System.Text.Json;
using HR.Domain.Approvals;
using HR.Domain.Bias;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Domain.Runs;
using HR.Domain.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HR.Infrastructure.Persistence;

public sealed class HrDbContext(DbContextOptions<HrDbContext> options) : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> Chunks => Set<DocumentChunk>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<RunEvent> RunEvents => Set<RunEvent>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<BiasAuditEntity> BiasAuditEntities => Set<BiasAuditEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        builder.Entity<Document>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).HasConversion(g => g.Value, v => new DocumentId(v));
            e.Property(d => d.Tags).HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<HashSet<string>>(v, jsonOptions) ?? new HashSet<string>())
                .Metadata.SetValueComparer(TagsComparer);
            e.Property(d => d.Title).IsRequired();
            e.HasIndex(d => d.ContentHash);
            e.HasMany(d => d.Chunks).WithOne().HasForeignKey(c => c.DocumentId);
        });

        builder.Entity<DocumentChunk>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasConversion(g => g.Value, v => new ChunkId(v));
            e.Property(c => c.DocumentId).HasConversion(d => d.Value, v => new DocumentId(v));
            e.Property(c => c.Text).IsRequired();
            e.Property(c => c.Metadata).HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, jsonOptions) ?? new Dictionary<string, string>())
                .Metadata.SetValueComparer(MetadataComparer);
            e.Property(c => c.Embedding).HasConversion(
                new ValueConverter<float[]?, byte[]>(
                    v => v == null ? Array.Empty<byte>() : SerializeFloats(v),
                    v => v.Length == 0 ? null : DeserializeFloats(v)))
                .Metadata.SetValueComparer(EmbeddingComparer);
            e.Property(c => c.Language).HasConversion<int>();
            e.HasIndex(c => c.DocumentId);
        });

        builder.Entity<Run>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasConversion(g => g.Value, v => new RunId(v));
            e.Property(r => r.OwnerUserId).HasConversion(u => u.Value, v => new UserId(v));
            e.Property(r => r.Status).HasConversion<int>();
            e.Property(r => r.Kind).HasConversion<int>();
            e.Property(r => r.CorrelationId).IsRequired();
            e.HasIndex(r => r.CorrelationId);
        });

        builder.Entity<RunEvent>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.Id).ValueGeneratedOnAdd();
            e.Property(ev => ev.RunId).HasConversion(g => g.Value, v => new RunId(v));
            e.Property(ev => ev.Kind).HasConversion<int>();
            e.HasIndex(ev => ev.RunId);
        });

        builder.Entity<UsageRecord>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).ValueGeneratedOnAdd();
            e.Property(u => u.RunId).HasConversion(
                g => g.HasValue ? g.Value.Value : (Guid?)null,
                v => v.HasValue ? new RunId(v.Value) : (RunId?)null);
            e.Property(u => u.UserId).HasConversion(id => id.Value, v => new UserId(v));
            e.Property(u => u.CostUsd).HasColumnType("TEXT");
            e.HasIndex(u => u.RunId);
        });

        builder.Entity<ChatSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasConversion(g => g.Value, v => new SessionId(v));
            e.Property(s => s.OwnerUserId).HasConversion(u => u.Value, v => new UserId(v));
            e.HasMany(s => s.Messages).WithOne().HasForeignKey(m => m.SessionId);
        });

        builder.Entity<SessionMessage>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
            e.Property(m => m.SessionId).HasConversion(g => g.Value, v => new SessionId(v));
            e.Property(m => m.RunId).HasConversion(
                g => g.HasValue ? g.Value.Value : (Guid?)null,
                v => v.HasValue ? new RunId(v.Value) : (RunId?)null);
        });

        builder.Entity<ApprovalRequest>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasConversion(g => g.Value, v => new ApprovalId(v));
            e.Property(a => a.RunId).HasConversion(g => g.Value, v => new RunId(v));
            e.Property(a => a.ResponderUserId).HasConversion(u => u == null ? (string?)null : u.Value.Value, u => u == null ? null : new UserId(u));
            e.Property(a => a.Status).HasConversion<int>();
            e.HasIndex(a => a.Status);
        });

        builder.Entity<BiasAuditEntity>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).ValueGeneratedOnAdd();
            e.Property(b => b.AttributeKind).HasConversion<int>();
            e.HasIndex(b => new { b.CandidateId, b.Timestamp });
        });

        builder.Entity<AuditLogEntity>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.HasIndex(l => new { l.CandidateId, l.StepName, l.Timestamp });
        });
    }

    private static readonly ValueComparer<HashSet<string>> TagsComparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SetEquals(b)),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        v => new HashSet<string>(v));

    private static readonly ValueComparer<Dictionary<string, string>> MetadataComparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.Count == b.Count && !a.Except(b).Any()),
        v => v.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
        v => new Dictionary<string, string>(v));

    private static readonly ValueComparer<float[]?> EmbeddingComparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
        v => v == null ? 0 : v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        v => v == null ? null : v.ToArray());

    private static byte[] SerializeFloats(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
    private static float[] DeserializeFloats(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}

public sealed class BiasAuditEntity
{
    public long Id { get; set; }
    public string CandidateId { get; set; } = string.Empty;
    public ProtectedAttributeKind AttributeKind { get; set; }
    public int Occurrences { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Persisted scorer-input snapshot (D6 audit trail). See <c>HR.Domain.Screening.AuditLog</c>.</summary>
public sealed class AuditLogEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public string CandidateId { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string InputSnapshotJson { get; set; } = string.Empty;
    public string ExcludedProtectedAttributesJson { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
