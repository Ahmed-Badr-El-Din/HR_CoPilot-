using System.Text;
using HR.Application.Documents;
using HR.Infrastructure.Embedding;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Processing;
using HR.Infrastructure.Providers;
using HR.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Tests;

public sealed class IngestionPipelineTests
{
    private static DocumentIngestionService CreateService(HrDbContext ctx)
        => new(
            new CompositeDocumentParser(new PdfTextParser(), new DocxTextParser()),
            new TextCleaner(),
            new StructureChunker(),
            new LocalModelProvider(new HashingEmbedder()),
            new SqliteVectorStore(ctx),
            new UnitOfWork(ctx),
            NullLogger<DocumentIngestionService>.Instance);

    [Fact]
    public async Task Ingesting_identical_bytes_is_idempotent()
    {
        using var db = new TestDb();
        var ctx = db.OpenContext();
        var service = CreateService(ctx);
        var doc = CorpusGenerator.Generate().First(d => d.FileName == "role-sd-am-en.json");

        var bytes = Encoding.UTF8.GetBytes(doc.JsonContent);
        var first = await service.IngestAsync(doc.FileName, new MemoryStream(bytes), new SystemCorrelationContext("ingest-1"), CancellationToken.None);
        var second = await service.IngestAsync(doc.FileName, new MemoryStream(bytes), new SystemCorrelationContext("ingest-2"), CancellationToken.None);

        Assert.False(first.WasCached, "first ingest must create the document");
        Assert.True(first.Status == "ready", $"first ingest status was '{first.Status}': {first.Message}");
        Assert.True(second.WasCached, "identical content must be a cached no-op");
        Assert.Equal(first.DocumentId, second.DocumentId);
        Assert.Equal("ready", second.Status);
    }

    [Fact]
    public async Task Ingest_persists_chunks_with_structure_metadata()
    {
        using var db = new TestDb();
        var ctx = db.OpenContext();
        var service = CreateService(ctx);
        var doc = CorpusGenerator.Generate().First(d => d.FileName == "role-devops-engineer.json");

        var result = await service.IngestAsync(
            doc.FileName,
            new MemoryStream(Encoding.UTF8.GetBytes(doc.JsonContent)),
            new SystemCorrelationContext("ingest-3"),
            CancellationToken.None);
        Assert.Equal("ready", result.Status);

        var runCtx = db.OpenContext();
        var stored = await runCtx.Chunks.Where(c => c.DocumentId == result.DocumentId).ToListAsync();
        Assert.NotEmpty(stored);
        Assert.All(stored, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
        Assert.Contains(stored, c => !string.IsNullOrWhiteSpace(c.Section));
        Assert.Contains(stored, c => c.Language == HR.Domain.Common.DocLanguage.En);
    }
}
