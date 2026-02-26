# AgentBlazor - How It All Works

A beginner-friendly guide to understanding the AgentBlazor architecture.

---

## Quick Overview

**AgentBlazor** lets you build Blazor apps where an AI agent can understand natural language and control your UI components. For example:

- User says: *"Sort the suppliers by risk level"*
- Agent understands this, finds the data grid, and sorts it

---

## Project Structure

```
agentblazor/
├── src/
│   ├── AgentBlazor.Core/           # The brain - runtime, planning, execution
│   ├── AgentBlazor.Components/     # UI components (chat, grids, forms)
│   ├── AgentBlazor.DefaultAgent/   # Default agent configuration
│   ├── AgentBlazor.Hosting/        # ASP.NET integration
│   └── AgentBlazor.ProviderAdapters/ # OpenAI, Ollama, etc.
├── demo/
│   └── AgentBlazor.Demo/           # Working example app
└── tests/                          # Test projects
```

---

## The Big Picture

```
┌─────────────────────────────────────────────────────────────────┐
│                        USER INTERFACE                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │              AgentChatSurface Component                  │   │
│  │  - Shows conversation                                    │   │
│  │  - Handles user input                                    │   │
│  │  - Displays agent responses & actions                    │   │
│  └─────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      AGENT RUNTIME                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │   PLANNER    │→ │  VALIDATOR   │→ │   EXECUTOR   │          │
│  │  (LLM call)  │  │  (checks)    │  │  (runs it)   │          │
│  └──────────────┘  └──────────────┘  └──────────────┘          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   CONTROLLABLE COMPONENTS                       │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────┐   │
│  │ AgentForm  │ │AgentDataGrid│ │AgentNavMenu│ │ AgentTabs  │   │
│  └────────────┘ └────────────┘ └────────────┘ └────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

---

## Key Concepts

### 1. Agents

An **Agent** is the AI that understands user requests and decides what to do.

```csharp
// Agents are registered with:
- Name: "DefaultAgent"
- Description: What it does
- Instructions: System prompt for the LLM
- AllowedComponents: Which UI components it can control
```

### 2. Components

**Components** are UI elements the agent can interact with:

| Component | What it does | Actions |
|-----------|--------------|---------|
| `AgentDataGrid` | Shows data tables | sort, filter, selectRow |
| `AgentForm` | Data entry forms | setField, submit, reset |
| `AgentNavMenu` | Navigation menus | navigate, scroll |
| `AgentTabs` | Tab panels | switch |
| `AgentDialog` | Modal dialogs | open, close |

### 3. Actions

**Actions** are operations an agent can perform on components:

```csharp
// Example: Sort a data grid
{
    ComponentId: "AgentDataGrid",
    ActionId: "sort",
    Arguments: { "column": "RiskLevel", "direction": "desc" }
}
```

---

## How a Request Flows

When a user types "Sort suppliers by risk":

### Step 1: Planning (LLM)
The agent calls the LLM with:
- Your message
- Available components and their actions
- Current page context

LLM returns a **plan**:
```json
{
    "steps": [
        {
            "componentId": "AgentDataGrid",
            "actionId": "sort",
            "arguments": { "column": "RiskLevel", "direction": "desc" }
        }
    ],
    "responseText": "I'll sort the suppliers by risk level."
}
```

### Step 2: Validation
The runtime checks:
- Does `AgentDataGrid` exist? ✓
- Does it have a `sort` action? ✓
- Are the parameters valid? ✓

### Step 3: Execution
The runtime invokes the action on the component:
```csharp
dataGrid.Sort(column: "RiskLevel", direction: "desc");
```

### Step 4: Response
User sees:
- Agent's message: "I'll sort the suppliers by risk level."
- The grid actually sorts
- Activity log showing what happened

---

## The Chat Components

### AgentChatSurface
The main chat interface. Use this when you want a full conversation UI.

```razor
<AgentChatSurface
    Title="AI Assistant"
    Description="Ask me to help with the data"
    Theme="@ChatTheme.Dark()" />
```

### AgentChatWidget
A floating chat bubble (like Intercom). Opens into a chat window.

```razor
<AgentChatWidget
    BubbleLabel="Ask Agent"
    Theme="@ChatTheme.Glass()" />
```

### AgentChatBar
An inline search/command bar with conversation timeline.

```razor
<AgentChatBar
    Placeholder="Ask the agent..."
    Suggestions='["Show all suppliers", "Filter by risk"]'
    Theme="@ChatTheme.Minimal()" />
```

### AgentChatPanel
A side panel container for the chat surface.

```razor
<AgentChatPanel Width="400px" />
```

---

## Theming the Chat Components

All chat components support a `ChatTheme` for customization:

```csharp
// Built-in presets
var theme = ChatTheme.Dark();      // Dark mode
var theme = ChatTheme.Light();     // Light mode
var theme = ChatTheme.Glass();     // Glass-morphism effect
var theme = ChatTheme.Minimal();   // Clean, minimal style
var theme = ChatTheme.Branded("#e03a58");  // Custom accent color

// Full customization
var theme = new ChatTheme
{
    Variant = ChatThemeVariant.Dark,
    Style = ChatVisualStyle.Glass,
    BorderRadius = ChatBorderRadius.Large,
    Spacing = ChatSpacing.Comfortable,
    AccentColor = "#3b82f6",
    EnableGlassEffect = true,
    EnableAnimations = true,
    ShowTimestamps = true,
    ShowAvatars = true
};
```

---

## Setting Up in Your App

### 1. Add Services (Program.cs)

```csharp
// Add AgentBlazor services
builder.Services.AddAgentBlazor(options =>
{
    // Configure your LLM provider
    options.UseOpenAI(apiKey: "sk-...", model: "gpt-4o");
    // or: options.UseOllama("http://localhost:11434", "llama3");
    // or: options.UseAnthropic(apiKey, "claude-3-5-sonnet");
});

// Map endpoints (after app.Build())
app.MapAgentBlazorEndpoints();
```

### 2. Add Chat to Your Page

```razor
@page "/my-page"

<div class="page-content">
    <!-- Your main content -->
    <AgentDataGrid Items="@suppliers" />
</div>

<!-- Add the chat widget -->
<AgentChatWidget BubbleLabel="Need help?" />
```

### 3. Make Components Controllable

Wrap your components with Agent versions:

```razor
<!-- Instead of: -->
<MudDataGrid Items="@data" />

<!-- Use: -->
<AgentDataGrid Items="@data" />
```

Now the agent can sort, filter, and interact with this grid!

---

## Special Agent Behaviors

### Clarification
When the agent needs more info:

```
User: "Sort the grid"
Agent: "Which column should I sort by?"
User: "Risk level"
Agent: "Done! I sorted by risk level descending."
```

### Approval
Some actions require user approval before executing:

```
Agent: "I'll delete these 5 records. Approve?"
[Approve] [Deny]
```

### Streaming
Agent responses stream in real-time, showing:
- What action is being executed
- The result of each action
- The final response text

---

## File Locations Quick Reference

| What | Where |
|------|-------|
| Main runtime | `src/AgentBlazor.Core/Runtime/Planning/DeterministicAgentRuntime.cs` |
| Action planning | `src/AgentBlazor.Core/Runtime/Planning/StructuredActionPlanner.cs` |
| Chat UI | `src/AgentBlazor.Components/Chat/AgentChatSurface.razor` |
| Chat theming | `src/AgentBlazor.Components/Chat/ChatTheme.cs` |
| DataGrid wrapper | `src/AgentBlazor.Components/Wrappers/AgentDataGrid.razor` |
| Form wrapper | `src/AgentBlazor.Components/Wrappers/AgentForm.razor` |
| Service setup | `src/AgentBlazor.Core/Services/AgentBlazorServiceCollectionExtensions.cs` |
| Demo app | `demo/AgentBlazor.Demo/` |

---

## Debugging Tips

1. **Check the timeline**: AgentChatSurface shows planned actions and results
2. **Enable streaming**: See real-time tool calls and progress
3. **Use AgentChatBar with ShowTimeline=true**: See the full conversation flow
4. **Check browser console**: JS interop issues show up here
5. **Look at network tab**: LLM API calls are visible

---

## Common Patterns

### Full-screen chat (like ChatGPT)
```razor
<AgentChatSurface Style="height: 100vh" />
```

### Chat widget on every page
```razor
<!-- In MainLayout.razor -->
@Body
<AgentChatWidget />
```

### Side panel chat
```razor
<div style="display: flex">
    <main>@Body</main>
    <AgentChatPanel Width="350px" />
</div>
```

### Inline command bar
```razor
<AgentChatBar
    ShowTimeline="false"
    Suggestions='["Quick action 1", "Quick action 2"]' />
```

---

## Next Steps

1. Run the demo: `cd demo/AgentBlazor.Demo && dotnet run`
2. Try different chat components
3. Add `AgentDataGrid` or `AgentForm` to a page
4. Ask the agent to interact with them
5. Customize with `ChatTheme`

Questions? Check the integration tests for working examples:
`tests/AgentBlazor.IntegrationTests/`

Best Workflow Demos

  1. Incident Triage to Mitigation Plan

  - User asks for unhealthy services.
  - Agent filters/sorts grid, switches tabs, opens detail dialog.
  - Agent generates a triage summary card + trend chart + action form.
  - User applies generated form values; agent creates mitigation task.
  - Shows: DataGrid + Tabs + Dialog + Form + Chart + generated actions.

  2. Quarterly Review Builder

  - User asks for “Q2 performance review package”.
  - Agent navigates to reports page, applies filters, composes table + chart.
  - Agent generates “Review Draft” form (owner, audience, notes).
  - User edits generated form and approves final “publish” action.
  - Shows: multi-page navigation, data transformations, approval workflow.

  3. Change Request Lifecycle

  - User: “Create a change request from high-priority backlog items.”
  - Agent identifies candidates, drafts request form, opens approval-required actions.
  - User approves/denies; agent confirms and logs outcome in chat.
  - Shows: pending approvals, deterministic execution, auditability.

  4. Forecast-Driven Planning

  - User asks for 90-day forecast.
  - Agent uses chartDataSource resolver to render multiple chart blocks.
  - Agent generates follow-up actions like “drill into month 2” or “compare scenarios”.
  - Shows: external data source integration + reusable chart pipeline.

  5. Ops Daily Standup Copilot

  - Agent assembles a daily briefing: blockers table, progress chart, action checklist.
  - User triggers “create standup notes” generated form, then “send summary”.
  - Shows: end-to-end generated UI from read -> synthesize -> act.