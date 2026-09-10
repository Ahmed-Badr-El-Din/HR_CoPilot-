using HR.Application.Abstractions.Tools;
using HR.Domain.Errors;

namespace HR.Application.Tools;

public sealed class ToolRegistry(
    IEnumerable<ITool> tools) : IToolRegistry
{
    private readonly Dictionary<string, ITool> _byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public ITool Get(string name)
    {
        if (!_byName.TryGetValue(name, out var tool))
            throw new ValidationError($"Unknown tool '{name}'.");
        return tool;
    }

    public IReadOnlyList<ITool> ForAgent(string agentName)
        => _byName.Values.Where(t => t.AllowedAgents.Contains(agentName)).ToList();

    public IReadOnlyList<ToolDefinitionView> Definitions()
        => _byName.Values
            .OrderBy(t => t.Name)
            .Select(t => new ToolDefinitionView(t.Name, t.Description, t.JsonSchema, t.Effect, t.AllowedAgents))
            .ToList();
}
