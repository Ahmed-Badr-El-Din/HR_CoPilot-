using HR.Domain.Errors;

namespace HR.Application.Abstractions.Tools;

public enum ToolEffect
{
    ReadOnly,
    Write, // side-effecting: must NEVER execute without passing the approval gate
    DeterministicValidation,
}

/// <summary>Context handed to a tool during execution.</summary>
public sealed record ToolExecutionContext(
    string CorrelationId,
    string RequesterAgent,
    string? RunId,
    string[] Arguments);

/// <summary>Result of validated tool execution.</summary>
public sealed record ToolExecutionResult(string Text, string? StructuredJson = null, bool NeedsApproval = false);

/// <summary>
/// A restricted, schema-validated capability an agent may call.
/// Allow-lists are enforced per agent (OWASP LLM – excessive agency).
/// </summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON Schema (draft-07 subset) used for docs and validation.</summary>
    string JsonSchema { get; }
    ToolEffect Effect { get; }
    IReadOnlyList<string> AllowedAgents { get; }

    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken ct = default);
}

public interface IToolRegistry
{
    ITool Get(string name);
    IReadOnlyList<ITool> ForAgent(string agentName);
    IReadOnlyList<ToolDefinitionView> Definitions();
}

public sealed record ToolDefinitionView(string Name, string Description, string JsonSchema, ToolEffect Effect, IReadOnlyList<string> AllowedAgents);

public static class ToolArgumentValidation
{
    /// <summary>
    /// Validates raw JSON arguments against a light, dependency-free JSON Schema
    /// subset (required, type, properties, enum). Throws ValidationError.
    /// </summary>
    public static void Validate(string jsonSchema, string argumentsJson)
    {
        using var schemaDoc = System.Text.Json.JsonDocument.Parse(jsonSchema);
        using var argDoc = System.Text.Json.JsonDocument.Parse(argumentsJson);
        var schema = schemaDoc.RootElement;
        var args = argDoc.RootElement;

        if (args.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new ValidationError("Tool arguments must be a JSON object.");

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var p in required.EnumerateArray())
            {
                var name = p.GetString()!;
                if (!args.TryGetProperty(name, out _) || args.GetProperty(name).ValueKind == System.Text.Json.JsonValueKind.Null)
                    throw new ValidationError($"Missing required tool argument '{name}'.");
            }
        }

        if (schema.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateObject())
            {
                if (!args.TryGetProperty(prop.Name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null)
                    continue;

                var type = prop.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type is null) continue;

                var matches = type switch
                {
                    "string" => value.ValueKind is System.Text.Json.JsonValueKind.String,
                    "integer" or "number" => value.ValueKind is System.Text.Json.JsonValueKind.Number,
                    "boolean" => value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
                    "array" => value.ValueKind is System.Text.Json.JsonValueKind.Array,
                    "object" => value.ValueKind is System.Text.Json.JsonValueKind.Object,
                    _ => true,
                };

                if (!matches)
                    throw new ValidationError($"Tool argument '{prop.Name}' must be of type {type}.");

                if (prop.Value.TryGetProperty("enum", out var en) && type == "string")
                {
                    var allowed = new HashSet<string?>(en.EnumerateArray().Select(e => e.GetString()));
                    if (!allowed.Contains(value.GetString()))
                        throw new ValidationError($"Tool argument '{prop.Name}' has an invalid enum value.");
                }
            }
        }
    }
}
