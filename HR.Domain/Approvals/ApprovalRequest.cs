using HR.Domain.Common;

namespace HR.Domain.Approvals;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    EditedAndApproved = 3,
}

public sealed class ApprovalRequest
{
    public ApprovalId Id { get; set; } = ApprovalId.New();
    public RunId RunId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string PayloadJson { get; set; } = "{}";
    public string? AssignedRole { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
    public UserId? ResponderUserId { get; set; }
    public string? Comment { get; set; }
    public string? EditedPayloadJson { get; set; }

    /// <summary>SLA in minutes; escalation is driven off this timestamp.</summary>
    public DateTimeOffset? SlaDeadline { get; set; }
}

public sealed record ApprovalDecision(ApprovalStatus Status, string? Comment, string? EditedPayloadJson);
