using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// LLM-based action planner that produces structured JSON plans.
/// No tool calling. No fallbacks. Just structured output.
/// </summary>
internal sealed class StructuredActionPlanner : IStructuredActionPlanner
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<StructuredActionPlanner>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StructuredActionPlanner(
        IChatClient chatClient,
        ILogger<StructuredActionPlanner>? logger = null)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ActionPlan> PlanAsync(
        ActionPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildUserPrompt(request);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        // Add conversation history if present
        foreach (var turn in request.ConversationHistory.TakeLast(10))
        {
            var role = turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Insert(messages.Count - 1, new ChatMessage(role, turn.Content));
        }

        var response = await _chatClient.GetResponseAsync(
            messages,
            new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.Json,
                Temperature = 0.1f // Low temperature for deterministic planning
            },
            cancellationToken);

        var responseText = response.Text?.Trim() ?? string.Empty;

        return ParsePlanResponse(responseText);
    }

    private static string BuildSystemPrompt(ActionPlanRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an action planner for a UI agent system.");
        sb.AppendLine("Your ONLY job is to produce a structured JSON action plan.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("1. Output ONLY valid JSON - no explanation, no markdown, no code fences.");
        sb.AppendLine("2. Each step must reference an available component and action.");
        sb.AppendLine("3. All required parameters must be specified - do NOT leave them empty.");
        sb.AppendLine("4. If you cannot determine a required parameter, set needsClarification to true.");
        sb.AppendLine("5. Do NOT infer, guess, or assume parameter values that aren't in the request.");
        sb.AppendLine("6. If the request is ambiguous, ask for clarification.");
        sb.AppendLine("7. NAVIGATION FIRST: If an action targets a component (e.g. AgentDataGrid, AgentForm, AgentTabs) that is NOT in CURRENTLY MOUNTED COMPONENTS below, you MUST add a first step: AgentNavMenu navigate_to with arguments.uri set to the route where that component lives (e.g. /suppliers for a suppliers grid, /workspace for workspace/tabs). Then add the component action as the second step. Do not output only the component action when the user is on another page.");
        sb.AppendLine();
        sb.AppendLine("OUTPUT SCHEMA:");
        sb.AppendLine(@"{
  ""steps"": [
    {
      ""componentId"": ""string (required)"",
      ""actionId"": ""string (required)"",
      ""arguments"": { ""paramName"": ""value"" },
      ""targetAgentId"": ""string (optional, for targeting specific component instance)""
    }
  ],
  ""needsClarification"": false,
  ""clarificationQuestion"": ""string (if needsClarification is true)""
}");
        sb.AppendLine();
        sb.AppendLine("AVAILABLE COMPONENTS AND ACTIONS:");

        foreach (var component in request.AvailableComponents)
        {
            sb.AppendLine();
            sb.Append("## ").AppendLine(component.ComponentId);
            sb.AppendLine(component.Description);

            foreach (var action in component.Actions)
            {
                sb.Append("  - ").Append(action.ActionId).Append(": ").AppendLine(action.Description);

                if (action.Parameters.Count > 0)
                {
                    sb.AppendLine("    Parameters:");
                    foreach (var param in action.Parameters)
                    {
                        var required = param.Required ? "(required)" : "(optional)";
                        sb.Append("      - ").Append(param.Name).Append(' ').Append(required);
                        sb.Append(": ").Append(param.Type);
                        if (param.AllowedValues?.Count > 0)
                        {
                            sb.Append(" [").Append(string.Join(", ", param.AllowedValues)).Append(']');
                        }
                        sb.AppendLine();
                    }
                }
            }
        }

        if (request.MountedComponents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("CURRENTLY MOUNTED COMPONENTS (what is on the current page):");
            foreach (var mounted in request.MountedComponents)
            {
                sb.Append("- ").Append(mounted.AgentId).Append(" (").Append(mounted.ComponentType).Append(')');
                if (mounted.State.Count > 0)
                {
                    sb.Append(" state: ");
                    sb.Append(string.Join(", ", mounted.State.Select(kv => $"{kv.Key}={kv.Value}")));
                }
                sb.AppendLine();
            }
            sb.AppendLine("If the user asks to filter, sort, or act on a DataGrid/Form/Tabs that is NOT listed above, add AgentNavMenu navigate_to as the first step with the correct uri, then the component action.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("CURRENTLY MOUNTED COMPONENTS: (none - user is likely on home or a page without agent components)");
            sb.AppendLine("If the request involves a grid, form, or tabs, add AgentNavMenu navigate_to as the first step with uri set to the relevant route (e.g. /suppliers, /workspace), then add the component action.");
        }

        return sb.ToString();
    }

    private static string BuildUserPrompt(ActionPlanRequest request)
    {
        return $"Plan actions for: {request.UserMessage}";
    }

    private ActionPlan ParsePlanResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger?.LogWarning("Empty response from planner");
            return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
        }

        // Strip code fences if present (defensive)
        responseText = StripCodeFences(responseText);

        try
        {
            var parsed = JsonSerializer.Deserialize<PlannerResponse>(responseText, JsonOptions);
            if (parsed is null)
            {
                _logger?.LogWarning("Failed to deserialize planner response");
                return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
            }

            if (parsed.NeedsClarification)
            {
                return ActionPlan.NeedsClarification(
                    parsed.ClarificationQuestion ?? "Can you provide more details?");
            }

            if (parsed.Steps is null || parsed.Steps.Count == 0)
            {
                return ActionPlan.Empty();
            }

            var steps = parsed.Steps
                .Where(s => !string.IsNullOrWhiteSpace(s.ComponentId) && !string.IsNullOrWhiteSpace(s.ActionId))
                .Select(s => new PlannedStep
                {
                    ComponentId = s.ComponentId!,
                    ActionId = s.ActionId!,
                    Arguments = s.Arguments ?? new Dictionary<string, object?>(),
                    TargetAgentId = s.TargetAgentId
                })
                .ToList();

            return new ActionPlan
            {
                Steps = steps,
                Confidence = parsed.Confidence ?? 1.0
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse planner response: {Response}", responseText);
            return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
        }
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine > 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
            }
        }
        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }
        return trimmed.Trim();
    }

    // Internal DTO for JSON deserialization
    private sealed class PlannerResponse
    {
        public List<PlannerStep>? Steps { get; set; }
        public bool NeedsClarification { get; set; }
        public string? ClarificationQuestion { get; set; }
        public double? Confidence { get; set; }
    }

    private sealed class PlannerStep
    {
        public string? ComponentId { get; set; }
        public string? ActionId { get; set; }
        public Dictionary<string, object?>? Arguments { get; set; }
        public string? TargetAgentId { get; set; }
    }
}
