using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using HR.API.Auth;
using HR.API.Endpoints;
using HR.API.Http;
using HR.Application.Abstractions;
using HR.Domain.Errors;
using HR.Infrastructure;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Providers;
using HR.Infrastructure.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HR.API;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ------------------------------------------------------------------ di
        builder.Services.AddHrInfrastructure(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new StrongIdJsonConverterFactory()));

        // Upload bound to the ingestion cap (validated again per-request).
        var ingest = builder.Configuration.GetSection(IngestionOptions.SectionName).Get<IngestionOptions>()
            ?? new IngestionOptions();
        builder.Services.Configure<FormOptions>(o =>
            o.MultipartBodyLengthLimit = ingest.MaxFileBytes + (8 * 1024 * 1024));

        // ------------------------------------------------------------- auth
        var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
            ?? new AuthOptions();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authOptions.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = authOptions.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
                };

                // SSE uses EventSource which cannot set headers; the token is passed
                // as a query parameter only on the streaming endpoints.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var path = context.HttpContext.Request.Path;
                        if (path.StartsWithSegments("/api/sessions")
                            || path.StartsWithSegments("/api/runs"))
                        {
                            var accessToken = context.Request.Query["access_token"];
                            if (!string.IsNullOrEmpty(accessToken))
                                context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy(Roles.CanManage, p => p.RequireRole(Roles.Admin));
            o.AddPolicy(Roles.CanScreen, p => p.RequireRole(Roles.Admin, Roles.HiringManager));
            o.AddPolicy(Roles.CanAudit, p => p.RequireRole(Roles.Admin, Roles.Auditor));
            o.AddPolicy(Roles.CanApprove, p => p.RequireRole(Roles.Admin, Roles.HiringManager));
            o.AddPolicy(Roles.CanSeed, p => p.RequireRole(Roles.Admin));
        });

        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));

        // OWASP Web A04: per-client token-bucket rate limiting on every inbound route.
        var rateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    type = "https://httpstatuses.com/429",
                    title = "RateLimitedError",
                    status = 429,
                    code = "rate_limited",
                    detail = "Too many requests. Retry later.",
                    correlation_id = context.HttpContext.Items[HttpCorrelationContext.ItemsKey],
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var key = context.User.Identity?.Name
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = Math.Max(1, rateLimits.Burst),
                    TokensPerPeriod = Math.Max(1, rateLimits.TokensPerMinute),
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "HR Copilot — D6T1",
                Version = "v1",
                Description = "Agentic bilingual HR talent-screening API. Variant D6 (talent screening), Twist T1 (Arabic + English).",
            });
            o.AddSecurityDefinition("bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            });
        });

        var app = builder.Build();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseCors();
        app.UseMiddleware<CorrelationMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (DomainException ex)
            {
                if (context.Response.HasStarted) throw;
                context.Response.StatusCode = ex.HttpStatus;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    type = "https://httpstatuses.com/" + ex.HttpStatus,
                    title = ex.GetType().Name,
                    status = ex.HttpStatus,
                    code = ex.Code,
                    detail = ex.Message,
                    correlation_id = context.Items[HttpCorrelationContext.ItemsKey],
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
        });

        app.MapGet("/", () => Results.Ok(new { service = "HR Copilot", variant = "D6T1", docs = "/swagger" }))
            .AllowAnonymous();

        app.MapSwagger().AllowAnonymous();
        app.MapAuthEndpoints();
        app.MapDocumentEndpoints();
        app.MapSessionEndpoints();
        app.MapWorkflowEndpoints();
        app.MapObservabilityEndpoints();

        // ------------------------------------------------------- boot & seed
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HrDbContext>();
            await db.Database.MigrateAsync();

            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var auth = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>();
            await DemoAccountSeeder.SeedAsync(users, roles, auth);

            if (ingest.SeedCorpusOnStartup)
            {
                var seeder = scope.ServiceProvider.GetRequiredService<CorpusSeedService>();
                await seeder.SeedAsync(force: false);
            }
        }

        await app.RunAsync();
        return 0;
    }
}
