using System.Security.Cryptography;
using System.Text;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Processing;
using HR.Domain.Common;
using HR.Domain.Documents;
using HR.Domain.Errors;
using Microsoft.Extensions.Logging;

namespace HR.Application.Documents;

public sealed record IngestResult(DocumentId DocumentId, string Title, string Status, string? Message, bool WasCached);

/// <summary>
/// FR-1 pipeline: extract → clean → chunk → embed → index. Idempotent because the
/// source is hashed up-front; re-ingesting identical bytes is a no-op that returns
/// the existing document. Each stage mutates per-document status so failure is
/// visible and attributable to the stage that broke.
/// </summary>
public sealed class DocumentIngestionService(
    IDocumentParser parser,
    ITextCleaner cleaner,
    IDocumentChunker chunker,
    IModelProvider provider,
    IVectorStore vectorStore,
    IHrUnitOfWork store,
    ILogger<DocumentIngestionService> logger)
{
    public async Task<IngestResult> IngestAsync(string fileName, Stream content, ICorrelationContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Idempotency guard (FR-1): hash before anything touches the stream.
        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        if (!string.IsNullOrWhiteSpace(fileName) && !parser.SupportsFormat(fileName))
            throw new ValidationError($"Unsupported file format for '{fileName}'. Supported: txt, md, json, pdf, docx.");

        var existing = await store.Documents.GetByHashAsync(hash, ct);
        if (existing is { Status: DocumentStatus.Ready })
            return new IngestResult(existing.Id, existing.Title, "ready", "Already ingested (identical content).", WasCached: true);

        var documentId = DocumentId.New();
        var document = new Document
        {
            Id = documentId,
            FileName = fileName,
            Title = Path.GetFileNameWithoutExtension(fileName),
            SourceFormat = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant(),
            ContentHash = hash,
            Status = DocumentStatus.Pending,
        };

        try
        {
            // 1. EXTRACT
            document.MarkPreparing(DocumentStatus.Extracting);
            await store.Documents.AddAsync(document);
            await store.SaveChangesAsync(ct);

            var parsed = await parser.ParseAsync(new MemoryStream(bytes), fileName, ct);
            document.Title = parsed.Title;
            document.Language = parsed.Language;
            document.Version = parsed.Version;
            document.Source = parsed.Source;
            document.PageCount = parsed.Pages.Count;
            document.Tags.Add(parsed.Language == DocLanguage.Ar ? "ar" : "en");
            document.Tags.Add(parsed.Source);

            // 2. CLEAN
            document.MarkPreparing(DocumentStatus.Cleaning);
            var cleanedPages = parsed.Pages
                .Select(p => p with { Text = cleaner.Clean(p.Text) })
                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                .ToList();

            // 3. CHUNK (structure-preserving)
            document.MarkPreparing(DocumentStatus.Chunking);
            var segments = chunker.Chunk(new ParsedDocument(
                parsed.Title, parsed.FileName, parsed.SourceFormat, parsed.Language, parsed.Version, parsed.Source, cleanedPages), 1400);
            if (segments.Count == 0)
                throw new ValidationError("Extracted content was empty after cleaning.");

            // 4. EMBED
            document.MarkPreparing(DocumentStatus.Embedding);
            var embedding = await provider.EmbedManyAsync(segments.Select(s => s.Text).ToArray(), null, ct);
            var vectors = embedding.Vectors;

            // 5. INDEX (persist chunks + vectors)
            document.MarkPreparing(DocumentStatus.Indexing);
            var chunks = new List<DocumentChunk>();
            for (var i = 0; i < segments.Count; i++)
            {
                var chunk = new DocumentChunk
                {
                    Id = ChunkId.New(),
                    DocumentId = documentId,
                    Ordinal = i + 1,
                    Section = segments[i].Section,
                    PageReference = segments[i].PageReference,
                    Text = segments[i].Text,
                    Language = parsed.Language,
                    Embedding = i < vectors.Length ? vectors[i] : null,
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = parsed.Source,
                        ["language"] = parsed.Language.ToString(),
                        ["version"] = parsed.Version,
                    },
                };
                chunks.Add(chunk);
            }

            document.Chunks = chunks;
            await vectorStore.UpsertAsync(chunks
                .Where(c => c.Embedding is not null)
                .Select(c => new VectorItem(c.Id, documentId, c.Embedding!, c.Metadata))
                .ToList(), ct);

            document.MarkReady(chunks.Count);
            await store.Documents.UpdateAsync(document);
            await store.SaveChangesAsync(ct);

            return new IngestResult(documentId, document.Title, "ready", $"Indexed {chunks.Count} chunk(s) across {cleanedPages.Count} page(s).", WasCached: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            document.MarkFailed(ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000]);
            await store.Documents.UpdateAsync(document);
            try
            {
                await store.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // no-op: client walked away; failure still recorded above at EF level
            }

            logger.LogError(ex, "Document ingestion failed for {File}", fileName);
            return new IngestResult(documentId, document.Title, "failed", ex.Message, WasCached: false);
        }
    }
}
