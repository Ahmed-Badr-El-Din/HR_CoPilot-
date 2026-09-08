using System.Text.RegularExpressions;

namespace HR.Domain.Bias;

public enum ProtectedAttributeKind
{
    Name,
    Gender,
    Age,
    Nationality,
    Religion,
    MaritalStatus,
    ContactDetail,
    Identification,
}

public sealed record BiasAuditRecord(
    string CandidateId,
    ProtectedAttributeKind AttributeKind,
    int Occurrences,
    string Pattern,
    DateTimeOffset Timestamp);

public sealed class BiasExclusionReport
{
    public string CandidateId { get; set; } = string.Empty;
    public List<BiasAuditRecord> Records { get; set; } = new();
    public int TotalOccurrencesRemoved => Records.Sum(r => r.Occurrences);
    public string RedactedSnippet { get; set; } = string.Empty;
    public bool HadProtectedContent => Records.Count > 0;
}

/// <summary>
/// D6 risk control. Protected attributes are *excluded from the scoring context*:
/// the rubric scorer only receives a redacted transcript. Every removal is recorded
/// so the audit trail can prove scoring input was free of protected attributes.
/// </summary>
public static class ProtectedAttributeDetector
{
    private static readonly Regex ContactDetail = new(
        @"(?:([+]?\d{1,3})[\s.-]?)?(?:\(?\d{2,3}\)?[\s.-]?)+\d{3,4}[\s.-]?\d{3,4}|" +
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Age = new(
        @"(?:\b(?:age|العمر|عمر)\s*[:：\-]?\s*\d{1,2}\b)|(?:\b\d{1,2}\s*(?:years?|سنة|عام)\b)|(?:\b(?:من مواليد|born|birth)\s*[:：\-]?\s*\d{4}\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Identification = new(
        @"(?:\b(?:national id|بطاقة|رقم قومي|بطاقة قومية|جواز سفر|passport|رقم الهوية|بيانات صاحب العمل)\b)?\s*[:#：\-]?\s*(\d{6,14})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InfoHeaderLine = new(
        @"(?im)^\s*[^\n]{0,60}?(?:البريد الالكتروني|بريد إلكتروني|mobile|tel|phone|هاتف|جوال|email|e-mail|الاسم|اسم العائلة|name|full name|address|العنوان|linkedin|المؤهل|qualification|الجنسية|الدين|الحالة|الحالة الاجتماعية|الجنس|الوظيفة السابقة|الشركة السابقة)\s*[:：\-]?\s*.*$",
        RegexOptions.Compiled);

    private static readonly string[] GenderWords = { "ذكر", "أنثى", "male", "female", "الجنس: ذكر", "الجنس: أنثى", "gender: male", "gender: female" };
    private static readonly string[] MaritalWords = { "متزوج", "أعزب", "مطلق", "أرمل", "married", "single", "divorced", "widowed" };
    private static readonly string[] ReligionWords = { "مسلم", "مسيحي", "muslim", "christian" };
    private static readonly string[] NationWords =
    {
        "مصري", "مصرية", "سعودي", "سعودية", "إماراتي", "إماراتية", "أردني", "أردنية",
        "لبناني", "لبنانية", "تونسي", "تونسية", "عراقي", "عراقية", "سوري", "سورية",
        "سوداني", "سودانية", "egyptian", "saudi", "emirati", "jordanian", "lebanese",
        "tunisian", "iraqi", "syrian", "sudanese",
    };

    public static BiasExclusionReport Exclude(string candidateId, string text)
    {
        var now = DateTimeOffset.UtcNow;
        var report = new BiasExclusionReport { CandidateId = candidateId };
        string redacted = text;

        void RemoveRegex(ProtectedAttributeKind kind, string pattern, Regex regex)
        {
            if (regex is null) return;
            var matches = regex.Matches(redacted);
            if (matches.Count == 0) return;
            int masked = 0;
            foreach (Match m in matches)
            {
                if (m.Length == 0) continue;
                redacted = redacted.Replace(m.Value, new string('▮', Math.Clamp(m.Value.Length, 4, 16)));
                masked += m.Length;
            }

            report.Records.Add(new BiasAuditRecord(candidateId, kind, matches.Count, pattern, now));
        }

        void RemoveWords(ProtectedAttributeKind kind, string pattern, IEnumerable<string> words)
        {
            int count = 0;
            foreach (var w in words)
            {
                var rx = new Regex(Regex.Escape(w), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var matches = rx.Matches(redacted);
                if (matches.Count == 0) continue;
                count += matches.Count;
                foreach (Match m in matches)
                    redacted = redacted.Replace(m.Value, new string('▮', Math.Clamp(m.Value.Length, 4, 16)));
            }

            if (count > 0) report.Records.Add(new BiasAuditRecord(candidateId, kind, count, pattern, now));
        }

        // Contact detail blocks and inline emails / phones.
        RemoveRegex(ProtectedAttributeKind.ContactDetail, "contact-regex", ContactDetail);

        // Ages and birth dates.
        RemoveRegex(ProtectedAttributeKind.Age, "age-regex", Age);

        // National / passport IDs.
        RemoveRegex(ProtectedAttributeKind.Identification, "id-regex", Identification);

        // Info header lines that carry names, emails, phones, addresses etc.
        RemoveRegex(ProtectedAttributeKind.Name, "personal-info-line", InfoHeaderLine);

        // Word-based attributes, detected in whichever script(s) the document uses.
        RemoveWords(ProtectedAttributeKind.Gender, "gender-word/ar-en", GenderWords);
        RemoveWords(ProtectedAttributeKind.MaritalStatus, "marital-word/ar-en", MaritalWords);
        RemoveWords(ProtectedAttributeKind.Religion, "religion-word/ar-en", ReligionWords);

        // Age words that the regex above may miss after masking (boundary cases).
        RemoveRegex(ProtectedAttributeKind.Age, "age-word", new Regex(@"\b(?:من مواليد|born|birth)\b", RegexOptions.IgnoreCase));

        RemoveWords(ProtectedAttributeKind.Nationality, "nationality-word/ar", NationWords);

        report.RedactedSnippet = redacted;
        return report;
    }
}
