using System.Reflection;
using System.Text.Json;
using HR.Application.Abstractions;

namespace HR.Infrastructure.Prompts;

public sealed class EmbeddedPromptCatalog : IPromptCatalog
{
    public const string RootNamespace = "HR.Infrastructure.Prompts.assets";

    private sealed class Asset
    {
        public string Id { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Language { get; set; } = "en";
        public string Description { get; set; } = string.Empty;
        public string System { get; set; } = string.Empty;
    }

    private static readonly Lazy<IReadOnlyDictionary<string, Asset>> Cache = new(BuildCache);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // The versioned prompt assets use lower-case keys ("id", "version", ...);
        // without case-insensitive binding every asset deserialises to an empty id
        // and the whole catalog silently resolves to "not found".
        PropertyNameCaseInsensitive = true,
    };

    public string GetPrompt(string promptId, int majorVersion)
        => Resolve(promptId, majorVersion).System;

    public string GetPrompt(string promptId)
    {
        var asset = Cache.Value.TryGetValue(promptId, out var a) ? a : null;
        return asset?.System ?? string.Empty;
    }

    public IReadOnlyList<PromptAssetInfo> List()
        => Cache.Value.Values
            .Select(a => new PromptAssetInfo(a.Id, $"v{a.Version}", a.Language, a.Description))
            .OrderBy(i => i.Id)
            .ToList();

    private static Asset Resolve(string promptId, int version)
    {
        if (Cache.Value.TryGetValue(promptId, out var asset) && asset.Version == version) return asset;
        if (Cache.Value.TryGetValue(promptId, out var fallback)) return fallback;
        throw new Domain.Errors.ValidationError($"Prompt artifact '{promptId}' v{version} not found.");
    }

    private static IReadOnlyDictionary<string, Asset> BuildCache()
    {
        var assembly = typeof(EmbeddedPromptCatalog).Assembly;
        var files = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(RootNamespace, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal));
        var map = new Dictionary<string, Asset>(StringComparer.Ordinal);
        foreach (var name in files)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            var asset = JsonSerializer.Deserialize<Asset>(stream, JsonOptions);
            if (asset is { Id.Length: > 0 }) map[asset.Id] = asset;
        }

        return map;
    }
}
