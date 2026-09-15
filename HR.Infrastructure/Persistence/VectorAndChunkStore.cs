using HR.Application.Abstractions.Persistence;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// Vector store backed by the relational store (SQLite): the embedding column of
/// the chunks table plus an in-process brute-force cosine scan. Deliberate ADR-004
/// choice: the corpus is sized for brute force, and a managed vector DB can be
/// swapped in behind IVectorStore without touching business logic.
/// </summary>
public sealed class SqliteVectorStore(HrDbContext db) : IVectorStore
{
    public Task UpsertAsync(IReadOnlyCollection<VectorItem> items, CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            var chunk = db.Chunks.Find(item.ChunkId) ?? db.Chunks.Local.FirstOrDefault(c => c.Id == item.ChunkId);
            if (chunk is null)
            {
                chunk = new DocumentChunk { Id = item.ChunkId, DocumentId = item.DocumentId, Embedding = item.Vector };
                db.Chunks.Add(chunk);
            }
            else
            {
                chunk.Embedding = item.Vector;
            }
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(float[] queryVector, int topK, IReadOnlyCollection<DocumentId>? documentIds = null, CancellationToken ct = default)
    {
        var chunks = await db.Chunks.AsNoTracking()
            .Where(c => c.Embedding != null)
            .Select(c => new { c.Id, c.DocumentId, Vec = c.Embedding })
            .ToListAsync(ct);

        var allowed = documentIds is { Count: > 0 }
            ? documentIds.Select(d => d.Value).ToHashSet()
            : null;

        var scored = new List<(Guid Id, Guid DocId, double Score)>();
        foreach (var c in chunks)
        {
            if (allowed is not null && !allowed.Contains(c.DocumentId.Value)) continue;
            if (c.Vec is null || c.Vec.Length != queryVector.Length) continue;
            double dot = 0;
            for (var i = 0; i < queryVector.Length; i++) dot += queryVector[i] * c.Vec[i];
            scored.Add((c.Id.Value, c.DocumentId.Value, dot));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => new VectorHit(new ChunkId(s.Id), new DocumentId(s.DocId), Math.Max(0, s.Score), new Dictionary<string, string>()))
            .ToList();
    }

    public async Task<int> DeleteByDocumentAsync(DocumentId documentId, CancellationToken ct = default)
        => await db.Chunks.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync(ct);

    public Task<int> CountAsync(CancellationToken ct = default) => db.Chunks.CountAsync(ct);
}

/// <summary>Bridge between the keyword index and the relational chunk table.</summary>
public sealed class DbChunkSource(HrDbContext db) : ChunkSource
{
    public override Task<long> GetChunkCountAsync(CancellationToken ct = default) => db.Chunks.LongCountAsync(ct);

    public override async Task<IReadOnlyList<DocumentChunk>> GetAllChunksAsync(CancellationToken ct = default)
        => await db.Chunks.AsNoTracking().ToListAsync(ct);
}

public sealed class DbChunkLookup(HrDbContext db) : IChunkLookup
{
    public async Task<ChunkRecord?> GetChunkAsync(ChunkId id, CancellationToken ct = default)
    {
        var chunk = await db.Chunks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (chunk is null) return null;
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == chunk.DocumentId, ct);
        return new ChunkRecord(
            chunk.Id,
            chunk.DocumentId,
            doc?.Title ?? chunk.DocumentId.ToString(),
            chunk.Language,
            chunk.Section,
            chunk.PageReference,
            chunk.Text);
    }
}
