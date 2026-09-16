using HR.Infrastructure.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HR.API.Auth;

/// <summary>
/// Seeds the three demo accounts and roles on first boot (FR-8). Passwords come
/// from AuthOptions / .env — never a committed secret. Admin and Auditor use the
/// same password by design so the demo needs only one credential to try all roles.
/// </summary>
public static class DemoAccountSeeder
{
    public static async Task SeedAsync(
        UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles,
        IOptions<AuthOptions> options,
        CancellationToken ct = default)
    {
        var settings = options.Value;
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));
        }

        await EnsureUserAsync(users, settings.DefaultAdminEmail, settings.DefaultAdminPassword, Roles.Admin, "Demo Admin", ct);
        await EnsureUserAsync(users, settings.DefaultManagerEmail, settings.DefaultManagerPassword, Roles.HiringManager, "Demo Hiring Manager", ct);
        await EnsureUserAsync(users, settings.DefaultAuditorEmail, settings.DefaultAuditorPassword, Roles.Auditor, "Demo Auditor", ct);
    }

    private static async Task EnsureUserAsync(
        UserManager<IdentityUser> users,
        string email,
        string password,
        string role,
        string displayName,
        CancellationToken ct)
    {
        var existing = await users.FindByEmailAsync(email);
        if (existing is null)
        {
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                throw new InvalidOperationException($"Failed to seed user {email}: {string.Join("; ", created.Errors.Select(e => e.Description))}");

            await users.AddToRoleAsync(user, role);
            await users.SetAuthenticationTokenAsync(user, "hr", "display_name", displayName);
            return;
        }

        if (!await users.IsInRoleAsync(existing, role))
            await users.AddToRoleAsync(existing, role);
    }
}
