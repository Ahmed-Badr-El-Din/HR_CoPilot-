using HR.Application.Abstractions;
using HR.Domain.Common;
using HR.Domain.Errors;

namespace HR.Application.Authorization;

/// <summary>
/// Server-side object-ownership checks (OWASP Web — broken access control).
/// A resource may be read by its owner or by Admin/Auditor roles; everyone else
/// is rejected with 403 regardless of what the client hides in the UI.
/// </summary>
public static class AuthorizationService
{
    public static void EnsureResourceOwnerOrAuditor(UserId owner, ICorrelationContext ctx, string resource)
    {
        if (ctx.UserId.Value == owner.Value || ctx.Roles.Any(r => r is "Admin" or "Auditor"))
            return;

        throw new PolicyViolationError($"You do not have access to this {resource}.");
    }
}
