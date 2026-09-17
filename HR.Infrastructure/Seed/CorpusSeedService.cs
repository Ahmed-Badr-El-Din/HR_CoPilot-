using System.Text;
using HR.Application.Abstractions.Persistence;
using HR.Application.Documents;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Seed;

/// <summary>
/// Seeds the synthetic bilingual corpus through the real ingestion pipeline — the
/// same code path as user uploads — so the demo and tests exercise production code.
/// Idempotent: skips when documents already exist unless forced.
/// </summary>
public sealed class CorpusSeedService(
    DocumentIngestionService ingestion,
    IHrUnitOfWork store,
    ILogger<CorpusSeedService> logger)
{
    public async Task<int> SeedAsync(bool force = false, CancellationToken ct = default)
    {
        var existing = await store.Documents.CountAsync(ct);
        if (existing > 0 && !force)
        {
            logger.LogInformation("Seeding skipped: {Count} document(s) already present.", existing);
            return 0;
        }

        var docs = CorpusGenerator.Generate();
        var ctx = new SystemCorrelationContext($"seed-{Guid.NewGuid():N}");
        var ingested = 0;
        var failed = 0;
        foreach (var doc in docs)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(doc.JsonContent));
            var result = await ingestion.IngestAsync(doc.FileName, stream, ctx, ct);
            if (result.Status == "ready") ingested++;
            else failed++;
            logger.LogInformation("Seed {File}: {Status} — {Message}", doc.FileName, result.Status, result.Message);
        }

        await store.SaveChangesAsync(ct);
        logger.LogInformation("Seeding finished: {Ingested} ingested, {Failed} failed (forced={Force}).", ingested, failed, force);
        return docs.Count;
    }
}
