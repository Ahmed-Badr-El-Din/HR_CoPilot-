namespace HR.API;

/// <summary>Server-side role names and authorization policies (FR-8).</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string HiringManager = "HiringManager";
    public const string Auditor = "Auditor";

    public static readonly string[] All = { Admin, HiringManager, Auditor };

    /// <summary>Only the admin can manage users and seed/delete corpus content.</summary>
    public const string CanManage = "can_manage";

    /// <summary>Hiring work: ingest documents and start screening workflows.</summary>
    public const string CanScreen = "can_screen";

    /// <summary>Read-only audit surface: usage, bias audit trail, all runs.</summary>
    public const string CanAudit = "can_audit";

    /// <summary>Only a hiring manager may resolve the shortlist approval gate.</summary>
    public const string CanApprove = "can_approve";

    /// <summary>Seeding the synthetic demo corpus is a privileged operation.</summary>
    public const string CanSeed = "can_seed";
}
