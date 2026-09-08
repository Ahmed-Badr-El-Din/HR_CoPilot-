using System.Text.RegularExpressions;

namespace HR.Domain.Security;

/// <summary>One matched suspicious instruction span and the pattern that caught it.</summary>
public sealed record InjectionMatch(string Pattern, string Excerpt);

/// <summary>Outcome of scanning a string for prompt-injection instructions.</summary>
public sealed record InjectionScan(bool IsSuspicious, IReadOnlyList<InjectionMatch> Matches)
{
    public static InjectionScan Clean { get; } = new(false, Array.Empty<InjectionMatch>());
}

/// <summary>
/// Deterministic prompt-injection guard (OWASP LLM-01). Detects classic override
/// instructions in English and Arabic — direct (typed by a user) and indirect
/// (embedded inside retrieved/ingested content such as a hostile CV). Retrieved
/// content is passed through <see cref="Neutralize"/> so embedded instructions are
/// defanged before the model ever sees them; direct user attempts are refused by
/// the ask workflow. Pure domain logic: no SDK, no IO.
/// </summary>
public static class PromptInjectionDetector
{
    private static readonly Regex[] Patterns =
    {
        // ---- English overrides -------------------------------------------------
        new(@"\bignore\s+(all\s+|any\s+|the\s+)?(previous|prior|above|earlier)\s+(instructions?|prompts?|rules?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdisregard\s+(all\s+|any\s+|the\s+)?(previous|prior|above|earlier)?\s*(instructions?|rules?|guidelines?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bforget\s+(all\s+|your\s+)?(previous\s+|prior\s+)?(instructions?|rules?|guidelines?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\byou\s+are\s+now\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bsystem\s+(prompt|message|instructions?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\breveal\s+(the\s+)?(system\s+)?(prompt|instructions?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\boverride\s+(the\s+)?(rubric|instructions?|safety|guardrails?|rules?)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(developer|debug|dan)\s+mode\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bjailbreak\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(disregard|ignore)\b[^.]{0,40}\b(rubric|scoring|score)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(give|assign|award)\b[^.]{0,40}\b(perfect|full|maximum|100)\b[^.]{0,20}\bscore\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(send|email|exfiltrate|leak)\b[^.]{0,40}\b(api\s*key|password|secret|token)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // ---- Arabic overrides --------------------------------------------------
        new(@"تجاهل\s+(جميع\s+|كل\s+|أي\s+)?(التعليمات|الأوامر|القواعد)\s*(السابقة|الماضية)?", RegexOptions.Compiled),
        new(@"انسَ\s+(جميع\s+|كل\s+)?(التعليمات|القواعد)", RegexOptions.Compiled),
        new(@"اكشف\s+(رسالة\s+النظام|التعليمات|المطالبات|التوجيهات)", RegexOptions.Compiled),
        new(@"أنت\s+الآن", RegexOptions.Compiled),
        new(@"تجاوز\s+(معايير|التعليمات|القواعد|الحماية|التحكيم)", RegexOptions.Compiled),
        new(@"(أعط|امنح|اعط)[^.]{0,30}(الدرجة\s+الكاملة|100)", RegexOptions.Compiled),
        new(@"وضع\s+(المطور|التصحيح|الدخول)", RegexOptions.Compiled),
        new(@"مفتاح\s+(الوصول|API|التشفير)", RegexOptions.Compiled),
    };

    public const string NeutralizedMarker = "[neutralized: possible prompt injection]";

    /// <summary>Scans text and reports every instruction-like span found.</summary>
    public static InjectionScan Scan(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return InjectionScan.Clean;
        var matches = new List<InjectionMatch>();
        foreach (var pattern in Patterns)
        {
            foreach (Match m in pattern.Matches(text))
            {
                var excerpt = m.Value.Length <= 120 ? m.Value : m.Value[..120];
                matches.Add(new InjectionMatch(pattern.ToString(), excerpt));
            }
        }

        return matches.Count == 0 ? InjectionScan.Clean : new InjectionScan(true, matches);
    }

    /// <summary>
    /// Replaces detected instruction spans with a marker so retrieved content can be
    /// shown to the model as untrusted data without the embedded imperative surviving.
    /// </summary>
    public static string Neutralize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var result = text;
        foreach (var pattern in Patterns)
            result = pattern.Replace(result, NeutralizedMarker);
        return result;
    }
}
