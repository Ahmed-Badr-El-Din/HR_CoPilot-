using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Persistence;
using HR.Application.Authorization;
using HR.Application.Workflows;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Domain.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HR.API.Endpoints;

public sealed record CreateSessionRequest(string? Title, string Language = "auto");

public sealed record AskRequest(string Question, string Language = "auto");

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions");
        group.MapPost("/", CreateAsync).RequireAuthorization();
        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", DetailAsync).RequireAuthorization();
        group.MapPost("/{id:guid}/ask", AskStreamAsync).RequireAuthorization();
    }

    private static async Task<Ok<object>> CreateAsync(
        CreateSessionRequest request,
        ICorrelationContext ctx,
        IHrUnitOfWork store,
        CancellationToken ct)
    {
        var session = new ChatSession
        {
            OwnerUserId = ctx.UserId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? "New conversation" : request.Title!.Trim(),
            PreferredLanguage = NormalizeLanguage(request.Language),
        };
        await store.Sessions.AddAsync(session);
        await store.SaveChangesAsync(ct);

        return TypedResults.Ok<object>(ToDto(session));
    }

    private static async Task<Ok<object>> ListAsync(ICorrelationContext ctx, IHrUnitOfWork store, CancellationToken ct)
    {
        var sessions = await store.Sessions.ListByUserAsync(ctx.UserId, ct);
        return TypedResults.Ok<object>(new
        {
            sessions = sessions.Select(ToDto).ToArray(),
        });
    }

    private static async Task<Ok<object>> DetailAsync(
        string id,
        ICorrelationContext ctx,
        IHrUnitOfWork store)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new ValidationError($"Invalid session id '{id}'.");
        var session = await store.Sessions.GetByIdAsync(new SessionId(guid))
            ?? throw new SessionNotFoundError(id);

        AuthorizationService.EnsureResourceOwnerOrAuditor(session.OwnerUserId, ctx, "session");
        return TypedResults.Ok<object>(ToDetailDto(session));
    }

    /// <summary>
    /// SSE stream for a grounded answer. Events: starter / token / citation /
    /// refused / done / error. Client cancellation (aborting the request) flows
    /// through the shared CancellationToken and actually stops the provider call
    /// and the DB writes server-side (FR-6).
    /// </summary>
    private static async Task AskStreamAsync(
        string id,
        AskRequest request,
        HttpContext http,
        ICorrelationContext ctx,
        IHrUnitOfWork store,
        AskService ask,
        CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid))
            throw new ValidationError($"Invalid session id '{id}'.");
        var sessionId = new SessionId(guid);
        var session = await store.Sessions.GetByIdAsync(sessionId)
            ?? throw new SessionNotFoundError(id);
        AuthorizationService.EnsureResourceOwnerOrAuditor(session.OwnerUserId, ctx, "session");

        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ValidationError("question is required.");

        var language = NormalizeLanguage(request.Language);
        var runId = await ask.StartAsync(sessionId, request.Question, language, ctx, ct);

        var response = http.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        try
        {
            await foreach (var ev in ask.StreamAsync(sessionId, request.Question, language, ctx, runId, ct))
            {
                switch (ev.Kind)
                {
                    case AskEventKind.Starter:
                        await SendSseAsync(response, "starter", new { strategy = ev.Text, run_id = runId.ToString() });
                        break;
                    case AskEventKind.Token:
                        await SendSseAsync(response, "token", new { text = ev.Text });
                        break;
                    case AskEventKind.Citation:
                        await SendSseAsync(response, "citation", ev.Citation);
                        break;
                    case AskEventKind.Refused:
                        await SendSseAsync(response, "refused", new { message = ev.Text });
                        break;
                    case AskEventKind.Done:
                        await SendSseAsync(response, "done", new { status = ev.Text });
                        break;
                    case AskEventKind.Error:
                        await SendSseAsync(response, "error", new { message = ev.Text });
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await SendSseAsync(response, "cancelled", new { run_id = runId.ToString() });
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await SendSseAsync(response, "error", new { message = ex.Message });
        }
    }

    internal static async Task SendSseAsync(HttpResponse response, string eventName, object? data)
    {
        await response.WriteAsync($"event: {eventName}\n");
        await response.WriteAsync($"data: {JsonSerializer.Serialize(data, JsonOpts)}\n\n");
        await response.Body.FlushAsync();
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string NormalizeLanguage(string language)
        => language switch
        {
            "ar" or "arb" => "ar",
            "en" or "eng" => "en",
            _ => "auto",
        };

    private static object ToDto(ChatSession s)
        => new
        {
            id = s.Id.ToString(),
            title = s.Title,
            language = s.PreferredLanguage,
            created_at = s.CreatedAt,
            updated_at = s.UpdatedAt,
        };

    private static object ToDetailDto(ChatSession s)
    {
        var messages = s.Messages
            .OrderBy(m => m.Id)
            .Select(m => new
            {
                role = m.Role,
                content = m.Content,
                refused = m.Refused,
                created_at = m.CreatedAt,
                run_id = m.RunId?.ToString(),
                citations = ParseCitations(m.CitationsJson),
            })
            .ToArray();

        return new
        {
            id = s.Id.ToString(),
            title = s.Title,
            language = s.PreferredLanguage,
            created_at = s.CreatedAt,
            updated_at = s.UpdatedAt,
            messages,
        };
    }

    private static object[] ParseCitations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<object>();
        try
        {
            return JsonSerializer.Deserialize<object[]>(json, JsonOpts) ?? Array.Empty<object>();
        }
        catch (JsonException)
        {
            return Array.Empty<object>();
        }
    }
}
