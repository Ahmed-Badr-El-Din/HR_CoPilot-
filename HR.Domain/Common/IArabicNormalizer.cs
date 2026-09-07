namespace HR.Domain.Common;

/// <summary>
/// Port for Arabic-first text normalisation (D6 bias guard + T1 bilingual retrieval).
/// Kept in HR.Domain so every layer can rely on a single deterministic normalisation
/// contract without coupling Application/Infrastructure to a concrete implementation.
/// Implementations must be script-fold-insensitive (hamza/alef/tatweel folding) and
/// free of protected attribute leakage.
/// </summary>
public interface IArabicNormalizer
{
    /// <summary>Normalises a string: strips diacritics/tatweel, folds hamza/alef/tamarbuta, lowercases.</summary>
    string Normalize(string text);

    /// <summary>True when the text contains Arabic-script characters (U+0600..U+06FF).</summary>
    bool ContainsArabic(string text);
}
