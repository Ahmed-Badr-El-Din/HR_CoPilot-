using System.Text.Json.Serialization;

namespace HR.Infrastructure.Providers;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public decimal PricingPer1MInput { get; set; } = 0.15m;
    public decimal PricingPer1MOutput { get; set; } = 0.60m;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxRetries { get; set; } = 2;

    /// <summary>Base delay for exponential backoff between retries of the same provider.</summary>
    public int RetryBackoffBaseMs { get; set; } = 250;

    /// <summary>Comma-separated provider order, e.g. "openai,local". Used by the resilient chain.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public string FallbackOrder { get; set; } = "openai,local";
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string ConnectionString { get; set; } = "Data Source=hr.db";
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string JwtIssuer { get; set; } = "hr-copilot";
    public string JwtAudience { get; set; } = "hr-copilot-clients";
    public string JwtSigningKey { get; set; } = "CHANGE_ME___at_least_32_characters_long";
    public int TokenLifetimeHours { get; set; } = 12;
    public string DefaultAdminEmail { get; set; } = "admin@hr.local";
    public string DefaultAdminPassword { get; set; } = "ChangeMe1!";
    public string DefaultManagerEmail { get; set; } = "manager@hr.local";
    public string DefaultManagerPassword { get; set; } = "ChangeMe1!";
    public string DefaultAuditorEmail { get; set; } = "auditor@hr.local";
    public string DefaultAuditorPassword { get; set; } = "ChangeMe1!";
}

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";
    public int MaxFileBytes { get; set; } = 25 * 1024 * 1024;
    public int ChunkMaxCharacters { get; set; } = 1400;
    public bool SeedCorpusOnStartup { get; set; } = true;
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Token-bucket refill rate per client, per minute.</summary>
    public int TokensPerMinute { get; set; } = 120;

    /// <summary>Burst capacity of the token bucket.</summary>
    public int Burst { get; set; } = 40;
}
