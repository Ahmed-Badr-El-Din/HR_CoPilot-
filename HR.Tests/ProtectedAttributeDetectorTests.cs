using HR.Domain.Bias;
using Xunit;

namespace HR.Tests;

public sealed class ProtectedAttributeDetectorTests
{
    [Fact]
    public void Arabic_cv_snippet_is_redacted_of_all_protected_attributes()
    {
        const string snippet = """
            السيرة الذاتية
            البيانات الشخصية:
            الاسم: احمد حسن
            العمر: 34 سنة
            الجنس: ذكر
            الجنسية: مصري
            الحالة الاجتماعية: متزوج
            الدين: مسلم
            البريد الالكتروني: ahmed@example.org
            الهاتف: 0155500001
            أنا محلل مصري، ذكر، مسلم، متزوج، خبرتي 10 سنوات في تحليل البيانات.
            في العمل قمت بقيادة فريق محللين وتطوير تقارير الإدارة.
            """;

        var report = ProtectedAttributeDetector.Exclude("c-1", snippet);

        Assert.True(report.HadProtectedContent);
        Assert.DoesNotContain("احمد", report.RedactedSnippet);
        Assert.DoesNotContain("ahmed@example.org", report.RedactedSnippet);
        Assert.DoesNotContain("34 سنة", report.RedactedSnippet);
        Assert.DoesNotContain("ذكر", report.RedactedSnippet);
        Assert.DoesNotContain("مصري", report.RedactedSnippet);
        Assert.DoesNotContain("متزوج", report.RedactedSnippet);
        Assert.Contains("تحليل البيانات", report.RedactedSnippet, StringComparison.Ordinal);
        Assert.Contains("قيادة فريق محللين", report.RedactedSnippet, StringComparison.Ordinal);

        var kinds = report.Records.Select(r => r.AttributeKind).ToHashSet();
        Assert.Contains(ProtectedAttributeKind.Name, kinds);
        Assert.Contains(ProtectedAttributeKind.ContactDetail, kinds);
        Assert.Contains(ProtectedAttributeKind.Age, kinds);
        Assert.Contains(ProtectedAttributeKind.Gender, kinds);
        Assert.Contains(ProtectedAttributeKind.Nationality, kinds);
        Assert.Contains(ProtectedAttributeKind.MaritalStatus, kinds);
        Assert.Contains(ProtectedAttributeKind.Religion, kinds);
    }

    [Fact]
    public void English_cv_snippet_is_redacted_of_all_protected_attributes()
    {
        const string snippet = """
            Name: Fatima Ali
            Age: 37
            Gender: Female
            Nationality: Emirati
            Email: fatima@example.net

            Led an analytics team and built Power BI dashboards with SQL.
            """;

        var report = ProtectedAttributeDetector.Exclude("c-en-2", snippet);

        Assert.DoesNotContain("Fatima", report.RedactedSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("37", report.RedactedSnippet);
        Assert.DoesNotContain("Female", report.RedactedSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Emirati", report.RedactedSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fatima@example.net", report.RedactedSnippet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Power BI dashboards", report.RedactedSnippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_content_produces_no_redactions()
    {
        const string snippet = "Led a four-person analytics team and built Power BI dashboards with SQL and Python.";

        var report = ProtectedAttributeDetector.Exclude("c-en-3", snippet);

        Assert.False(report.HadProtectedContent);
        Assert.Equal(snippet, report.RedactedSnippet);
        Assert.Equal(0, report.TotalOccurrencesRemoved);
    }
}
