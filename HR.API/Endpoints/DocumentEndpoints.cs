using System.Security.Claims;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Persistence;
using HR.Application.Documents;
using HR.Domain.Errors;
using HR.Infrastructure.Providers;
using HR.Infrastructure.Seed;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace HR.API.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents");
        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", DetailAsync).RequireAuthorization();
        group.MapPost("/ingest", IngestAsync).RequireAuthorization(Roles.CanScreen);
        group.MapPost("/ingest-corpus", IngestCorpusAsync).RequireAuthorization(Roles.CanSeed);
    }

    private static async Task<Ok<object>> ListAsync(
        IHrUnitOfWork store,
        int skip = 0,
        int take = 50)
    {
        var documents = await store.Documents.ListAsync(skip, Math.Clamp(take, 1, 200));
        var total = await store.Documents.CountAsync();
        return TypedResults.Ok<object>(new
        {
            total,
            documents = documents.Select(d => new
            {
                id = d.Id.ToString(),
                title = d.Title,
                source_format = d.SourceFormat,
                language = d.Language.ToString(),
                version = d.Version,
                status = d.Status.ToString(),
                status_message = d.StatusMessage,
                page_count = d.PageCount,
                tags = d.Tags.OrderBy(t => t).ToArray(),
                created_at = d.CreatedAt,
            }),
        });
    }

    private static async Task<Ok<object>> DetailAsync(
        string id,
        IHrUnitOfWork store)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new ValidationError($"Invalid document id '{id}'.");
        var doc = await store.Documents.GetByIdAsync(new HR.Domain.Common.DocumentId(guid))
            ?? throw new DocumentNotFoundError(id);

        var chunks = await store.Documents.GetChunksAsync(doc.Id);
        return TypedResults.Ok<object>(new
        {
            id = doc.Id.ToString(),
            title = doc.Title,
            status = doc.Status.ToString(),
            status_message = doc.StatusMessage,
            language = doc.Language.ToString(),
            version = doc.Version,
            source = doc.Source,
            page_count = doc.PageCount,
            chunk_count = chunks.Count,
            chunks = chunks.Select(c => new
            {
                id = c.Id.ToString(),
                ordinal = c.Ordinal,
                section = c.Section,
                page_reference = c.PageReference,
                language = c.Language.ToString(),
                text_preview = c.Text.Length > 240 ? c.Text[..240] + "…" : c.Text,
            }),
        });
    }

    private static async Task<Ok<IngestResult>> IngestAsync(
        IFormFile file,
        DocumentIngestionService ingestion,
        ICorrelationContext ctx,
        IOptions<IngestionOptions> options,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new ValidationError("A non-empty file is required.");

        if (file.Length > options.Value.MaxFileBytes)
            throw new ValidationError($"File exceeds the {options.Value.MaxFileBytes} byte upload limit.");

        await using var stream = file.OpenReadStream();
        var result = await ingestion.IngestAsync(file.FileName, stream, ctx, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>Seeds the synthetic bilingual corpus through the real ingestion pipeline.</summary>
    private static async Task<Ok<object>> IngestCorpusAsync(
        CorpusSeedService seeder,
        bool force = false,
        CancellationToken ct = default)
    {
        var count = await seeder.SeedAsync(force, ct);
        return TypedResults.Ok<object>(new { ingested = count });
    }
}
