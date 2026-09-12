using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HR.Application.Abstractions.Models;
using HR.Domain.Errors;
using HR.Infrastructure.Embedding;
using HR.Infrastructure.Nlp;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Providers;

/// <summary>
/// Hosted provider speaking the OpenAI Chat Completions protocol. Works against
/// OpenAI itself and every OpenAI-compatible free tier (Groq, OpenRouter, Mistral,
/// Together, Cerebras, deepseek, ollama's /v1 shim) via configuration only — which
/// is exactly the "free tier running out" requirement: swap base-url + model, or
/// switch the fallback order, no code change.
/// </summary>
public sealed class OpenAiModelProvider(
    HttpClient http,
    IOptions<LlmOptions> options) : IModelProvider
{
    private readonly LlmOptions _options = options.Value;

    public string Name => $"openai-compatible:{_options.ChatModel}";
    public bool IsHosted => true;
    public string Description => "OpenAI-compatible chat + embeddings API. Configure BaseUrl/ApiKey for Groq/OpenRouter/OpenAI/etc.";

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.Completion | ProviderCapabilities.Streaming | ProviderCapabilities.ToolCalling;

    public async Task<CompletionResult> CompleteAsync(CompletionRequest request, CancellationToken ct)
    {
        var payload = BuildPayload(request, stream: false, tools: request.Tools);
        var response = await PostJsonAsync(payload, request.CorrelationId, ct);
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        var content = ReadContent(root);
        var usage = ReadUsage(root);
        return new CompletionResult(content, _options.ChatModel, Name, usage.Item1, usage.Item2, Cost(usage.Item1, usage.Item2));
    }

    public async IAsyncEnumerable<StreamingDelta> StreamAsync(CompletionRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var payload = BuildPayload(request, stream: true, tools: null);
        payload["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{TrimUrl(_options.BaseUrl)}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        AttachAuth(httpReq, request.CorrelationId);

        using var response = await http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw ProviderStatusError((int)response.StatusCode, body, response.Headers.RetryAfter?.Delta);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        int promptTokens = 0;
        int completionTokens = 0;
        var builder = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            // Parse into locals inside the guarded block; yield stays OUTSIDE it
            // because C# forbids `yield` in a try-with-catch.
            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    promptTokens = ReadInt(usage, "prompt_tokens");
                    completionTokens = ReadInt(usage, "completion_tokens");
                }

                delta = ExtractDelta(root);
            }
            catch (JsonException)
            {
                // skip malformed keep-alive frames
            }

            if (!string.IsNullOrEmpty(delta))
            {
                builder.Append(delta);
                yield return new StreamingDelta(delta, Final: false);
            }

            ct.ThrowIfCancellationRequested();
        }

        var estimatedPrompt = promptTokens > 0 ? promptTokens : TextNormalizer.EstimateTokens(BuildContextPreview(request));
        var estimatedCompletion = completionTokens > 0 ? completionTokens : Math.Max(1, builder.Length / 4);
        yield return new StreamingDelta(string.Empty, Final: true, estimatedPrompt, estimatedCompletion);
    }

    public async Task<ToolCallResult> CompleteWithToolsAsync(CompletionRequest request, CancellationToken ct)
    {
        var payload = BuildPayload(request, stream: false, tools: request.Tools);
        var response = await PostJsonAsync(payload, request.CorrelationId, ct);
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        var toolCalls = ReadToolCalls(root);
        var content = ReadContent(root);
        var usage = ReadUsage(root);
        return new ToolCallResult(toolCalls, string.IsNullOrWhiteSpace(content) ? null : content, _options.ChatModel, Name, usage.Item1, usage.Item2, Cost(usage.Item1, usage.Item2));
    }

    public async Task<EmbeddingResult> EmbedManyAsync(IReadOnlyList<string> texts, string? modelOverride = null, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = modelOverride ?? _options.EmbeddingModel,
            ["input"] = texts.ToArray(),
        };

        var json = await PostAsync(payload, $"{TrimUrl(_options.BaseUrl)}/embeddings", ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var vectors = root.GetProperty("data").EnumerateArray()
            .OrderBy(e => e.GetProperty("index").GetInt32())
            .Select(e => e.GetProperty("embedding").EnumerateArray().Select(v => (float)v.GetDouble()).ToArray())
            .ToArray();
        var tokenUsage = root.TryGetProperty("usage", out var u) ? ReadInt(u, "total_tokens") : texts.Sum(TextNormalizer.EstimateTokens);
        return new EmbeddingResult(vectors, vectors.Length > 0 ? vectors[0].Length : 0, _options.EmbeddingModel, Name, tokenUsage, 0m);
    }

    // ---------- internals ----------

    private Dictionary<string, object> BuildPayload(CompletionRequest request, bool stream, IReadOnlyList<ToolDefinition>? tools)
    {
        var messages = request.Messages.Select(m => (object)new { role = m.Role, content = m.Content }).ToList();
        messages.Insert(0, new { role = ModelRoles.System, content = request.SystemPrompt });

        var payload = new Dictionary<string, object>
        {
            ["model"] = _options.ChatModel,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream,
        };

        if (tools is { Count: > 0 })
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(t.JsonSchema),
                },
            }).ToArray();
        }

        return payload;
    }

    private async Task<string> PostJsonAsync(object payload, string? correlationId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{TrimUrl(_options.BaseUrl)}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        AttachAuth(req, correlationId);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw ProviderStatusError((int)resp.StatusCode, body, resp.Headers.RetryAfter?.Delta);
        }

        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> PostAsync(object payload, string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        AttachAuth(req);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw ProviderStatusError((int)resp.StatusCode, body, resp.Headers.RetryAfter?.Delta);
        }

        return await resp.Content.ReadAsStringAsync(ct);
    }

    private void AttachAuth(HttpRequestMessage req, string? correlationId = null)
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(correlationId))
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
    }

    private static string TrimUrl(string url) => url.TrimEnd('/').Replace("/v1/v1", "/v1", StringComparison.Ordinal);

    private decimal Cost(int inTokens, int outTokens)
        => (decimal)inTokens / 1_000_000m * _options.PricingPer1MInput + (decimal)outTokens / 1_000_000m * _options.PricingPer1MOutput;

    private static string ReadContent(JsonElement root)
    {
        var first = root.GetProperty("choices")[0];
        var message = first.TryGetProperty("message", out var m) ? m : default;
        return message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? ExtractDelta(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
        var delta = choices[0].TryGetProperty("delta", out var d) ? d : default;
        if (delta.ValueKind != JsonValueKind.Object) return null;
        return delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
    }

    private static IReadOnlyList<ToolCallInput>? ReadToolCalls(JsonElement root)
    {
        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0) return null;
        var message = choices[0].TryGetProperty("message", out var m) ? m : default;
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<ToolCallInput>();
        foreach (var c in calls.EnumerateArray())
        {
            var id = c.TryGetProperty("id", out var i) ? i.GetString() ?? string.Empty : string.Empty;
            var name = c.GetProperty("function").GetProperty("name").GetString() ?? string.Empty;
            var args = c.GetProperty("function").GetProperty("arguments").GetString() ?? "{}";
            list.Add(new ToolCallInput(id, name, args));
        }

        return list.Count == 0 ? null : list;
    }

    private static (int, int) ReadUsage(JsonElement root)
        => root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object
            ? (ReadInt(u, "prompt_tokens"), ReadInt(u, "completion_tokens"))
            : (0, 0);

    private static int ReadInt(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.TryGetInt32(out var n) ? n : 0;

    private static string BuildContextPreview(CompletionRequest request)
        => request.SystemPrompt + "\n" + string.Join("\n", request.Messages.Select(m => m.Content));

    private static DomainException ProviderStatusError(int status, string body, TimeSpan? retryAfter)
        => status == 429
            ? new RateLimitedError($"Provider rate-limited (HTTP 429){(retryAfter is not null ? $", retry after {retryAfter.Value.TotalSeconds:F0}s" : string.Empty)}. {Cut(body)}")
            : new ProviderUnavailableError($"Provider returned HTTP {status}. {Cut(body)}");

    private static string Cut(string body) => body.Length > 300 ? body[..300] : body;
}
