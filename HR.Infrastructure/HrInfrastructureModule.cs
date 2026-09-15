using HR.Application.Abstractions;
using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Processing;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Abstractions.Tools;
using HR.Application.Agents;
using HR.Application.Documents;
using HR.Application.Tools;
using HR.Application.Workflows;
using HR.Domain.Common;
using HR.Infrastructure.Common;
using HR.Infrastructure.Embedding;
using HR.Infrastructure.Nlp;
using HR.Infrastructure.Observability;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Processing;
using HR.Infrastructure.Prompts;
using HR.Infrastructure.Providers;
using HR.Infrastructure.Retrieval;
using HR.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure;

/// <summary>
/// Composition root for the whole system. Only this module (in Infrastructure) is
/// allowed to touch SDKs: EF Core, Identity, HTTP, providers. The application and
/// domain layers observe only the ports registered here, so swapping the LLM
/// provider, embedding model or vector store is configuration plus one adapter.
/// </summary>
public static class HrInfrastructureModule
{
    public static IServiceCollection AddHrInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        // ---- Relational store (FR-1) + Identity (FR-8). Dev uses SQLite; the
        //      connection string is externalised so Postgres is pure config. ----
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        services.AddDbContext<HrDbContext>(o => o.UseSqlite(storage.ConnectionString));
        services.AddIdentityCore<IdentityUser>(o =>
            {
                var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
                o.Password.RequireDigit = true;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<HrDbContext>();

        // ---- Ports -> adapters (Core ports live in HR.Application). ----
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<HashingEmbedder>();
        services.AddSingleton<IEventEmitter, InMemoryEventEmitter>();
        services.AddSingleton<IPromptCatalog, EmbeddedPromptCatalog>();

        services.AddSingleton<LocalModelProvider>();
        services.AddHttpClient<OpenAiModelProvider>();
        services.AddSingleton<IModelProvider>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
            var byName = new Dictionary<string, Func<IModelProvider>>(StringComparer.Ordinal)
            {
                ["openai"] = () => sp.GetRequiredService<OpenAiModelProvider>(),
                ["local"] = () => sp.GetRequiredService<LocalModelProvider>(),
            };

            var order = string.IsNullOrWhiteSpace(options.FallbackOrder)
                ? new[] { "local" }
                : options.FallbackOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var chain = order.Where(byName.ContainsKey).Select(name => byName[name]()).ToArray();
            if (chain.Length == 0)
                chain = new IModelProvider[] { sp.GetRequiredService<LocalModelProvider>() };

            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientModelProvider>>();
            return new ResilientModelProvider(chain, options, logger);
        });

        services.AddScoped<IHrUnitOfWork, UnitOfWork>();
        services.AddScoped<IVectorStore, SqliteVectorStore>();
        services.AddScoped<IChunkLookup, DbChunkLookup>();
        services.AddScoped<ChunkSource, DbChunkSource>();
        services.AddScoped<KeywordIndex>();
        services.AddSingleton<IArabicNormalizer, ArabicNormalizer>();
        services.AddScoped<IRetrievalService, HybridRetriever>();

        services.AddSingleton<PdfTextParser>();
        services.AddSingleton<DocxTextParser>();
        services.AddSingleton<IDocumentParser, CompositeDocumentParser>();
        services.AddSingleton<ITextCleaner, TextCleaner>();
        services.AddSingleton<IDocumentChunker, StructureChunker>();

        services.AddScoped<ITool, SearchCorpusTool>();
        services.AddScoped<ITool, ReadChunkTool>();
        services.AddScoped<ITool, ValidateShortlistTool>();
        services.AddScoped<ITool, PublishShortlistTool>();
        services.AddScoped<IToolRegistry, ToolRegistry>();

        services.AddScoped<IBiasWriter, AuditBiasWriter>();
        services.AddScoped<DocumentIngestionService>();
        services.AddScoped<AskService>();
        services.AddScoped<ScreeningOrchestrator>();
        services.AddScoped<CorpusSeedService>();

        return services;
    }
}
