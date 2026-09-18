using HR.Domain.Common;
using HR.Infrastructure.Nlp;
using Xunit;

namespace HR.Tests;

public sealed class ArabicNormalizerTests
{
    private readonly IArabicNormalizer _normalizer = new ArabicNormalizer();

    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إبراهيم", "ابراهيم")]
    [InlineData("آمن", "امن")]
    public void Folds_hamza_and_alef_forms(string input, string expected)
        => Assert.Equal(expected, _normalizer.Normalize(input));

    [Theory]
    [InlineData("تَقْدِيمُ", "ت ق د يم")]
    [InlineData("الـــبيانات", "ال بيانات")]
    [InlineData("فتاة", "فتاه")]
    [InlineData("مؤسسة", "موسسه")]
    [InlineData("رئيس\tالقسم", "رييس القسم")]
    public void Strips_diacritics_tatweel_and_normalises_morphology(string input, string expected)
        => Assert.Equal(expected, _normalizer.Normalize(input));

    [Theory]
    [InlineData("مدير تحليلات", true)]
    [InlineData("Sr Analyst", false)]
    [InlineData("GM مدير عام", true)]
    public void Detects_arabic_script(string input, bool expected)
        => Assert.Equal(expected, _normalizer.ContainsArabic(input));

    [Fact]
    public void Normalize_is_idempotent()
    {
        var first = _normalizer.Normalize("مدير تحليلات البيانات");
        Assert.Equal(first, _normalizer.Normalize(first));
    }

    [Fact]
    public void Arabic_equivalents_collapse_to_same_token()
    {
        Assert.Equal(_normalizer.Normalize("أحمد"), _normalizer.Normalize("احمد"));
        Assert.Equal(_normalizer.Normalize("المؤسسة"), _normalizer.Normalize("الموسسة"));
    }

    [Fact]
    public void Empty_and_whitespace_only_inputs_normalise_to_empty()
    {
        Assert.Equal(string.Empty, _normalizer.Normalize(string.Empty));
        Assert.Equal(string.Empty, _normalizer.Normalize("   \t  "));
    }
}
