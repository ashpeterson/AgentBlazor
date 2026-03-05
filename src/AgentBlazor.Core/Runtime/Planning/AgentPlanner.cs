using System.Text;
using System.Text.Json;
using AgentBlazor.Core.Components;
using AgentBlazor.Core.Runtime.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// Simplified LLM planner. Single prompt, no repair loop.
/// The model returns a structured JSON response containing a natural-language message,
/// component actions, and optional UI blocks — all in one call.
/// </summary>
internal sealed class AgentPlanner : IStructuredActionPlanner
{
    private readonly IChatClient? _chatClient;
    private readonly ILogger<AgentPlanner>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public bool IsProviderConfigured => _chatClient is not null;

    public AgentPlanner(
        IChatClient? chatClient = null,
        ILogger<AgentPlanner>? logger = null)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<ActionPlan> PlanAsync(
        ActionPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            return ActionPlan.NeedsClarification(
                "No AI provider is configured. Register an AgentBlazor chat client.");
        }

        var systemPrompt = BuildSystemPrompt(request);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt)
        };

        foreach (var turn in request.ConversationHistory.TakeLast(10))
        {
            var role = turn.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, turn.Content));
        }

        messages.Add(new ChatMessage(ChatRole.User, BuildUserPrompt(request)));

        _logger?.LogDebug("AgentPlanner prompt: {Length} chars", systemPrompt.Length);

        var response = await _chatClient.GetResponseAsync(
            messages,
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0.0f },
            cancellationToken);

        var responseText = response.Text?.Trim() ?? string.Empty;
        _logger?.LogDebug("AgentPlanner response: {Response}", responseText);

        // Extract reasoning content if present (thinking models)
        var reasoningContent = ExtractReasoningContent(response);

        var plan = ParseResponse(responseText, request.GenerateUi);
        return plan with
        {
            SystemPrompt = systemPrompt,
            RawResponse = responseText,
            ReasoningContent = reasoningContent
        };
    }

    // -------------------------------------------------------------------------
    // Prompt building
    // -------------------------------------------------------------------------

    private static string BuildSystemPrompt(ActionPlanRequest request)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# ROLE");
        sb.AppendLine("You are an AI assistant embedded in a Blazor web application.");
        sb.AppendLine("You control UI components and respond helpfully to the user.");
        sb.AppendLine("You must return only valid JSON — no markdown fences, no extra text.");
        sb.AppendLine();

        sb.AppendLine("# OUTPUT SCHEMA");
        sb.AppendLine("""
{
  "message": "Natural language reply shown to the user (always required)",
  "actions": [
    { "agentId": "component-instance-id", "action": "action_name", "args": {} }
  ],
  "ui": [
    { "type": "summary.card", "title": "...", "description": "..." }
  ],
  "needsClarification": false,
  "clarificationQuestion": null
}
""");
        sb.AppendLine();

        sb.AppendLine("# RULES");
        sb.AppendLine("- `message` is always required — it is shown directly to the user.");
        sb.AppendLine("- `actions` executes component operations. Use only agentIds and action names listed in ACTIVE COMPONENTS.");
        sb.AppendLine("- `ui` is optional — include when it adds value (charts, tables, summaries, forms).");
        sb.AppendLine("- Only set needsClarification=true if CRITICAL information is truly missing and cannot be inferred.");
        sb.AppendLine("- If the user provides ANY data values, use them — do not ask for clarification.");
        sb.AppendLine("- Do not invent agentIds, action names, or routes not listed below.");
        sb.AppendLine("- Include all required parameters for each action.");
        sb.AppendLine("- Treat SHARED STATE as the canonical app/session context.");
        sb.AppendLine();
        sb.AppendLine("# ACTION TARGETING RULES");
        sb.AppendLine("- CRITICAL: Use ONLY the action names listed in ACTIVE COMPONENTS below.");
        sb.AppendLine("- Each action can ONLY be called on the agentId that lists it.");
        sb.AppendLine("- For forms, look for fill_* actions (e.g., 'fill_supplier_onboarding') that accept all fields at once.");
        sb.AppendLine("- Use EXACT parameter names from ACTIVE COMPONENTS.");
        sb.AppendLine("- Do NOT call form submit actions unless the user explicitly asks to submit/save/confirm/send.");
        sb.AppendLine();
        sb.AppendLine("# FORM FILLING");
        sb.AppendLine("- When the user provides data values, fill them in without asking for clarification.");
        sb.AppendLine("- Use compound fill/set/update actions even for partial edits (single-field updates are valid).");
        sb.AppendLine("- Do not ask for all fields when the user asks to change only one field.");
        sb.AppendLine("- Check the ACTIVE COMPONENTS section for available fill_* actions and their parameters.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.AgentInstructions))
        {
            sb.AppendLine("# INSTRUCTIONS");
            sb.AppendLine(request.AgentInstructions.Trim());
            sb.AppendLine();
        }

        BuildActiveComponentsSection(sb, request);
        BuildSharedStateSection(sb, request);
        BuildServiceToolsSection(sb, request);

        if (request.AvailableRoutes.Count > 0)
        {
            sb.AppendLine("# AVAILABLE ROUTES");
            foreach (var route in request.AvailableRoutes)
            {
                sb.Append("- ").Append(route.Path);
                if (!string.IsNullOrWhiteSpace(route.Description))
                    sb.Append(": ").Append(route.Description);
                if (route.Aliases.Count > 0)
                    sb.Append(" (keywords: ").Append(string.Join(", ", route.Aliases.Take(5))).Append(')');
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (request.GenerateUi)
        {
            sb.AppendLine("# OPTIONAL UI BLOCKS");
            sb.AppendLine("Include in the `ui` array when helpful:");
            sb.AppendLine("- summary.card  : { type, title, description, actions[{id, label, prompt}] }");
            sb.AppendLine("- form.draft    : { type, title, description, fields[{name,label,type,value,required}], actions[{id,label,prompt}] }");
            sb.AppendLine("- table.view    : { type, title, columns[{key,header}], rows[{...}], actions[{id,label,prompt}] }");
            sb.AppendLine("- chart.view    : { type, title, dataSource }  OR  { type, title, chartType, labels, series[{name,data[]}] }");
            sb.AppendLine("  chartType values: line | bar | pie");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.CurrentRoute))
        {
            sb.Append("# CURRENT ROUTE: ").AppendLine(request.CurrentRoute);
            sb.AppendLine();
        }

        sb.AppendLine("Return JSON now.");
        return sb.ToString();
    }

    private static void BuildSharedStateSection(StringBuilder sb, ActionPlanRequest request)
    {
        if (request.SharedState.Count == 0)
            return;

        sb.AppendLine("# SHARED STATE");
        sb.AppendLine("Use this synchronized state when deciding what to do next.");
        foreach (var (key, value) in request.SharedState
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("  ").Append(key).Append(": ").AppendLine(value);
        }

        sb.AppendLine();
    }

    private static void BuildActiveComponentsSection(StringBuilder sb, ActionPlanRequest request)
    {
        if (request.MountedComponents.Count == 0)
            return;

        sb.AppendLine("# ACTIVE COMPONENTS");

        foreach (var mounted in request.MountedComponents)
        {
            sb.Append("AgentId: ").Append(mounted.AgentId)
              .Append(" (").Append(mounted.ComponentType).AppendLine(")");

            if (mounted.Actions is { Count: > 0 })
            {
                sb.AppendLine("  Actions:");
                foreach (var action in mounted.Actions)
                {
                    sb.Append("    - ").Append(action.ActionId);
                    if (action.RequiresApproval) sb.Append(" [requires-approval]");
                    if (!string.IsNullOrWhiteSpace(action.Description))
                        sb.Append(": ").Append(action.Description);
                    sb.AppendLine();

                    foreach (var p in action.Parameters)
                    {
                        sb.Append("        ").Append(p.Name).Append(": ").Append(p.Type);
                        if (p.Required) sb.Append(" [required]");
                        if (p.AllowedValues is { Count: > 0 })
                            sb.Append(" [allowed: ").Append(string.Join("|", p.AllowedValues)).Append(']');
                        if (!string.IsNullOrWhiteSpace(p.Description))
                            sb.Append(" — ").Append(p.Description);
                        sb.AppendLine();
                    }
                }
            }

            if (mounted.State.Count > 0)
            {
                sb.AppendLine("  State:");
                foreach (var kv in mounted.State)
                    sb.Append("    ").Append(kv.Key).Append(": ").AppendLine(kv.Value);
            }

            sb.AppendLine();
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Attempts to extract reasoning/thinking content from the model response.
    /// Handles providers that surface reasoning via content items or response metadata.
    /// </summary>
    private static string? ExtractReasoningContent(ChatResponse response)
    {
        if (response.Messages is null) return null;

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is not TextContent tc) continue;

                // Check for JSON-wrapped "thinking" content from providers like Anthropic
                if (tc.RawRepresentation is System.Text.Json.JsonElement je &&
                    je.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() is "thinking" or "reasoning" &&
                    je.TryGetProperty("thinking", out var thinkEl))
                {
                    return thinkEl.GetString();
                }
            }
        }

        return null;
    }

    private static void BuildServiceToolsSection(StringBuilder sb, ActionPlanRequest request)
    {
        if (request.ServiceTools.Count == 0)
            return;

        sb.AppendLine("# AVAILABLE TOOLS");
        sb.AppendLine("These are server-side tools you can call alongside or instead of component actions.");
        sb.AppendLine("Use agentId: \"tool\" and action: \"<tool_name>\" in the actions array.");
        sb.AppendLine();

        foreach (var tool in request.ServiceTools)
        {
            sb.Append("- ").Append(tool.Name);
            if (!string.IsNullOrWhiteSpace(tool.Description))
                sb.Append(": ").Append(tool.Description);
            sb.AppendLine();

            foreach (var param in tool.Parameters)
            {
                sb.Append("    ").Append(param.Name).Append(" (").Append(param.Type).Append(')');
                if (param.Required) sb.Append(" [required]");
                if (!string.IsNullOrWhiteSpace(param.Description))
                    sb.Append(" — ").Append(param.Description);
                sb.AppendLine();
            }
        }

        sb.AppendLine();
    }

    private static string BuildUserPrompt(ActionPlanRequest request)
    {
        if (request.GeneratedUiAction is not null)
        {
            var actionJson = JsonSerializer.Serialize(new
            {
                blockId = request.GeneratedUiAction.BlockId,
                actionId = request.GeneratedUiAction.ActionId,
                prompt = request.GeneratedUiAction.Prompt,
                payload = request.GeneratedUiAction.Payload
            });
            return $"USER REQUEST: {request.UserMessage}\nGENERATED_UI_ACTION: {actionJson}";
        }

        return $"USER REQUEST: {request.UserMessage}";
    }

    // -------------------------------------------------------------------------
    // Response parsing
    // -------------------------------------------------------------------------

    private ActionPlan ParseResponse(string responseText, bool generateUi)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger?.LogWarning("Empty response from AgentPlanner");
            return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<PlannerResponse>(responseText, JsonOptions);
            if (parsed is null)
            {
                _logger?.LogWarning("Failed to deserialize AgentPlanner response");
                return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
            }

            var uiToolCalls = generateUi ? BuildUiToolCalls(parsed.Ui) : [];

            if (parsed.NeedsClarification)
            {
                return ActionPlan.NeedsClarification(
                    parsed.ClarificationQuestion ?? "Can you provide more details?",
                    uiToolCalls) with { Message = parsed.Message };
            }

            var steps = new List<PlannedStep>();
            if (parsed.Actions is { Count: > 0 })
            {
                foreach (var action in parsed.Actions)
                {
                    if (string.IsNullOrWhiteSpace(action.AgentId) || string.IsNullOrWhiteSpace(action.Action))
                        continue;

                    steps.Add(new PlannedStep
                    {
                        ComponentId = action.AgentId,
                        ActionId = action.Action,
                        Arguments = NormalizeArgs(action.Args),
                        TargetAgentId = action.AgentId
                    });
                }
            }

            return new ActionPlan
            {
                Steps = steps,
                UiToolCalls = uiToolCalls,
                Message = parsed.Message
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse AgentPlanner response: {Response}", responseText);
            return ActionPlan.NeedsClarification("I couldn't understand that request. Can you rephrase?");
        }
    }

    private static IReadOnlyList<AgentUiToolCall> BuildUiToolCalls(IReadOnlyList<PlannerUiBlock>? blocks)
    {
        if (blocks is null || blocks.Count == 0) return [];

        var result = new List<AgentUiToolCall>(blocks.Count);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Type)) continue;

            var toolId = block.Type.Trim().ToLowerInvariant() switch
            {
                "summary.card" or "summary_card" => AgentUiToolIds.SummaryCard,
                "form.draft" or "form_draft"     => AgentUiToolIds.FormDraft,
                "table.view" or "table_view"     => AgentUiToolIds.TableView,
                "chart.view" or "chart_view"     => AgentUiToolIds.ChartView,
                "action.confirmation"            => AgentUiToolIds.ActionConfirmation,
                _ => null
            };

            if (toolId is null) continue;

            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(block.Title))       args["title"] = block.Title;
            if (!string.IsNullOrWhiteSpace(block.Description)) args["description"] = block.Description;
            if (!string.IsNullOrWhiteSpace(block.BlockId))     args["blockId"] = block.BlockId;
            if (!string.IsNullOrWhiteSpace(block.DataSource))  args["dataSource"] = block.DataSource;
            if (!string.IsNullOrWhiteSpace(block.ChartType))   args["chartType"] = block.ChartType;
            if (block.Labels          is not null) args["labels"] = block.Labels;
            if (block.Series          is not null) args["series"] = block.Series;
            if (block.Columns         is not null) args["columns"] = block.Columns;
            if (block.Rows            is not null) args["rows"] = block.Rows;
            if (block.Fields          is not null) args["fields"] = block.Fields;
            if (block.Actions         is not null) args["actions"] = block.Actions;
            if (block.DataArguments   is not null) args["dataArguments"] = block.DataArguments;

            result.Add(new AgentUiToolCall { ToolId = toolId, Arguments = args });
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Argument normalization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a raw args dictionary (whose values may be JsonElement from deserialization)
    /// to a dictionary with native CLR types so callers get int, string, bool, etc.
    /// Also normalizes common parameter name variations.
    /// </summary>
    private static Dictionary<string, object?> NormalizeArgs(Dictionary<string, object?>? args)
    {
        if (args is null) return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, object?>(args.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in args)
        {
            var normalizedKey = NormalizeParameterName(key);
            result[normalizedKey] = value is JsonElement je ? UnwrapJsonElement(je) : value;
        }
        return result;
    }

    /// <summary>
    /// Normalizes common parameter name variations that LLMs might use.
    /// </summary>
    private static string NormalizeParameterName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "fieldname" => "field",
            "columnname" => "column",
            "rowindex" => "rowKey",
            "tabindex" => "index",
            "sortdirection" => "direction",
            "filtervalue" => "value",
            "filteroperator" => "operator",
            _ => name
        };
    }

    private static object? UnwrapJsonElement(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Number => je.TryGetInt32(out var i) ? i
            : je.TryGetInt64(out var l) ? l
            : (object?)je.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => je.ToString()
    };

    // -------------------------------------------------------------------------
    // Private DTOs for JSON deserialization
    // -------------------------------------------------------------------------

    private sealed class PlannerResponse
    {
        public string? Message { get; set; }
        public List<PlannerAction>? Actions { get; set; }
        public List<PlannerUiBlock>? Ui { get; set; }
        public bool NeedsClarification { get; set; }
        public string? ClarificationQuestion { get; set; }
    }

    private sealed class PlannerAction
    {
        public string? AgentId { get; set; }
        public string? Action { get; set; }
        public Dictionary<string, object?>? Args { get; set; }
    }

    private sealed class PlannerUiBlock
    {
        public string? Type { get; set; }
        public string? BlockId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? DataSource { get; set; }
        public string? ChartType { get; set; }
        public Dictionary<string, object?>? DataArguments { get; set; }
        public List<object?>? Labels { get; set; }
        public List<object?>? Series { get; set; }
        public List<object?>? Columns { get; set; }
        public List<object?>? Rows { get; set; }
        public List<object?>? Fields { get; set; }
        public List<object?>? Actions { get; set; }
    }
}
