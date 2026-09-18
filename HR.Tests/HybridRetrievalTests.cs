using HR.Application.Abstractions.Retrieval;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Infrastructure.Embedding;
using HR.Infrastructure.Nlp;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Providers;
using HR.Infrastructure.Retrieval;
using Xunit;

namespace HR.Tests;

public sealed class HybridRetrievalTests
{
    private static HybridRetriever CreateRetriever(HrDbContext ctx)
        => new(
            new SqliteVectorStore(ctx),
            new LocalModelProvider(new HashingEmbedder()),
            new DbChunkLookup(ctx),
            new KeywordIndex(new DbChunkSource(ctx)),
            new ArabicNormalizer());

    private static async Task<(DocumentId ArDoc, DocumentId EnDoc)> SeedAsync(TestDb db)
    {
        var ctx = db.OpenContext();
        var embed = new HashingEmbedder();

        var arChunk = new DocumentChunk
        {
            Id = ChunkId.New(),
            Ordinal = 1,
            Section = "الخبرات",
            PageReference = "p.3",
            Language = DocLanguage.Ar,
            Text = "قيادة فريق محللين وتحليل البيانات الكبيرة، وتطوير تقارير الإدارة بتقنيات SQL وPython ولوحات Power BI.",
        };
        var arDoc = new Document
        {
            Id = DocumentId.New(),
            Title = "أحمد حسن",
            FileName = "cv-ar.json",
            SourceFormat = "json",
            Language = DocLanguage.Ar,
            Source = "synthetic",
            Status = DocumentStatus.Ready,
        };
        arChunk.DocumentId = arDoc.Id;
        arChunk.Embedding = embed.Embed(arChunk.Text);
        arDoc.Chunks.Add(arChunk);

        var enChunk = new DocumentChunk
        {
            Id = ChunkId.New(),
            Ordinal = 1,
            Section = "Responsibilities",
            PageReference = "p.2",
            Language = DocLanguage.En,
            Text = "Owns end-to-end analytical delivery: dashboards in Power BI, SQL and Python for weekly stakeholder reports.",
        };
        var enDoc = new Document
        {
            Id = DocumentId.New(),
            Title = "Senior Analytics Manager",
            FileName = "role-analytics.json",
            SourceFormat = "json",
            Language = DocLanguage.En,
            Source = "synthetic",
            Status = DocumentStatus.Ready,
        };
        enChunk.DocumentId = enDoc.Id;
        enChunk.Embedding = embed.Embed(enChunk.Text);
        enDoc.Chunks.Add(enChunk);

        ctx.Documents.AddRange(arDoc, enDoc);
        await ctx.SaveChangesAsync();
        return (arDoc.Id, enDoc.Id);
    }

    [Fact]
    public async Task Arabic_query_returns_arabic_chunk_with_citation_metadata()
    {
        using var db = new TestDb();
        var (arDoc, _) = await SeedAsync(db);
        var retriever = CreateRetriever(db.OpenContext());

        var result = await retriever.RetrieveAsync(new RetrievalQuery("قيادة فريق محللين وتحليل البيانات", TopK: 5), CancellationToken.None);

        Assert.NotEmpty(result.Chunks);
        Assert.Contains(result.Chunks, c => c.DocumentId == arDoc);
        Assert.Contains("hybrid-rrf", result.Strategy);
        Assert.True(result.DenseConfidence > 0, "hashing embeddings are deterministic; dense confidence should be non-zero");
        var hit = result.Chunks.First(c => c.DocumentId == arDoc);
        Assert.Equal("الخبرات", hit.Section);
        Assert.Equal("p.3", hit.PageReference);
    }

    [Fact]
    public async Task English_query_returns_english_chunk()
    {
        using var db = new TestDb();
        var (_, enDoc) = await SeedAsync(db);
        var retriever = CreateRetriever(db.OpenContext());

        var result = await retriever.RetrieveAsync(new RetrievalQuery("analytics dashboards SQL Power BI stakeholder", TopK: 5), CancellationToken.None);

        Assert.NotEmpty(result.Chunks);
        Assert.Contains(result.Chunks, c => c.DocumentId == enDoc);
    }

    [Fact]
    public async Task Document_id_filter_restricts_results()
    {
        using var db = new TestDb();
        var (_, enDoc) = await SeedAsync(db);
        var retriever = CreateRetriever(db.OpenContext());

        var result = await retriever.RetrieveAsync(
            new RetrievalQuery("قيادة فريق محللين", TopK: 5, DocumentIds: new[] { enDoc }),
            CancellationToken.None);

        Assert.All(result.Chunks, c => Assert.Equal(enDoc, c.DocumentId));
        Assert.DoesNotContain(result.Chunks, c => c.Section == "الخبرات");
    }
}
