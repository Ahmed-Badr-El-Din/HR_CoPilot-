using System.Text.RegularExpressions;

namespace HR.Domain.Common;

/// <summary>
/// Canonical refusal contract (FR-2/FR-4). A grounded answer must refuse rather than
/// guess; providers signal that by returning one of these sentinels. Centralising it
/// lets the ask workflow turn a provider's refusal text into a real refusal outcome
/// instead of presenting it as a normal answer.
/// </summary>
public static class Refusals
{
    public const string Sentinel = "Not enough information in the corpus.";

    public const string ArabicSentinel = "لا توجد معلومات كافية في قاعدة المستندات";

    private static readonly Regex CitationBracket = new(@"\[[0-9a-fA-F]{32}\]", RegexOptions.Compiled);

    public static bool IsRefusal(string answer)
    {
        var trimmed = answer.TrimStart();
        var startsWithSentinel =
            trimmed.StartsWith(Sentinel, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(ArabicSentinel, StringComparison.Ordinal);
        // A real refusal carries no supporting citation; an answer that merely quotes
        // the sentinel policy (e.g. "the exact string is 'Not enough information...'")
        // still cites its source and must not be misclassified.
        return startsWithSentinel && !CitationBracket.IsMatch(answer);
    }
}
