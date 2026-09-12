using System.Text;
using System.Text.Json;
using HR.Application.Abstractions.Processing;
using HR.Domain.Common;
using UglyToad.PdfPig;

namespace HR.Infrastructure.Processing;

/// <summary>
/// Multiplexes the supported input formats (FR-1: ≥2 — txt, markdown and JSON are
/// parsed directly; PDF is handled by the PdfPig adapter, DOCX by the OOXML adapter).
/// The JSON format mirrors the ParsedDocument shape and is how the synthetic bilingual
/// corpus ships.
/// </summary>
public sealed class CompositeDocumentParser(PdfTextParser pdfParser, DocxTextParser docxParser) : IDocumentParser
{
    public bool SupportsFormat(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext is "txt" or "md" or "markdown" or "json" or "pdf" or "docx";
    }

    public async Task<ParsedDocument> ParseAsync(Stream stream, string fileName, CancellationToken ct)
    {
        var ext = System.IO.Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return ext switch
        {
            "pdf" => await pdfParser.ParseAsync(stream, fileName, ct),
            "docx" => await docxParser.ParseAsync(stream, fileName, ct),
            "json" => ParseJson(await reader.ReadToEndAsync(ct), fileName),
            _ => ParseText(await reader.ReadToEndAsync(ct), fileName, ext),
        };
    }

    private ParsedDocument ParseText(string raw, string fileName, string ext)
    {
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var title = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var isArabic = SnippetHasArabic(text);

        // Split into logical "pages" on blank-line paragraphs (~N chars per page).
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var pages = new List<ParsedPage>();
        var builder = new StringBuilder();
        var pageNo = 1;
        string? section = null;
        foreach (var para in paragraphs)
        {
            var p = para.Trim();
            if (p.Length == 0) continue;
            if (LooksLikeHeading(p)) section = CleanHeading(p);

            var marker = section is null ? string.Empty : $"[{section}] ";
            if (builder.Length + p.Length > 3200 && builder.Length > 0)
            {
                pages.Add(new ParsedPage(pageNo++, section ?? marker ?? string.Empty, builder.ToString()));
                builder.Clear();
            }

            builder.Append(p).Append("\n\n");
        }

        if (builder.Length > 0)
            pages.Add(new ParsedPage(pageNo, section ?? string.Empty, builder.ToString()));

        if (pages.Count == 0) pages.Add(new ParsedPage(1, string.Empty, text.Trim()));

        return new ParsedDocument(title, fileName, ext, isArabic ? DocLanguage.Ar : DocLanguage.En, "1.0", "upload", pages);
    }

    private ParsedDocument ParseJson(string raw, string fileName)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var pages = root.GetProperty("pages")
            .EnumerateArray()
            .Select((p, i) => new ParsedPage(
                p.TryGetProperty("number", out var n) ? n.GetInt32() : i + 1,
                p.TryGetProperty("section", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                p.GetProperty("text").GetString() ?? string.Empty))
            .ToList();

        var lang = root.TryGetProperty("language", out var l) && l.GetString() == "ar" ? DocLanguage.Ar : DocLanguage.En;
        return new ParsedDocument(
            root.TryGetProperty("title", out var t) ? t.GetString() ?? System.IO.Path.GetFileNameWithoutExtension(fileName) : System.IO.Path.GetFileNameWithoutExtension(fileName),
            fileName,
            "json",
            lang,
            root.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0" : "1.0",
            root.TryGetProperty("source", out var src) ? src.GetString() ?? "synthetic" : "synthetic",
            pages);
    }

    internal static bool LooksLikeHeading(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("#", StringComparison.Ordinal)) return true;
        if (t.Length > 2 && t.Length < 120 && t.All(c => char.IsUpper(c) || c is ' ' or ':' or '-') && t.Any(char.IsLetter)) return true;
        if (TitlesRegex.IsMatch(t)) return true;
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex TitlesRegex = new(
        @"^(القسم|الجزء|الفصل|الخطة|المقدمة|الخاتمة|الأهداف|المسؤوليات|section|chapter|part|introduction|objective|responsibilities|requirements|overview)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex HeadingCleaner = new(@"^#{1,6}\s*", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string CleanHeading(string line)
    {
        var t = HeadingCleaner.Replace(line.Trim(), string.Empty);
        return t.Length > 48 ? t[..48] : t;
    }

    internal static bool SnippetHasArabic(string text)
    {
        foreach (var c in text)
            if (c >= '\u0600' && c <= '\u06FF') return true;
        return false;
    }
}
