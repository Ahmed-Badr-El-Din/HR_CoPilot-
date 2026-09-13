using System.Text.RegularExpressions;

namespace HR.Infrastructure.Nlp;

/// <summary>
/// Deterministic AR↔EN gloss for the HR domain. Enables cross-lingual queries
/// (Twist T1) even on the fully offline path where no multilingual embedding model
/// is available: the query is expanded into both scripts and both variants are
/// fused by the hybrid retriever.
/// </summary>
public sealed class BilingualLexicon
{
    public static readonly IReadOnlyDictionary<string, string> ArToEn = new Dictionary<string, string>
    {
        ["مدير"] = "manager",
        ["مشروع"] = "project",
        ["موارد بشرية"] = "hr",
        ["هندسة"] = "engineering",
        ["برمجيات"] = "software",
        ["تطوير"] = "development",
        ["مبيعات"] = "sales",
        ["تسويق"] = "marketing",
        ["مالية"] = "finance",
        ["محاسبة"] = "accounting",
        ["تشغيل"] = "operations",
        ["دعم"] = "support",
        ["فني"] = "technical",
        ["تحليل"] = "analysis",
        ["بيانات"] = "data",
        ["سحابة"] = "cloud",
        ["قائد"] = "lead",
        ["خبرة"] = "experience",
        ["جامعة"] = "university",
        ["شهادة"] = "certification",
        ["مهارات"] = "skills",
        ["إدارة"] = "management",
        ["تخطيط"] = "planning",
        ["ميزانية"] = "budget",
        ["توظيف"] = "recruitment",
        ["مقابلة"] = "interview",
        ["مرشح"] = "candidate",
        ["سيرة ذاتية"] = "resume",
        ["أجايل"] = "agile",
        ["سكروم"] = "scrum",
        ["جافا"] = "java",
        ["بايثون"] = "python",
        ["قاعدة بيانات"] = "database",
        ["أمن"] = "security",
        ["اختبارات"] = "testing",
        ["تصميم"] = "design",
        ["تجربة"] = "experience",
        ["عام"] = "years",
        ["أعوام"] = "years",
    };

    public static readonly IReadOnlyDictionary<string, string> EnToAr = BuildEnToAr();

    private static Dictionary<string, string> BuildEnToAr()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in ArToEn)
            if (!map.ContainsKey(kv.Value)) map[kv.Value] = kv.Key;
        return map;
    }

    /// <summary>Adds the counterpart-language variant of a short query as separate keywords.</summary>
    public static string? Expand(string query, bool targetArabic)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var pieces = new List<string>();
        foreach (var word in Regex.Split(query.ToLowerInvariant(), @"[^\w\u0600-\u06FF]+")
                     .Where(w => w.Length > 0))
        {
            var translated = targetArabic
                ? EnToAr.TryGetValue(word, out var ar) ? ar : null
                : ArToEn.TryGetValue(word, out var en) ? en : null;
            if (translated is not null) pieces.Add(translated);
        }

        return pieces.Count > 0 ? string.Join(" ", pieces) : null;
    }

    public static bool IsArabic(string query) => TextNormalizer.ContainsArabic(query);
}
