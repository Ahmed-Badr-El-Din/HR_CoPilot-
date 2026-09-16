using System.Security.Claims;
using HR.API.Auth;
using HR.Domain.Errors;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace HR.API.Endpoints;

public sealed record LoginRequest(string Email, string Password);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync);
        group.MapGet("/me", MeAsync).RequireAuthorization();
    }

    private static async Task<Ok<object>> LoginAsync(
        LoginRequest request,
        UserManager<IdentityUser> users,
        JwtTokenService tokens)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedError("Invalid email or password.");

        var roles = (await users.GetRolesAsync(user)).ToArray();
        var token = tokens.CreateToken(new HR.Domain.Common.UserId(user.Id), user.Email!, user.Email!, roles);

        return TypedResults.Ok<object>(new
        {
            access_token = token.AccessToken,
            token_type = token.TokenType,
            expires_in = token.ExpiresIn,
            email = user.Email,
            roles = token.Roles,
        });
    }

    private static Ok<object> MeAsync(ClaimsPrincipal user)
        => TypedResults.Ok<object>(new
        {
            sub = JwtClaims.UserId(user).Value,
            email = JwtClaims.Email(user),
            name = user.Identity?.Name,
            roles = JwtClaims.RoleNames(user),
        });
}
