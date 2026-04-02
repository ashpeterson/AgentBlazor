# Quickstart

Get AgentBlazor running in your Blazor app in under 5 minutes.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key (or Azure OpenAI, Ollama)

## 1. Install the Package

```bash
dotnet add package AgentBlazor
```

## 2. Configure Services

In `Program.cs`:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    // Choose your LLM provider
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: "gpt-4o-mini");

    // Register your workflow agent
    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<MyCapabilities>("assistant", agent =>
        {
            agent.WithDescription("Help users complete their tasks.");
            agent.WithRoutePrefixes("/"); // Active on all routes
        });
    });
});
```

## 3. Create a Capability Class

Use `[AgentCapability]` to mark a class as agent-accessible:

```csharp
[AgentCapability("assistant")]
public sealed class MyCapabilities
{
    private readonly IMyService _service;

    public MyCapabilities(IMyService service)
    {
        _service = service;
    }

    [AgentAction("Search for items")]
    public async Task<CapabilityResult> SearchAsync(
        [AgentParam("Search query")] string query)
    {
        var results = await _service.SearchAsync(query);
        return CapabilityResult.Success($"Found {results.Count} items.");
    }

    [AgentAction("Submit order", RequiresApproval = true)]
    public async Task<CapabilityResult> SubmitOrderAsync(
        [AgentParam("Order ID")] Guid orderId)
    {
        await _service.SubmitAsync(orderId);
        return CapabilityResult.Success("Order submitted.")
            .WithNextActions("View order status", "Create another order");
    }
}
```

## 4. Add the Chat Widget

In your layout or page:

```razor
@using AgentBlazor.Components

<AgentChatWidget
    Title="Assistant"
    Placeholder="Ask me anything..."
    Width="28rem"
    Height="60vh" />
```

## 5. Run Your App

```bash
dotnet run
```

Click the chat widget and try:
- "Search for widgets"
- "Submit order ABC123"

The agent will call your capability methods and handle approvals automatically.

## Attribute Reference

### `[AgentCapability]`

Marks a class as containing agent-callable actions.

```csharp
[AgentCapability("agent-name")]
public class MyCapabilities { }
```

### `[AgentAction]`

Marks a method as agent-callable.

```csharp
[AgentAction("Description shown to agent")]
public Task<CapabilityResult> MyActionAsync() { }

// With approval requirement
[AgentAction("Dangerous action", RequiresApproval = true)]
public Task<CapabilityResult> DangerousAsync() { }
```

### `[AgentParam]`

Describes a parameter for the agent.

```csharp
public Task<CapabilityResult> SearchAsync(
    [AgentParam("The search query")] string query,
    [AgentParam("Max results to return")] int limit = 10)
```

### `[AgentReadable]`

Exposes component state to the agent.

```csharp
[AgentReadable("Current user selection")]
public string? SelectedItem { get; set; }
```

## Return Types

### `CapabilityResult`

All `[AgentAction]` methods should return `Task<CapabilityResult>`:

```csharp
// Success
return CapabilityResult.Success("Operation completed.");

// Success with warnings
return CapabilityResult.Success("Completed.")
    .WithWarning("One item was skipped.");

// Success with suggested next actions
return CapabilityResult.Success("Order created.")
    .WithNextActions("View order", "Create another");

// Failure
return CapabilityResult.Failure("Could not complete operation.");

// Blocked (needs user action first)
return CapabilityResult.Blocked("Please select an item first.");
```

## Using Agentic Components

AgentBlazor includes MudBlazor-backed components the agent can control:

```razor
<AgentDataGrid @ref="_grid" Items="@_items" T="Product">
    <Columns>
        <PropertyColumn Property="x => x.Name" />
        <PropertyColumn Property="x => x.Price" />
    </Columns>
</AgentDataGrid>

<AgentForm @ref="_form" Model="@_model">
    <AgentTextField @bind-Value="_model.Name" Label="Name" />
    <AgentButton ButtonType="ButtonType.Submit">Save</AgentButton>
</AgentForm>
```

The agent can filter, sort, paginate the grid, and fill form fields automatically.

## Pro Tier Features

Enable analytics, audit logging, and smart suggestions with a Pro license:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(apiKey, "gpt-4o-mini");

    // Enable Pro tier with SQLite persistence
    options.UseProLicense("AB-PRO-YOUR-LICENSE-KEY");
});
```

Then add the Pro Dashboard to your app:

```razor
<AgentProDashboard Title="Analytics" DaysRange="30" />
```

## Available Components

AgentBlazor includes 14 agentic components:

| Component | Purpose |
|-----------|---------|
| `AgentDataGrid` | Filterable, sortable, paginated data grid |
| `AgentForm` | Validated form with agent-fillable fields |
| `AgentDialog` | Modal dialogs with approval boundaries |
| `AgentTabs` | Tab navigation |
| `AgentNavMenu` | Route navigation |
| `AgentSelect` | Dropdown selection |
| `AgentAutocomplete` | Search-as-you-type selection |
| `AgentDatePicker` | Single date selection |
| `AgentDateRangePicker` | Date range selection |
| `AgentTreeView` | Hierarchical selection |
| `AgentStepper` | Multi-step workflows |
| `AgentCommandBar` | Action buttons |
| `AgentFileUpload` | File attachment with policies |
| `AgentChatWidget` | Floating chat interface |

## Troubleshooting

### Agent not responding

1. Check your OpenAI API key is valid
2. Ensure `AddAgentBlazor` is called before `builder.Build()`
3. Verify the capability class is registered with `AddWorkflow<T>`

### Actions not appearing

1. Ensure methods are marked with `[AgentAction]`
2. Check the class has `[AgentCapability("agent-name")]`
3. Verify the route prefix matches the current page

### Component state not visible to agent

1. Add `[AgentReadable]` to properties the agent should see
2. Ensure the component has a `@ref` reference in the page

### Pro features not working

1. Verify `UseProLicense()` is called with a valid key
2. Check the SQLite database is writable at the data directory
3. Ensure `IUsageAnalyticsService` is injected (not the null implementation)

## Next Steps

- Run `demo/AgentBlazor.Demo` to see workflow orchestration in action
- See [Pricing Tiers](pricing-tiers.md) for Pro features
- Check `docs/STATUS.md` for current implementation status
