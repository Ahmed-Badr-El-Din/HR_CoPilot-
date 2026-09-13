using HR.Domain.Common;

namespace HR.Infrastructure.Nlp;

/// <summary>Adapter exposing the static <see cref="TextNormalizer"/> under the HR.Domain port.</summary>
public sealed class ArabicNormalizer : IArabicNormalizer
{
    public string Normalize(string text) => TextNormalizer.Normalize(text);

    public bool ContainsArabic(string text) => TextNormalizer.ContainsArabic(text);
}
