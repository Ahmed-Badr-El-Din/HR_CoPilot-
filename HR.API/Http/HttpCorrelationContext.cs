using HR.API.Auth;
using HR.Application.Abstractions;
using HR.Domain.Common;

namespace HR.API.Http;

/// <summary>
/// Request-scoped correlation + identity (FR-9). The correlation ID is minted once
/// per request by middleware, then read from Items for the whole request so the
/// orchestrator, every agent, every tool and every LLM call share it. Identity
/// comes from the authenticated claims.
/// </summary>
public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public const string ItemsKey = "hr-correlation-id";

    public string CorrelationId
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is null) return Guid.NewGuid().ToString("N");

            if (http.Items.TryGetValue(ItemsKey, out var existing) && existing is string s)
                return s;

            var id = Guid.NewGuid().ToString("N");
            http.Items[ItemsKey] = id;
            return id;
        }
    }

    public UserId UserId
    {
        get
        {
            var http = accessor.HttpContext;
            return http?.User.Identity?.IsAuthenticated == true ? JwtClaims.UserId(http.User) : new UserId("anonymous");
        }
    }

    public IReadOnlyList<string> Roles
    {
        get
        {
            var http = accessor.HttpContext;
            return http?.User.Identity?.IsAuthenticated == true ? JwtClaims.RoleNames(http.User) : Array.Empty<string>();
        }
    }
}

/// <summary>Ensures every request carries a correlation ID echoed in the response.</summary>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Correlation-Id"].ToString();
        context.Items[HttpCorrelationContext.ItemsKey] = string.IsNullOrWhiteSpace(incoming)
            ? Guid.NewGuid().ToString("N")
            : incoming;
        context.Response.Headers["X-Correlation-Id"] = context.Items[HttpCorrelationContext.ItemsKey] as string;

        await next(context);
    }
}
