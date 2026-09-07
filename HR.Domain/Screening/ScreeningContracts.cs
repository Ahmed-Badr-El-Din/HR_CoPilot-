using HR.Domain.Bias;
using HR.Domain.Common;
using HR.Domain.Documents;

namespace HR.Domain.Screening;

// ---------------------------------------------------------------------------
// Typed contracts between agents and tools (FR-4: "agents communicate through
// typed contracts, not free-form text").
// ---------------------------------------------------------------------------

public sealed record ExtractedEvidence(
    string CandidateId,
    string DimensionId,
    string Competency,
    string QuoteText,
    Citation Citation,
    double Confidence);

/// <summary>
/// A sanitised, redacted chunk that was actually handed to a downstream agent as
/// evidence. Never carries protected attributes — the extractor redacts them before
/// an EvidenceChunk is materialised (D6 bias guard).
/// </summary>
public sealed record EvidenceChunk(
    DocumentId DocumentId,
    ChunkId ChunkId,
    string DocumentTitle,
    string Section,
    string PageReference,
    string SanitizedText);

/// <summary>
/// Immutable record of exactly what a scoring step received, proving protected
/// attributes were excluded. Written at the moment the scorer input is composed.
/// </summary>
public sealed record AuditLog(
    RunId RunId,
    string StepName,
    string CandidateId,
    string ActorUserId,
    string CorrelationId,
    string InputSnapshotJson,
    IReadOnlyList<string> ExcludedProtectedAttributes,
    DateTimeOffset CreatedAt);

public sealed record DimensionScore(string DimensionId, double Score, double MaxScore, string Rationale);

public sealed class CandidateScore
{
    public string CandidateId { get; set; } = string.Empty;
    public List<DimensionScore> Dimensions { get; set; } = new();
    public double Total { get; set; }
    public double MaxTotal { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> ExcludedProtectedAttributes { get; set; } = new();
    public List<Citation> SourceCitations { get; set; } = new();
}

public sealed class ScoreSheet
{
    public List<CandidateScore> Scores { get; set; } = new();
}

public sealed record InterviewProbe(string Competency, string Question);

public sealed record ShortlistEntry(
    string CandidateId,
    int Rank,
    double TotalScore,
    string Summary,
    List<InterviewProbe> Probes);

public sealed class ShortlistDraft
{
    public string Role { get; set; } = string.Empty;
    public List<ShortlistEntry> Entries { get; set; } = new();
    public string Rationale { get; set; } = string.Empty;
    public List<Citation> SupportingCitations { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
