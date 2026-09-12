using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using HR.Application.Abstractions.Processing;
using HR.Domain.Common;
using HR.Domain.Errors;

namespace HR.Infrastructure.Processing;

/// <summary>
/// Minimal DOCX reader (FR-1: ≥2 formats). A .docx is an OOXML zip; this adapter
/// extracts the paragraph text from <c>word/document.xml</c> without pulling in a
/// heavyweight office SDK. Heading paragraphs (a <c>w:pStyle</c> whose value
/// starts with "Heading"/"Title"/"عنوان") become section boundaries, mirroring the
/// structure-aware chunking of <see cref="StructureChunker"/>. Pages are synthesised
/// the same way the plain-text parser does (~3200 characters per logical page).
/// </summary>
public sealed class DocxTextParser : IDocumentParser
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const int PageCharacterBudget = 3200;

    public bool SupportsFormat(string fileName)
        => System.IO.Path.GetExtension(fileName).TrimStart('.').Equals("docx", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new ValidationError("DOCX file has no word/document.xml part; is it a valid .docx?");

        using var documentStream = entry.Open();
        var xml = XDocument.Load(documentStream, LoadOptions.None);

        var paragraphs = new List<(string Text, string? Heading)>();
        foreach (var p in xml.Descendants(W + "p"))
        {
            ct.ThrowIfCancellationRequested();
            var text = string.Concat(p.Descendants(W + "t").Select(t => t.Value)).Trim();
            if (text.Length == 0) continue;
            paragraphs.Add((text, HeadingOf(p)));
        }

        var pages = new List<ParsedPage>();
        var builder = new StringBuilder();
        string? section = null;
        var pageNo = 1;
        foreach (var (text, heading) in paragraphs)
        {
            if (heading is not null)
            {
                // A new heading starts a new logical page so the section metadata of
                // each chunk stays aligned with the document's own structure.
                if (builder.Length > 0)
                {
                    pages.Add(new ParsedPage(pageNo++, section ?? string.Empty, builder.ToString()));
                    builder.Clear();
                }

                section = heading;
            }

            if (builder.Length + text.Length > PageCharacterBudget && builder.Length > 0)
            {
                pages.Add(new ParsedPage(pageNo++, section ?? string.Empty, builder.ToString()));
                builder.Clear();
            }

            builder.Append(text).Append("\n\n");
        }

        if (builder.Length > 0)
            pages.Add(new ParsedPage(pageNo, section ?? string.Empty, builder.ToString()));
        if (pages.Count == 0)
            pages.Add(new ParsedPage(1, string.Empty, string.Empty));

        var fullText = string.Join("\n", paragraphs.Select(x => x.Text));
        var language = CompositeDocumentParser.SnippetHasArabic(fullText) ? DocLanguage.Ar : DocLanguage.En;
        return Task.FromResult(new ParsedDocument(
            System.IO.Path.GetFileNameWithoutExtension(fileName),
            fileName,
            "docx",
            language,
            "1.0",
            "upload",
            pages));
    }

    private static string? HeadingOf(XElement paragraph)
    {
        var style = paragraph.Element(W + "pPr")?.Element(W + "pStyle")?
            .Attribute(W + "val")?.Value;
        if (style is null) return null;
        var isHeading = style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            || style.StartsWith("Title", StringComparison.OrdinalIgnoreCase)
            || style.Contains("عنوان", StringComparison.Ordinal);
        if (!isHeading) return null;
        var text = string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value)).Trim();
        return text.Length > 48 ? text[..48] : text;
    }
}
