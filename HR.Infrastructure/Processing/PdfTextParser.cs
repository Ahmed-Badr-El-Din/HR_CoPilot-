using System.Text;
using HR.Application.Abstractions.Processing;
using HR.Domain.Common;
using UglyToad.PdfPig;

namespace HR.Infrastructure.Processing;

/// <summary>Extracts the text layer of PDFs (page by page) with PdfPig.</summary>
public sealed class PdfTextParser : IDocumentParser
{
    public bool SupportsFormat(string fileName)
        => System.IO.Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken ct)
    {
        var pages = new List<ParsedPage>();
        using var document = PdfDocument.Open(stream);
        var title = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string? lastSection = null;

        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var firstLine = text.Split('\n', 2, StringSplitOptions.TrimEntries)[0];
            if (CompositeDocumentParser.LooksLikeHeading(firstLine) && firstLine.Length < 120)
                lastSection = firstLine;

            pages.Add(new ParsedPage(page.Number, lastSection ?? string.Empty, text));
        }

        if (pages.Count == 0)
            throw new Domain.Errors.ValidationError($"PDF '{fileName}' has no extractable text layer. Scan + OCR is documented as out of scope (Twist T6, not assigned).");

        var isArabic = pages.Any(p => CompositeDocumentParser.SnippetHasArabic(p.Text));
        var parsed = new ParsedDocument(
            title, fileName, "pdf",
            isArabic ? DocLanguage.Ar : DocLanguage.En,
            "1.0", "upload", pages);
        return Task.FromResult(parsed);
    }
}
