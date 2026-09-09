using HR.Application.Abstractions.Models;
using HR.Application.Abstractions.Tools;
using HR.Domain.Common;
using HR.Domain.Errors;
using HR.Domain.Runs;

namespace HR.Application.Agents;

/// <summary>
/// Shared execution machinery for agents: event emission, usage recording,
/// guarded tool execution (schema-validated + agent allow-list) and the
/// LLM tool-calling loop with iteration breaker and per-step timeout.
/// </summary>
public static class AgentExecutor
{
    public static async Task<ToolExecutionResult> RunToolAsync(
        RunId runId,
        AgentServices services,
        string agentName,
        ITool tool,
        string argumentsJson,
        CancellationToken ct)
    {
        if (!tool.AllowedAgents.Contains(agentName))
            throw new PolicyViolationError($"Agent '{agentName}' is not allowed to call tool '{tool.Name}'.");

        if (tool.Effect == ToolEffect.Write)
            throw new ApprovalRequiredError($"Tool '{tool.Name}' is a write tool and may only run after human approval.");

        // OWASP LLM-10: validate tool arguments against the declared schema before execution.
        ToolArgumentValidation.Validate(tool.JsonSchema, argumentsJson);

        await services.EmitAsync(runId, new RunEvent
        {
            RunId = runId,
            Kind = RunEventKind.ToolInvoked,
            AgentName = agentName,
            ToolName = tool.Name,
            PayloadJson = argumentsJson,
        }, ct);

        var context = new ToolExecutionContext(services.Context.CorrelationId, agentName, runId.ToString(), new[] { argumentsJson });
        return await tool.ExecuteAsync(context, ct);
    }

    /// <summary>
    /// Runs a completion with tool-calling support, looping up to MaxIterations with a
    /// per-step timeout. Write tools are never executed here — they surface at the
    /// approval gate instead. Degrades to a plain text answer if the provider
    /// does not support tool calls.
    /// </summary>
    public static async Task<string> CompleteWithToolLoopAsync(
        RunId runId,
        AgentServices services,
        string systemPrompt,
        string userMessage,
        IReadOnlyList<string> toolNames,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ModelRoles.System, systemPrompt),
            new(ModelRoles.User, userMessage),
        };

        var defs = toolNames.Select(n => services.Tools.Get(n))
            .Select(t => new ToolDefinition(t.Name, t.Description, t.JsonSchema))
            .ToArray();

        var request = new CompletionRequest("agent/tool-loop", systemPrompt, messages, defs, Temperature: 0.0, MaxTokens: 1800, CorrelationId: services.Context.CorrelationId);
        var finalText = new List<string>();

        for (var iteration = 0; iteration < services.MaxIterations; iteration++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(services.PerStepTimeout);
            var result = await services.Provider.CompleteWithToolsAsync(request, timeoutCts.Token);

            await RecordUsageAsync(runId, services, new UsageRecord
            {
                CorrelationId = services.Context.CorrelationId,
                RunId = runId,
                UserId = services.Context.UserId,
                Model = result.Model,
                ProviderName = result.ProviderName,
                Kind = "chat",
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                CostUsd = result.CostUsd,
            }, ct);

            if (result.TextContent is { Length: > 0 } text)
                finalText.Add(text);

            if (result.ToolCalls is not { Count: > 0 }) break;

            foreach (var call in result.ToolCalls)
            {
                ITool tool;
                try
                {
                    tool = services.Tools.Get(call.Name);
                }
                catch (ValidationError)
                {
                    messages.Add(new ChatMessage(ModelRoles.Tool, "{\"error\":\"unknown tool\"}"));
                    continue;
                }

                string output;
                try
                {
                    var executed = await RunToolAsync(runId, services, "ask", tool, call.ArgumentsJson, ct);
                    output = executed.StructuredJson ?? executed.Text;
                }
                catch (DomainException ex)
                {
                    output = $"{{\"error\": \"{ex.Message}\"}}";
                }

                messages.Add(new ChatMessage(ModelRoles.Tool, output));
            }

            request = request with { Messages = messages, MaxTokens = 1200 };
        }

        return string.Concat(finalText);
    }

    public static Task RecordUsageAsync(RunId runId, AgentServices services, UsageRecord record, CancellationToken ct)
        => services.RecordUsageAsync(runId, record, ct);

    /// <summary>
    /// Pulls the first balanced JSON fragment (array or object) out of an LLM
    /// message, tolerating surrounding prose. Returns null when none exists.
    /// </summary>
    public static string? ExtractJsonFragment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text!.IndexOfAny(new[] { '{', '[' });
        if (start < 0) return null;
        var open = text[start];
        var close = open == '{' ? '}' : ']';
        int depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == open) depth++;
            else if (c == close) depth--;
            if (depth == 0) return text[start..(i + 1)];
        }

        return null;
    }
}
