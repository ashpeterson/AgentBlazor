using System.Text;
using System.Text.Json;
using AgentBlazor.Core.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// LLM planner that emits deterministic action plans.
/// The model can only choose known components/actions and known generated-UI tools.
/// </summary>
internal sealed class StructuredActionPlanner : IStructuredActionPlanner
{
    private readonly IChatClient? _chatClient;
    private readonly IAgentUiToolCatalog _uiToolCatalog;
    private readonly ILogger<StructuredActionPlanner>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public bool IsProviderConfigured => _chatClient is not null;

    public StructuredActionPlanner(
        IChatClient? chatClient = null,
        IAgentUiToolCatalog? uiToolCatalog = null,
        ILogger<StructuredActionPlanner>? logger = null)
    {
        _chatClient = chatClient;
        _uiToolCatalog = uiToolCatalog ?? new DefaultAgentUiToolCatalog();
        _logger = logger;
    }

    public async Task<ActionPlan> PlanAsync(
        ActionPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            return ActionPlan.NeedsClarification(
                "No provider is configured. Register an AgentBlazor provider chat client.");
        }

        var systemPrompt = BuildSystemPrompt(request);
        var userPrompt = BuildUserPrompt(request);

        _logger?.LogDebug("System prompt length: {Length} chars", systemPrompt.Length);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        foreach (var turn in request.ConversationHistory.TakeLast(10))
        {
            var role = turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Insert(messages.Count - 1, new ChatMessage(role, turn.Content));
        }

        var responseText = await RequestPlannerResponseAsync(messages, cancellationToken);
        var plan = ParsePlanResponse(responseText, request.GenerateUi);

        if (!RequiresGeneratedUiRepair(request, plan))
        {
            return plan;
        }

        var repairedPlan = await TryRepairGeneratedUiPlanAsync(request, responseText, plan, cancellationToken);
        if (repairedPlan is not null)
        {
            plan = repairedPlan;
        }

        if (plan.UiToolCalls.Count == 0 && !plan.RequiresClarification)
        {
            plan = AttachFallbackGeneratedUiSummary(plan, request.UserMessage);
        }

        return plan;
    }

    private string BuildSystemPrompt(ActionPlanRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# ROLE");
        sb.AppendLine("You are a deterministic UI action planner.");
        sb.AppendLine("You must return only valid JSON.");
        sb.AppendLine();

        sb.AppendLine("# OUTPUT SCHEMA");
        sb.AppendLine("Return JSON matching this schema:");
        sb.AppendLine("```json");
        sb.AppendLine("""
{
  "reasoning": "brief explanation",
  "steps": [
    {
      "componentId": "must match an available component id",
      "actionId": "must match an action on that component",
      "arguments": {},
      "targetAgentId": "optional specific component instance"
    }
  ],
  "needsClarification": false,
  "clarificationQuestion": "only when needsClarification=true",
  "confidence": 1.0,
  "uiToolCalls": [
    {
      "toolId": "must match an available generated-ui tool id",
      "arguments": {}
    }
  ]
}
""");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("# HARD RULES");
        sb.AppendLine("- Output raw JSON only. No markdown wrappers.");
        sb.AppendLine("- Use only available component ids and action ids listed below.");
        sb.AppendLine("- Include all required parameters.");
        sb.AppendLine("- If required parameters are missing, set needsClarification=true with a specific question.");
        sb.AppendLine("- Do not invent routes, columns, or action names.");
        sb.AppendLine("- For rank intents (highest/lowest), use sort desc/asc and add select_row if user says focus/open top item.");
        sb.AppendLine("- For AgentForm.set_field, semantic field hints are allowed; runtime resolves canonical fields.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.AgentInstructions))
        {
            sb.AppendLine("# AGENT-SPECIFIC INSTRUCTIONS");
            sb.AppendLine(request.AgentInstructions.Trim());
            sb.AppendLine();
        }

        if (request.GenerateUi)
        {
            sb.AppendLine("# GENERATED UI RULES");
            sb.AppendLine("- uiToolCalls is REQUIRED and should usually contain at least one tool call.");
            sb.AppendLine("- Use only generated-ui tools listed below.");
            sb.AppendLine("- Do not emit freeform generated UI blocks.");
            sb.AppendLine("- For GENERATED_UI_ACTION payloads, treat payload values as authoritative and emit executable steps.");
            sb.AppendLine("- Ignore payload metadata keys: blockId, actionId.");
            sb.AppendLine("- For generated UI follow-up actions that populate forms, do not include AgentForm.submit unless the user explicitly asks to submit.");
            sb.AppendLine("- uiToolCalls should typically return a confirmation or follow-up tool after execution.");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("# GENERATED UI RULES");
            sb.AppendLine("- uiToolCalls must be an empty array when GENERATE_UI is false.");
            sb.AppendLine();
        }

        sb.AppendLine("# AVAILABLE COMPONENTS");
        foreach (var component in request.AvailableComponents)
        {
            sb.Append("- ").Append(component.ComponentId).Append(": ").AppendLine(component.Description);
            foreach (var action in component.Actions)
            {
                sb.Append("  - ").Append(action.ActionId);
                if (action.RequiresApproval)
                {
                    sb.Append(" [requires approval]");
                }

                sb.Append(": ").AppendLine(action.Description);
                if (action.Parameters.Count == 0)
                {
                    continue;
                }

                sb.AppendLine("    parameters:");
                foreach (var parameter in action.Parameters)
                {
                    sb.Append("      - ").Append(parameter.Name)
                        .Append(" (").Append(parameter.Type).Append(", ")
                        .Append(parameter.Required ? "required" : "optional").Append(')');
                    if (!string.IsNullOrWhiteSpace(parameter.Description))
                    {
                        sb.Append(": ").Append(parameter.Description);
                    }

                    if (parameter.AllowedValues is { Count: > 0 })
                    {
                        sb.Append(" [allowed: ").Append(string.Join(", ", parameter.AllowedValues)).Append(']');
                    }

                    sb.AppendLine();
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("# MOUNTED COMPONENTS");
        if (request.MountedComponents.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var mounted in request.MountedComponents)
            {
                sb.Append("- ").Append(mounted.AgentId)
                    .Append(" (type: ").Append(mounted.ComponentType).Append(')')
                    .AppendLine();

                if (mounted.State.Count == 0)
                {
                    continue;
                }

                foreach (var state in mounted.State)
                {
                    sb.Append("  - ").Append(state.Key).Append(": ").AppendLine(state.Value);
                }
            }
        }
        sb.AppendLine();

        if (request.AvailableRoutes.Count > 0)
        {
            sb.AppendLine("# AVAILABLE ROUTES");
            foreach (var route in request.AvailableRoutes)
            {
                sb.Append("- ").Append(route.Path);
                if (!string.IsNullOrWhiteSpace(route.Description))
                {
                    sb.Append(": ").Append(route.Description);
                }

                if (route.Aliases.Count > 0)
                {
                    sb.Append(" (keywords: ").Append(string.Join(", ", route.Aliases.Take(5))).Append(')');
                }

                sb.AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine("# AVAILABLE GENERATED-UI TOOLS");
        foreach (var tool in _uiToolCatalog.GetTools())
        {
            sb.Append("- ").Append(tool.ToolId).Append(": ").AppendLine(tool.Description);
            sb.AppendLine("  inputSchema:");
            sb.Append("  ").AppendLine(tool.InputSchema.Replace("\n", "\n  ", StringComparison.Ordinal));
        }
        sb.AppendLine();

        sb.AppendLine("# CHECKLIST");
        sb.AppendLine("1. Identify intent.");
        sb.AppendLine("2. Choose component actions.");
        sb.AppendLine("3. Add navigation first when target component is not mounted.");
        sb.AppendLine("4. Ensure required params are present; otherwise ask clarification.");
        sb.AppendLine("5. Add deterministic uiToolCalls only from allowed tools.");
        sb.AppendLine();
        sb.AppendLine("Return JSON now.");

        return sb.ToString();
    }

    private static string BuildUserPrompt(ActionPlanRequest request)
    {
        var generatedUiAction = request.GeneratedUiAction;
        if (generatedUiAction is not null)
        {
            var actionJson = JsonSerializer.Serialize(new
            {
                blockId = generatedUiAction.BlockId,
                actionId = generatedUiAction.ActionId,
                prompt = generatedUiAction.Prompt,
                payload = generatedUiAction.Payload
            }, JsonOptions);

            return
                $"USER REQUEST: {request.UserMessage}\n" +
                $"GENERATE_UI: {request.GenerateUi.ToString().ToLowerInvariant()}\n" +
                $"GENERATED_UI_ACTION_JSON:\n{actionJson}";
        }

        if (request.GenerateUi)
        {
            return $"USER REQUEST: {request.UserMessage}\nGENERATE_UI: true\nGENERATED_UI_REQUIREMENT: uiToolCalls must include deterministic generated-ui tool(s).";
        }

        return $"USER REQUEST: {request.UserMessage}\nGENERATE_UI: false";
    }

    private async Task<string> RequestPlannerResponseAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (_chatClient is null)
        {
            return string.Empty;
        }

        var chatOptions = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            Temperature = 0.0f
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
        var responseText = response.Text?.Trim() ?? string.Empty;
        _logger?.LogDebug("LLM response: {Response}", responseText);
        return responseText;
    }

    private static bool RequiresGeneratedUiRepair(ActionPlanRequest request, ActionPlan plan)
    {
        if (!request.GenerateUi || request.GeneratedUiAction is not null)
        {
            return false;
        }

        if (plan.RequiresClarification || plan.UiToolCalls.Count > 0)
        {
            return false;
        }

        return true;
    }

    private async Task<ActionPlan?> TryRepairGeneratedUiPlanAsync(
        ActionPlanRequest request,
        string originalPlanJson,
        ActionPlan originalPlan,
        CancellationToken cancellationToken)
    {
        if (_chatClient is null)
        {
            return null;
        }

        var repairMessages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildGeneratedUiRepairSystemPrompt(request)),
            new(ChatRole.User, BuildGeneratedUiRepairUserPrompt(request, originalPlanJson))
        };

        var repairedResponse = await RequestPlannerResponseAsync(repairMessages, cancellationToken);
        var repairedPlan = ParsePlanResponse(repairedResponse, generateUi: true);

        if (repairedPlan.RequiresClarification || repairedPlan.UiToolCalls.Count == 0)
        {
            _logger?.LogWarning(
                "Generated UI repair did not produce uiToolCalls. Falling back to deterministic summary card.");
            return null;
        }

        if (repairedPlan.Steps.Count == 0 && originalPlan.Steps.Count > 0)
        {
            return repairedPlan with { Steps = originalPlan.Steps };
        }

        return repairedPlan;
    }

    private string BuildGeneratedUiRepairSystemPrompt(ActionPlanRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ROLE");
        sb.AppendLine("You repair deterministic planner JSON.");
        sb.AppendLine();
        sb.AppendLine("# HARD RULES");
        sb.AppendLine("- Return raw JSON only.");
        sb.AppendLine("- Keep existing valid steps unchanged.");
        sb.AppendLine("- uiToolCalls MUST include at least one valid generated-ui tool.");
        sb.AppendLine("- Use only generated-ui tools listed below.");
        sb.AppendLine("- Prefer generated-ui blocks that make next actions explicit (for example summary.card, chart.view, form.draft, or table.view).");
        sb.AppendLine("- If chart labels/series are uncertain, use chart.view with a valid dataSource.");
        sb.AppendLine("- Do not fabricate metrics.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.AgentInstructions))
        {
            sb.AppendLine("# AGENT-SPECIFIC INSTRUCTIONS");
            sb.AppendLine(request.AgentInstructions.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("# AVAILABLE GENERATED-UI TOOLS");
        foreach (var tool in _uiToolCatalog.GetTools())
        {
            sb.Append("- ").Append(tool.ToolId).Append(": ").AppendLine(tool.Description);
            sb.AppendLine("  inputSchema:");
            sb.Append("  ").AppendLine(tool.InputSchema.Replace("\n", "\n  ", StringComparison.Ordinal));
        }
        sb.AppendLine();
        sb.AppendLine("Return repaired JSON now.");
        return sb.ToString();
    }

    private static string BuildGeneratedUiRepairUserPrompt(ActionPlanRequest request, string originalPlanJson)
    {
        return
            $"USER REQUEST: {request.UserMessage}\n" +
            "GENERATE_UI: true\n" +
            "REPAIR_GOAL: preserve steps and add valid uiToolCalls.\n" +
            $"ORIGINAL_PLAN_JSON:\n{originalPlanJson}";
    }

    private static ActionPlan AttachFallbackGeneratedUiSummary(ActionPlan plan, string userMessage)
    {
        var summaryDescription = BuildFallbackSummaryDescription(plan, userMessage);
        var summaryToolCall = new AgentUiToolCall
        {
            ToolId = AgentUiToolIds.SummaryCard,
            Arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["blockId"] = "generated-summary",
                ["title"] = "Request Summary",
                ["description"] = summaryDescription
            }
        };

        return plan with
        {
            UiToolCalls = [summaryToolCall]
        };
    }

    private static string BuildFallbackSummaryDescription(ActionPlan plan, string userMessage)
    {
        if (plan.Steps.Count == 0)
        {
            return string.IsNullOrWhiteSpace(userMessage)
                ? "No executable actions were required."
                : $"Processed request: {userMessage}.";
        }

        var actionList = string.Join(
            ", ",
            plan.Steps.Take(4).Select(static step => $"{step.ComponentId}.{step.ActionId}"));
        var suffix = plan.Steps.Count > 4 ? ", ..." : string.Empty;
        var plural = plan.Steps.Count == 1 ? "action" : "actions";
        return $"Planned {plan.Steps.Count} {plural}: {actionList}{suffix}.";
    }

    private ActionPlan ParsePlanResponse(string responseText, bool generateUi)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger?.LogWarning("Empty response from planner");
            return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PlannerResponse>(responseText, JsonOptions);
            if (parsed is null)
            {
                _logger?.LogWarning("Failed to deserialize planner response");
                return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
            }

            if (!string.IsNullOrWhiteSpace(parsed.Reasoning))
            {
                _logger?.LogDebug("Planner reasoning: {Reasoning}", parsed.Reasoning);
            }

            var uiToolCalls = NormalizeUiToolCalls(parsed.UiToolCalls, generateUi);

            if (parsed.NeedsClarification)
            {
                return ActionPlan.NeedsClarification(
                    parsed.ClarificationQuestion ?? "Can you provide more details?",
                    uiToolCalls);
            }

            if (parsed.Steps is null || parsed.Steps.Count == 0)
            {
                return ActionPlan.Empty(uiToolCalls);
            }

            var steps = parsed.Steps
                .Where(step =>
                    !string.IsNullOrWhiteSpace(step.ComponentId) &&
                    !string.IsNullOrWhiteSpace(step.ActionId))
                .Select(step => new PlannedStep
                {
                    ComponentId = step.ComponentId!,
                    ActionId = step.ActionId!,
                    Arguments = step.Arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
                    TargetAgentId = step.TargetAgentId
                })
                .ToList();

            return new ActionPlan
            {
                Steps = steps,
                Confidence = parsed.Confidence ?? 1.0,
                UiToolCalls = uiToolCalls
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse planner response: {Response}", responseText);
            return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
        }
    }

    private IReadOnlyList<AgentUiToolCall> NormalizeUiToolCalls(
        IReadOnlyList<PlannerUiToolCall>? uiToolCalls,
        bool generateUi)
    {
        if (!generateUi || uiToolCalls is null || uiToolCalls.Count == 0)
        {
            return [];
        }

        var knownToolIds = _uiToolCatalog.GetTools()
            .Select(static tool => tool.ToolId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalized = new List<AgentUiToolCall>(uiToolCalls.Count);
        foreach (var call in uiToolCalls)
        {
            if (string.IsNullOrWhiteSpace(call.ToolId))
            {
                continue;
            }

            if (!knownToolIds.Contains(call.ToolId))
            {
                _logger?.LogWarning("Planner emitted unknown ui tool id '{ToolId}'. Ignoring.", call.ToolId);
                continue;
            }

            normalized.Add(new AgentUiToolCall
            {
                ToolId = call.ToolId!,
                Arguments = call.Arguments ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            });
        }

        return normalized;
    }

    private sealed class PlannerResponse
    {
        public string? Reasoning { get; set; }
        public List<PlannerStep>? Steps { get; set; }
        public bool NeedsClarification { get; set; }
        public string? ClarificationQuestion { get; set; }
        public double? Confidence { get; set; }
        public List<PlannerUiToolCall>? UiToolCalls { get; set; }
    }

    private sealed class PlannerStep
    {
        public string? ComponentId { get; set; }
        public string? ActionId { get; set; }
        public Dictionary<string, object?>? Arguments { get; set; }
        public string? TargetAgentId { get; set; }
    }

    private sealed class PlannerUiToolCall
    {
        public string? ToolId { get; set; }
        public Dictionary<string, object?>? Arguments { get; set; }
    }
}
