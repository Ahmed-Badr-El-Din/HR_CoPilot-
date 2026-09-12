using System.Text.RegularExpressions;
using HR.Application.Abstractions.Processing;

namespace HR.Infrastructure.Processing;

/// <summary>
/// Cleaning stage: strips control characters, normalises whitespace and dashes,
/// preserves paragraphs. Deliberately does NOT strip Arabic diacritics here —
/// display text keeps its full form; normalisation happens at index/search time.
/// </summary>
public sealed class TextCleaner : ITextCleaner
{
    private static readonly Regex ControlChars = new(@"[^\u0009\u000A\u000D\u0020-\uD7FF\uE000-\uFFFD]", RegexOptions.Compiled);
    private static readonly Regex ExcessNewlines = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex ExcessBlankLines = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex ObituaryDashes = new(@" {2,}", RegexOptions.Compiled);

    public string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var cleaned = text.Replace("\r\n", "\n").Replace('\r', '\n');
        cleaned = ControlChars.Replace(cleaned, string.Empty);
        cleaned = ExcessNewlines.Replace(cleaned, " ");
        cleaned = ExcessBlankLines.Replace(cleaned, "\n\n");
        cleaned = ObituaryDashes.Replace(cleaned, " ");
        return cleaned.Trim();
    }
}
