# Plan: Clarification System & UI Intelligence

**Date:** 2026-02-23  
**Reference:** CopilotKit (https://github.com/CopilotKit/CopilotKit) - Frontend for Agents & Generative UI  
**Goal:** Ensure EVERY user prompt is either interpreted OR asks for clarification; maximize agent's understanding of user's UI components and navigation options

---

## 1. Executive Summary

This plan addresses two interconnected needs:
1. **Guaranteed Response Coverage** - Every user prompt must result in either action OR clarification request (no silent failures)
2. **Deep UI Intelligence** - Agent must understand user's specific UI components, navigation structure, and context

**Key Principle:** All intelligence remains **internal** - no data spillage into customer projects. The demo represents a potential customer project.

---

## 2. Current State Analysis

### What's Already in Place

| Component | Status | Notes |
|-----------|--------|-------|
| `PendingClarification` | ✅ Implemented | In `AgentRuntime` and `ConversationManager` |
| `IntentResolver` | ✅ Implemented | Classifies intents (navigate, filter, sort, dialog, form, tab) |
| Component Capability Catalog | ✅ Implemented | `AgentComponentV1CapabilityProfile` with MudBlazor actions |
| Framework Tools | ✅ Implemented | Components exposed as `AIFunction` to agent |
| Approval Gating | ✅ Implemented | `RequiresApproval` flag on sensitive actions |
| StructuredActionPlanner | ⚠️ Partial | Has clarification logic but not enforced |

### Gap Analysis

1. **Clarification is optional, not mandatory** - Agent may attempt action without required parameters
2. **No UI structure awareness** - Agent knows WHAT actions exist but not HOW the specific page is laid out
3. **Navigation intelligence is minimal** - Only basic URI inference, no understanding of app structure
4. **No human-in-the-loop UI** - CopilotKit-style "interrupt" patterns not exposed to end user

---

## 3. Reference Architecture: CopilotKit

From CopilotKit research, key patterns to adopt:

### 3.1 Human-in-the-Loop (HITL) Patterns

```
┌─────────────────────────────────────────────────────────────┐
│                    Agent Execution Flow                      │
├─────────────────────────────────────────────────────────────┤
│  User Prompt                                                │
│       │                                                     │
│       ▼                                                     │
│  ┌─────────────┐    No    ┌────────────────────────┐      │
│  │  Can agent  │──────────►│  Ask clarifying question │      │
│  │  execute?   │           │  (HITL - pause & prompt) │      │
│  └─────────────┘           └───────────┬────────────┘      │
│       │ Yes                               │                 │
│       ▼                                   ▼                 │
│  ┌─────────────┐                   (User responds)          │
│  │ Execute &  │◄──────────────────────────────               │
│  │ Stream UI  │                                            │
│  └─────────────┘                                            │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 Key CopilotKit Features to Implement

1. **Agent Q&A** - Agent prompts user via chat or custom UI
2. **Shared State** - Agent and UI synchronize context
3. **Streaming Intermediary State** - Real-time progress updates
4. **Agent Steering** - User can correct/redirect agent mid-flow

---

## 4. Implementation Plan

### Phase 1: Guaranteed Clarification (Priority: Critical)

#### 1.1 Mandatory Clarification Prompt Engineering

**File:** `src/AgentBlazor.Core/Runtime/Agents/AgentRuntime.cs`

Replace existing instruction in `BuildInstructions()`:

```csharp
// CURRENT (line ~1141):
builder.AppendLine("If required action parameters are missing or ambiguous, ask a concise clarifying question before calling a tool.");

// NEW - STRONGER:
builder.AppendLine("CRITICAL: You MUST either execute a tool OR ask a clarifying question. NEVER return text without doing either.");
builder.AppendLine("If you cannot determine required parameters from context, ask: 'Which [parameter]?' (be specific).");
builder.AppendLine("Examples of mandatory clarification:");
builder.AppendLine("  - 'Which column should I filter by?' (not just 'filter the data')");
builder.AppendLine("  - 'What value should I search for in the [column] field?'");
builder.AppendLine("  - 'Which tab do you want to switch to?'");
builder.AppendLine("Do NOT guess parameters. Ask instead of guessing.");
```

#### 1.2 Fallback Clarification for Silent Failures

**File:** `src/AgentBlazor.Core/Runtime/Agents/AgentRuntime.cs` - After `RunAsync`

Add post-processing check:

```csharp
// After agent.RunAsync (around line 281)
if (plannedActions.IsEmpty && executionResults.IsEmpty && string.IsNullOrWhiteSpace(text))
{
    // Agent returned nothing actionable - force clarification
    return new AgentTurnResponse(
        AgentName: registration.Name,
        ResponseText: "I couldn't determine what you'd like me to do. Could you rephrase? " +
                      "For example: 'Filter suppliers by risk score > 70' or 'Navigate to the suppliers page'.",
        PlannedActions: [],
        ExecutionResults: [],
        RequiresClarification: true);
}
```

#### 1.3 Clarification Response Classification

**File:** `src/AgentBlazor.Core/Runtime/Agents/ResponseBuilder.cs`

Add explicit clarification detection in agent instructions:

```csharp
builder.AppendLine("When you need clarification, respond with ONLY this JSON structure:");
builder.AppendLine("{");
builder.AppendLine("  \"intent\": \"clarification\",");
builder.AppendLine("  \"question\": \"Your specific question here\",");
builder.AppendLine("  \"context\": \"What you understood so far\"");
builder.AppendLine("}");
```

---

### Phase 2: Deep UI Component Intelligence (Priority: High)

#### 2.1 Internal Component Tree Registry

Keep internal (not exposed to customer project):

```csharp
// INTERNAL - NOT in public API
namespace AgentBlazor.Core.Runtime.Internal;

public interface IInternalComponentTreeRegistry
{
    void RegisterPageStructure(string pageRoute, ComponentNode root);
    IReadOnlyList<ComponentNode> GetPageStructure(string pageRoute);
    IReadOnlyList<ComponentNode> GetCurrentPageComponents();
}

public class ComponentNode
{
    public string Id { get; set; }           // e.g., "suppliers-grid"
    public string Type { get; set; }          // e.g., "MudDataGrid"
    public string? ParentId { get; set; }
    public List<string> ChildIds { get; set; } = new();
    public Dictionary<string, string> Attributes { get; set; } = new();
    public List<ColumnDefinition> Columns { get; set; } = new();
    public List<string> AvailableActions { get; set; } = new();
}
```

#### 2.2 Automatic Page Structure Discovery

**Implementation:** Use JSInterop to capture component tree at render time:

```csharp
// AgentBlazor.Components/wwwroot/agentblazor.js
window.AgentBlazor.capturePageStructure = () => {
    // Walk DOM, identify MudBlazor components
    // Build component tree with IDs and types
    // Return JSON for internal registry
};
```

**Trigger:** Call on each page navigation/interaction

#### 2.3 Enhanced Instructions with Page Context

**File:** `AgentRuntime.cs` - Update `BuildInstructions()`

```csharp
private static string BuildInstructionsWithPageContext(...)
{
    // ... existing instructions ...
    
    // ADD: Current page structure
    var pageComponents = internalRegistry.GetCurrentPageComponents();
    if (pageComponents.Count > 0)
    {
        builder.AppendLine("\nCurrent page components:");
        foreach (var comp in pageComponents)
        {
            builder.AppendLine($"- {comp.Id} ({comp.Type})");
            if (comp.Columns.Count > 0)
            {
                builder.AppendLine($"  columns: {string.Join(", ", comp.Columns.Select(c => c.Name))}");
            }
        }
    }
    
    // ADD: Navigation routes known to app
    var routes = routeRegistry.GetAllRoutes();
    builder.AppendLine("\nAvailable routes:");
    foreach (var route in routes)
    {
        builder.AppendLine($"- {route.Path}: {route.Description}");
    }
}
```

---

### Phase 3: Navigation Intelligence (Priority: High)

#### 3.1 Internal Route Registry Enhancement

**File:** `src/AgentBlazor.Core/Runtime/Routing/InMemoryRouteRegistry.cs`

```csharp
public class RouteDefinition
{
    // ... existing ...
    
    // NEW: Enhanced metadata
    public string? Description { get; set; }
    public List<string> Keywords { get; set; } = new();  // for fuzzy matching
    public string? AssociatedComponentId { get; set; }   // which component on this page
    public Dictionary<string, string> PageMetadata { get; set; } = new();
}
```

#### 3.2 Route Discovery from App

**Implementation:** Automatically discover routes from Blazor router:

```csharp
// Internal service to scan for @page directives
public interface IInternalRouteDiscoveryService
{
    Task<IReadOnlyList<RouteDefinition>> DiscoverRoutesAsync();
}
```

**Note:** This runs AT RUNTIME in the DEMO, not in customer projects. Results stored internally.

---

### Phase 4: Human-in-the-Loop UI (Priority: Medium)

#### 4.1 Clarification UI Component

**File:** `src/AgentBlazor.Components/AgentClarificationPrompt.razor`

```razor
@if (Question is not null)
{
    <div class="agent-clarification">
        <MudCard>
            <MudCardContent>
                <MudText Typo="Typo-subtitle1">@Question</MudText>
                <MudTextField @bind-Value="UserResponse" 
                              Placeholder="Type your answer..."
                              Immediate="true"
                              OnKeyDown="HandleKeyDown" />
            </MudCardContent>
            <MudCardActions>
                <MudButton Variant="Variant.Filled" 
                           Color="Color.Primary"
                           OnClick="SubmitResponse">
                    Submit
                </MudButton>
            </MudCardActions>
        </MudCard>
    </div>
}

@code {
    [Parameter] public string? Question { get; set; }
    [Parameter] public EventCallback<string> Response { get; set; }
    
    private string UserResponse = string.Empty;
    
    private async Task SubmitResponse()
    {
        await Response.InvokeAsync(UserResponse);
        UserResponse = string.Empty;
    }
}
```

#### 4.2 Integration with Chat Widget

**File:** `src/AgentBlazor.Components/AgentChatWidget.razor`

Add clarification handling:

```razor
@if (CurrentClarification is { } clar)
{
    <AgentClarificationPrompt Question="@clar.Question" 
                              Response="OnClarificationResponse" />
}

@code {
    private PendingClarification? CurrentClarification;
    
    private async Task SendMessage(string message)
    {
        var response = await _runtime.RunTurnAsync(new AgentTurnRequest(message));
        
        if (response.RequiresClarification)
        {
            CurrentClarification = new PendingClarification
            {
                Question = response.ClarificationQuestion,
                Context = response.ResponseText
            };
        }
        else
        {
            // Normal message handling
            Messages.Add(new ChatMessage { ... });
        }
    }
    
    private async Task OnClarificationResponse(string response)
    {
        // Append to original message and re-run
        var original = PendingClarification.Context + " " + response;
        await SendMessage(original);
        CurrentClarification = null;
    }
}
```

---

## 5. Internal-Only Architecture

### 5.1 Separation of Concerns

```
┌─────────────────────────────────────────────────────────────┐
│                    CUSTOMER PROJECT                          │
│  (AgentBlazor.Demo - or any customer Blazor app)           │
│                                                             │
│  - Uses AgentBlazor packages                                │
│  - Has its own components, pages, data                     │
│  - NO access to internal intelligence systems              │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ Reference
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                   AgentBlazor Packages                       │
│                                                             │
│  Public API:                                                │
│  - AddAgentBlazorServices()                                │
│  - AgentChatWidget, AgentDataGrid, AgentDialog             │
│  - IAgentRuntime                                           │
│                                                             │
│  INTERNAL (not exposed):                                    │
│  - IInternalComponentTreeRegistry                         │
│  - IInternalRouteDiscoveryService                         │
│  - Page structure capture                                  │
│  - Enhanced instructions with context                     │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 How Demo "Learns" (Internal)

The demo project registers its own structure:

```csharp
// In demo's Program.cs - internal setup
builder.Services.AddSingleton<IInternalComponentTreeRegistry, 
    DemoComponentTreeRegistry>();

// Demo registers its own structure (not customer-facing)
services.GetRequiredService<IInternalComponentTreeRegistry>()
    .RegisterPageStructure("/suppliers", new ComponentNode
    {
        Id = "suppliers-grid",
        Type = "MudDataGrid",
        Columns = new List<ColumnDefinition>
        {
            new() { Name = "Name", Type = typeof(string) },
            new() { Name = "RiskScore", Type = typeof(int) },
            // ...
        }
    });
```

**Customer projects don't need this** - they get intelligent defaults from the library.

---

## 6. Key Files to Modify

| File | Change | Priority |
|------|--------|----------|
| `AgentRuntime.cs` | Enhance instructions, add fallback clarification | Critical |
| `ResponseBuilder.cs` | Add clarification JSON format to instructions | High |
| `AgentChatWidget.razor` | Add clarification UI handling | High |
| `InMemoryRouteRegistry.cs` | Add metadata (keywords, description) | High |
| New: `AgentClarificationPrompt.razor` | Clarification dialog component | Medium |
| New: `IInternalComponentTreeRegistry.cs` | Internal component tree (internal) | High |
| New: `InternalPageCapture.js` | JSInterop for DOM walking | Medium |

---

## 7. Success Criteria

### Must Have (v1)
- [ ] Agent NEVER returns "I couldn't do that" without asking for clarification first
- [ ] Agent can identify missing parameters and ask specific questions
- [ ] Clarification UI renders in chat widget when agent requests it
- [ ] Agent understands demo's page structure (columns, components)

### Should Have (v2)
- [ ] Automatic route discovery from Blazor app
- [ ] Component tree capture at runtime
- [ ] Shared state between agent and UI (CopilotKit-style)

### Nice to Have (v3)
- [ ] Agent steering - user can correct agent mid-flow
- [ ] Full CopilotKit feature parity

---

## 8. References

- CopilotKit: https://github.com/CopilotKit/CopilotKit
- CopilotKit HITL Docs: https://docs.copilotkit.ai/human-in-the-loop
- AG-UI Protocol: https://github.com/ag-ui-protocol/ag-ui
- Microsoft Agent Framework: https://learn.microsoft.com/en-us/azure/ai-services/agents/

---

*End of Plan*
