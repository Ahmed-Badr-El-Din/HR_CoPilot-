using HR.Application.Abstractions;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Domain.Documents;
using HR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HR.API.Endpoints;

public static class ObservabilityEndpoints
{
    public static void MapObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", HealthAsync);
        app.MapGet("/readyz", ReadinessAsync);
        app.MapGet("/api/usage", UsageAsync).RequireAuthorization(Roles.CanAudit);
        app.MapGet("/api/bias/audit", BiasAuditAsync).RequireAuthorization(Roles.CanAudit);
        app.MapGet("/api/corpus/stats", CorpusStatsAsync).RequireAuthorization();
    }

    private static Ok<object> HealthAsync()
        => TypedResults.Ok<object>(new { status = "ok", service = "hr-copilot", time = DateTimeOffset.UtcNow });

    private static async Task<Results<Ok<object>, StatusCodeHttpResult>> ReadinessAsync(
        HrDbContext db,
        IModelProvider provider)
    {
        var dbOk = await IsDatabaseReachableAsync(db);
        var state = new
        {
            status = dbOk ? "ready" : "not_ready",
            database = dbOk ? "ok" : "unreachable",
            provider = provider.Name,
            provider_hosted = provider.IsHosted,
            provider_capabilities = provider.Capabilities.ToString(),
            time = DateTimeOffset.UtcNow,
        };

        return dbOk
            ? TypedResults.Ok<object>(state)
            : TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<bool> IsDatabaseReachableAsync(HrDbContext db)
    {
        try
        {
            await db.Chunks.CountAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Ok<object>> UsageAsync(IHrUnitOfWork store, int take = 50)
    {
        var (promptTokens, completionTokens, cost) = await store.Usage.TotalsAsync();
        var records = await store.Usage.ListAllAsync(0, Math.Clamp(take, 1, 500));
        return TypedResults.Ok<object>(new
        {
            totals = new { prompt_tokens = promptTokens, completion_tokens = completionTokens, cost_usd = cost },
            records = records.Select(u => new
            {
                correlation_id = u.CorrelationId,
                user_id = u.UserId.Value,
                run_id = u.RunId?.ToString(),
                kind = u.Kind,
                model = u.Model,
                provider = u.ProviderName,
                prompt_tokens = u.PromptTokens,
                completion_tokens = u.CompletionTokens,
                cost_usd = u.CostUsd,
                created_at = u.CreatedAt,
            }),
        });
    }

    private static async Task<Ok<object>> BiasAuditAsync(IHrUnitOfWork store, string? candidate_id = null)
    {
        var records = string.IsNullOrWhiteSpace(candidate_id)
            ? await store.BiasAudits.ListByCandidateAsync("", default)
            : await store.BiasAudits.ListByCandidateAsync(candidate_id);
        return TypedResults.Ok<object>(new
        {
            candidate_id,
            records = records.Select(r => new
            {
                r.CandidateId,
                attribute = r.AttributeKind.ToString(),
                r.Occurrences,
                r.Pattern,
                r.Timestamp,
            }),
        });
    }

    private static async Task<Ok<object>> CorpusStatsAsync(IHrUnitOfWork store)
    {
        var docs = await store.Documents.ListAsync(0, 1000);
        return TypedResults.Ok<object>(new
        {
            documents = docs.Count,
            ready_documents = docs.Count(d => d.Status == DocumentStatus.Ready),
            failed_documents = docs.Count(d => d.Status == DocumentStatus.Failed),
            languages = docs
                .GroupBy(d => d.Language)
                .Select(g => new { language = g.Key.ToString(), documents = g.Count() })
                .ToArray(),
        });
    }
}
