using HR.Domain.Common;

namespace HR.Application.Abstractions.Processing;

public sealed record ParsedPage(int Number, string Section, string Text);

public sealed record ParsedDocument(
    string Title,
    string FileName,
    string SourceFormat,
    DocLanguage Language,
    string Version,
    string Source,
    IReadOnlyList<ParsedPage> Pages);

public interface IDocumentParser
{
    bool SupportsFormat(string fileName);
    Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken ct = default);
}

public interface ITextCleaner
{
    string Clean(string text);
}

public sealed record ChunkSegment(string Section, string PageReference, string Text);

public interface IDocumentChunker
{
    /// <summary>Segments one parsed document into chunk candidates, preserving structure.</summary>
    IReadOnlyList<ChunkSegment> Chunk(ParsedDocument document, int maxCharacters);
}
