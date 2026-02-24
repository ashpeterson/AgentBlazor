using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentBlazor.Core.Runtime.Planning;

/// <summary>
/// LLM-based action planner that produces structured JSON plans using research-backed
/// prompting techniques: few-shot examples, chain-of-thought reasoning, and strict schema enforcement.
///
/// Based on findings from:
/// - CAAP (Context-Aware Action Planning): Few-shot + CoT achieves 94.4% accuracy
/// - AG-UI Protocol: Typed state + event-based actions
/// - Microsoft.Extensions.AI: Native JSON schema enforcement
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

        _logger?.LogDebug("System prompt length: {Length} chars", systemPrompt.Length);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        // Add conversation history if present (last 10 turns for context)
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
                Temperature = 0.0f // Zero temperature for maximum determinism
            },
            cancellationToken);

        var responseText = response.Text?.Trim() ?? string.Empty;

        _logger?.LogDebug("LLM response: {Response}", responseText);

        return ParsePlanResponse(responseText);
    }

    private static string BuildSystemPrompt(ActionPlanRequest request)
    {
        var sb = new StringBuilder();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 1: ROLE AND TASK DEFINITION
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# ROLE");
        sb.AppendLine("You are a UI Action Planner. Your task is to convert natural language requests into structured JSON action plans that control UI components.");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 2: OUTPUT SCHEMA (upfront for clarity)
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# OUTPUT SCHEMA");
        sb.AppendLine("You MUST output valid JSON matching this exact schema:");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""Brief explanation of your interpretation and plan"",
  ""steps"": [
    {
      ""componentId"": ""string - must match an AVAILABLE COMPONENT"",
      ""actionId"": ""string - must match an action for that component"",
      ""arguments"": { ""paramName"": ""value"" },
      ""targetAgentId"": ""string (optional) - specific component instance""
    }
  ],
  ""needsClarification"": false,
  ""clarificationQuestion"": ""string (only if needsClarification is true)""
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 3: CRITICAL RULES
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# CRITICAL RULES");
        sb.AppendLine();
        sb.AppendLine("## Rule 1: OUTPUT FORMAT");
        sb.AppendLine("- Output ONLY valid JSON. No markdown fences, no explanatory text outside JSON.");
        sb.AppendLine("- The \"reasoning\" field goes INSIDE the JSON, not outside.");
        sb.AppendLine();

        sb.AppendLine("## Rule 2: COMPONENT VALIDATION");
        sb.AppendLine("- Only use componentId values from AVAILABLE COMPONENTS below.");
        sb.AppendLine("- Only use actionId values listed for that specific component.");
        sb.AppendLine("- Case matters: use exact casing as shown.");
        sb.AppendLine();

        sb.AppendLine("## Rule 3: PARAMETER HANDLING");
        sb.AppendLine("- All REQUIRED parameters must be provided with non-empty values.");
        sb.AppendLine("- If a required parameter cannot be determined from the request, set needsClarification=true.");
        sb.AppendLine("- Do NOT guess, infer, or make up parameter values that aren't in the request.");
        sb.AppendLine("- For 'column' parameters, use exact column names from MOUNTED COMPONENT STATE if available.");
        sb.AppendLine();

        sb.AppendLine("## Rule 4: NAVIGATION LOGIC (CRITICAL)");
        sb.AppendLine("- Check MOUNTED COMPONENTS to see what's on the current page.");
        sb.AppendLine("- If the target component type is NOT mounted, you MUST navigate first:");
        sb.AppendLine("  1. First step: AgentNavMenu.navigate_to with uri from AVAILABLE ROUTES");
        sb.AppendLine("  2. Second step: The actual component action");
        sb.AppendLine("- If target component IS mounted, do NOT add navigation.");
        sb.AppendLine();

        sb.AppendLine("## Rule 5: SEMANTIC INTERPRETATION");
        sb.AppendLine("- Map natural language to filter operators:");
        sb.AppendLine("  - \"high\", \"above\", \"more than\", \"greater\" → operator: \"gte\" or \"gt\"");
        sb.AppendLine("  - \"low\", \"below\", \"less than\", \"under\" → operator: \"lte\" or \"lt\"");
        sb.AppendLine("  - \"is\", \"equals\", \"exactly\" → operator: \"eq\"");
        sb.AppendLine("  - \"contains\", \"includes\", \"has\" → operator: \"contains\"");
        sb.AppendLine("  - \"starts with\", \"begins with\" → operator: \"startswith\"");
        sb.AppendLine("  - \"empty\", \"blank\", \"missing\" → operator: \"isnull\"");
        sb.AppendLine("- For subjective terms (\"high risk\", \"low priority\"), pass the semantic value and let the app interpret it.");
        sb.AppendLine();

        sb.AppendLine("## Rule 6: CLARIFICATION");
        sb.AppendLine("- If the request is ambiguous, vague, or missing critical information, ask for clarification.");
        sb.AppendLine("- Good clarification questions are specific: \"Which column should I filter?\" not \"What do you want?\"");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 4: FEW-SHOT EXAMPLES
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# EXAMPLES");
        sb.AppendLine();

        // Example 1: Simple filter on mounted component
        sb.AppendLine("## Example 1: Filter on mounted DataGrid");
        sb.AppendLine("User: \"Show suppliers with high risk\"");
        sb.AppendLine("Context: DataGrid is mounted with columns [Name, Risk, Status]");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""User wants to filter suppliers by risk level. DataGrid is mounted. Using 'gte' for 'high' semantic value."",
  ""steps"": [
    {
      ""componentId"": ""AgentDataGrid"",
      ""actionId"": ""filter"",
      ""arguments"": { ""column"": ""Risk"", ""operator"": ""gte"", ""value"": ""high"" }
    }
  ],
  ""needsClarification"": false
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // Example 2: Navigation required
        sb.AppendLine("## Example 2: Action on unmounted component (navigation required)");
        sb.AppendLine("User: \"Go to suppliers and filter by name Smith\"");
        sb.AppendLine("Context: NO DataGrid mounted, /suppliers route available");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""User wants to see suppliers filtered by name. No DataGrid mounted, so navigate first."",
  ""steps"": [
    {
      ""componentId"": ""AgentNavMenu"",
      ""actionId"": ""navigate_to"",
      ""arguments"": { ""uri"": ""/suppliers"" }
    },
    {
      ""componentId"": ""AgentDataGrid"",
      ""actionId"": ""filter"",
      ""arguments"": { ""column"": ""Name"", ""operator"": ""contains"", ""value"": ""Smith"" }
    }
  ],
  ""needsClarification"": false
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // Example 3: Multiple actions
        sb.AppendLine("## Example 3: Multiple related actions");
        sb.AppendLine("User: \"Sort by date descending and go to page 2\"");
        sb.AppendLine("Context: DataGrid mounted with columns [Date, Name, Amount]");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""Two actions: sort and pagination. Both on mounted DataGrid."",
  ""steps"": [
    {
      ""componentId"": ""AgentDataGrid"",
      ""actionId"": ""sort"",
      ""arguments"": { ""column"": ""Date"", ""direction"": ""desc"" }
    },
    {
      ""componentId"": ""AgentDataGrid"",
      ""actionId"": ""go_to_page"",
      ""arguments"": { ""pageIndex"": 1 }
    }
  ],
  ""needsClarification"": false
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // Example 4: Clarification needed
        sb.AppendLine("## Example 4: Clarification needed");
        sb.AppendLine("User: \"Filter the data\"");
        sb.AppendLine("Context: DataGrid mounted with columns [Name, Date, Status, Amount]");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""User wants to filter but didn't specify column, operator, or value."",
  ""steps"": [],
  ""needsClarification"": true,
  ""clarificationQuestion"": ""Which column would you like to filter, and what value should I filter for?""
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // Example 5: Form field
        sb.AppendLine("## Example 5: Form field interaction");
        sb.AppendLine("User: \"Set the customer name to John Doe\"");
        sb.AppendLine("Context: Form mounted with fields [customerName, email, phone]");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""User wants to set a form field. Form is mounted, field name maps to customerName."",
  ""steps"": [
    {
      ""componentId"": ""AgentForm"",
      ""actionId"": ""set_field"",
      ""arguments"": { ""field"": ""customerName"", ""value"": ""John Doe"" }
    }
  ],
  ""needsClarification"": false
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // Example 6: Tab switching
        sb.AppendLine("## Example 6: Tab switching");
        sb.AppendLine("User: \"Go to the second tab\"");
        sb.AppendLine("Context: Tabs component mounted with 3 tabs");
        sb.AppendLine("```json");
        sb.AppendLine(@"{
  ""reasoning"": ""User wants second tab. Tabs are 0-indexed, so second tab is index 1."",
  ""steps"": [
    {
      ""componentId"": ""AgentTabs"",
      ""actionId"": ""switch_tab"",
      ""arguments"": { ""index"": 1 }
    }
  ],
  ""needsClarification"": false
}");
        sb.AppendLine("```");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 5: COMMON MISTAKES TO AVOID
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# COMMON MISTAKES TO AVOID");
        sb.AppendLine("❌ Adding navigation when component is already mounted");
        sb.AppendLine("❌ Using 'asc' or 'desc' as filter operators (those are for sort)");
        sb.AppendLine("❌ Guessing column names not in the component state");
        sb.AppendLine("❌ Using page number 1 for first page (pages are 0-indexed)");
        sb.AppendLine("❌ Leaving required parameters empty");
        sb.AppendLine("❌ Adding markdown code fences to output");
        sb.AppendLine("❌ Writing explanation text outside the JSON");
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 6: AVAILABLE COMPONENTS (from request)
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# AVAILABLE COMPONENTS");
        sb.AppendLine();

        foreach (var component in request.AvailableComponents)
        {
            sb.Append("## ").AppendLine(component.ComponentId);
            sb.AppendLine(component.Description);
            sb.AppendLine();
            sb.AppendLine("Actions:");

            foreach (var action in component.Actions)
            {
                sb.Append("### ").Append(action.ActionId);
                if (action.RequiresApproval)
                    sb.Append(" ⚠️ REQUIRES APPROVAL");
                sb.AppendLine();
                sb.AppendLine(action.Description);

                if (action.Parameters.Count > 0)
                {
                    sb.AppendLine("Parameters:");
                    foreach (var param in action.Parameters)
                    {
                        var required = param.Required ? "REQUIRED" : "optional";
                        sb.Append("  - ").Append(param.Name).Append(" (").Append(param.Type).Append(", ").Append(required).Append(')');
                        if (!string.IsNullOrWhiteSpace(param.Description))
                            sb.Append(": ").Append(param.Description);
                        if (param.AllowedValues?.Count > 0)
                        {
                            sb.Append(" [allowed: ").Append(string.Join(", ", param.AllowedValues)).Append(']');
                        }
                        sb.AppendLine();
                    }
                }
                sb.AppendLine();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 7: CURRENT UI STATE (mounted components)
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# CURRENT UI STATE");
        sb.AppendLine();

        if (request.MountedComponents.Count > 0)
        {
            sb.AppendLine("## MOUNTED COMPONENTS (on current page):");
            foreach (var mounted in request.MountedComponents)
            {
                sb.Append("- **").Append(mounted.AgentId).Append("** (type: ").Append(mounted.ComponentType).Append(')');
                sb.AppendLine();
                if (mounted.State.Count > 0)
                {
                    sb.AppendLine("  State:");
                    foreach (var kv in mounted.State)
                    {
                        sb.Append("    - ").Append(kv.Key).Append(": ").AppendLine(kv.Value);
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("✓ Actions targeting these component TYPES do NOT need navigation.");
        }
        else
        {
            sb.AppendLine("## MOUNTED COMPONENTS: NONE");
            sb.AppendLine("⚠️ No agent components on current page. Most actions will require navigation first.");
        }
        sb.AppendLine();

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 8: AVAILABLE ROUTES
        // ═══════════════════════════════════════════════════════════════════
        if (request.AvailableRoutes.Count > 0)
        {
            sb.AppendLine("# AVAILABLE ROUTES");
            sb.AppendLine("Use these for AgentNavMenu.navigate_to:");
            sb.AppendLine();
            foreach (var r in request.AvailableRoutes)
            {
                sb.Append("- **").Append(r.Path).Append("**");
                if (!string.IsNullOrWhiteSpace(r.Description))
                    sb.Append(" — ").Append(r.Description);
                if (r.Aliases.Count > 0)
                    sb.Append(" (keywords: ").Append(string.Join(", ", r.Aliases.Take(5))).Append(')');
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECTION 9: CHAIN-OF-THOUGHT INSTRUCTIONS
        // ═══════════════════════════════════════════════════════════════════
        sb.AppendLine("# REASONING PROCESS");
        sb.AppendLine("Before outputting JSON, think through these steps (put reasoning in the \"reasoning\" field):");
        sb.AppendLine("1. What is the user trying to accomplish?");
        sb.AppendLine("2. Which component(s) and action(s) are needed?");
        sb.AppendLine("3. Is the target component mounted? If not, add navigation step first.");
        sb.AppendLine("4. What parameters are needed? Can I get them from the request or mounted state?");
        sb.AppendLine("5. Are any required parameters missing? If so, ask for clarification.");
        sb.AppendLine();
        sb.AppendLine("Now output your JSON plan.");

        return sb.ToString();
    }

    private static string BuildUserPrompt(ActionPlanRequest request)
    {
        return $"USER REQUEST: {request.UserMessage}";
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

            // Log reasoning for diagnostics
            if (!string.IsNullOrWhiteSpace(parsed.Reasoning))
            {
                _logger?.LogDebug("Planner reasoning: {Reasoning}", parsed.Reasoning);
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

        // Handle ```json or ``` at start
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine > 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..];
            }
        }

        // Handle ``` at end
        if (trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }

    // Internal DTOs for JSON deserialization
    private sealed class PlannerResponse
    {
        public string? Reasoning { get; set; }
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
