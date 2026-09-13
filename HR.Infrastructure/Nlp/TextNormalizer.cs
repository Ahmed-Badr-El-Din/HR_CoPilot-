using System.Globalization;
using System.Text;

namespace HR.Infrastructure.Nlp;

/// <summary>
/// Shared Arabic-first text normalisation. Used by the keyword index, the hashing
/// embedder and the bilingual lexicon so that AR/EN retrieval is consistent.
/// </summary>
public static class TextNormalizer
{
    private static readonly HashSet<char> Diacritics = new()
    {
        '\u064B', '\u064C', '\u064D', '\u064E', '\u064F', '\u0650', '\u0651', '\u0652',
        '\u0653', '\u0654', '\u0655', '\u0670', '\u0640', '\u0656', '\u0657', '\u0658',
    };

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;
        foreach (var c in text)
        {
            var ch = NormalizeChar(c);
            if (ch == ' ')
            {
                if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static char NormalizeChar(char c)
    {
        if (Diacritics.Contains(c) || c is '\u200B' or '\u200C' or '\u200D') return ' ';
        if (char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c)) return ' ';
        return char.ToLowerInvariant(c) switch
        {
            'أ' or 'إ' or 'آ' => 'ا',
            'ى' or 'ي' => 'ي',
            'ؤ' => 'و',
            'ئ' => 'ي',
            'ة' => 'ه',
            'ٱ' => 'ا',
            '۱' or '۲' or '۳' or '٤' or '٥' or '٦' or '٧' or '٨' or '٩' or '۰' => '0',
            _ => char.ToLowerInvariant(c),
        };
    }

    public static bool ContainsArabic(string text)
    {
        foreach (var c in text)
            if (c >= '\u0600' && c <= '\u06FF') return true;
        return false;
    }

    /// <summary>Approximate token count (chars/4 for Latin, chars/2 for Arabic).</summary>
    public static int EstimateTokens(string text)
    {
        var arabic = 0;
        var latin = 0;
        foreach (var c in text)
        {
            if (c >= '\u0600' && c <= '\u06FF') arabic++;
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) latin++;
        }

        return (int)Math.Max(1, Math.Round(arabic / 2.0 + latin / 4.0));
    }

    public static string RtlAwareHtml(string text) => text;

    public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
}
