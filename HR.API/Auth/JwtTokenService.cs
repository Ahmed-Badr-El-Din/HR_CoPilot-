using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HR.Domain.Common;
using HR.Infrastructure.Providers;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HR.API.Auth;

/// <summary>
/// Issues signed JWT bearer tokens. The signing key is externalised via
/// AuthOptions (never a secret in the repo). All roles are carried as role
/// claims so policies enforce permissions server-side (FR-8).
/// </summary>
public sealed class JwtTokenService(IOptions<AuthOptions> options)
{
    private readonly AuthOptions _options = options.Value;

    public JwtTokenResult CreateToken(UserId userId, string email, string displayName, IReadOnlyList<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.Value),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.UniqueName, email),
            new(ClaimTypes.Name, displayName),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(_options.TokenLifetimeHours);
        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new JwtTokenResult(
            new JwtSecurityTokenHandler().WriteToken(token),
            "bearer",
            (int)(expires - now).TotalSeconds,
            roles);
    }
}

public static class JwtClaims
{
    public static UserId UserId(ClaimsPrincipal user)
        => new(user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? string.Empty);

    public static string? Email(ClaimsPrincipal user)
        => user.FindFirstValue(JwtRegisteredClaimNames.Email) ?? user.FindFirstValue(ClaimTypes.Email);

    public static IReadOnlyList<string> RoleNames(ClaimsPrincipal user)
        => user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToArray();
}

public sealed record JwtTokenResult(string AccessToken, string TokenType, int ExpiresIn, IReadOnlyList<string> Roles);
