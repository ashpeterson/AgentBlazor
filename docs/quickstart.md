# Quickstart

Get AgentBlazor running in your Blazor app in under 5 minutes.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key

The currently validated production path is OpenAI via `options.UseOpenAI(...)`. Azure OpenAI and other providers still work as integrations, but OpenAI is the primary path to ship first.

Status as of 2026-04-09:

- the non-demo test matrix is green
- the CLI now supports `init -> scaffold -> doctor -> validate`
- standard hosted WebAssembly server+client installs are now part of the supported scaffold path

## Optional: Install The CLI

The CLI can scaffold the standard runtime wiring for a standard Blazor host and the standard hosted WebAssembly server+client path, including a provider template, but you still need to supply the real configuration values for your environment.

```bash
dotnet tool install --global AgentBlazor.Cli --prerelease
agentblazor init ./MySolution.sln --host MyBlazorApp
agentblazor scaffold ./MySolution.sln --host MyBlazorApp --provider openai --approve
```

## 1. Install the Package

```bash
dotnet add package AgentBlazor
```

## 2. Configure Services

In `Program.cs`:

```csharp
using AgentBlazor;
using MudBlazor.Services;

builder.Services.AddMudServices();

builder.Services.AddAgentBlazor(options =>
{
    // Choose your LLM provider
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-5.4-mini");

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

## 3. Add The Host Shell

In `Components/App.razor`, include the MudBlazor and AgentBlazor assets:

```razor
<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
<link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
...
<script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
<script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
```

In your main layout, add the MudBlazor providers:

```razor
@using MudBlazor

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />
```

For a complete runnable shape, copy the host shell from:

- [App.razor](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Components/App.razor)
- [MainLayout.razor](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter/Components/Layout/MainLayout.razor)

## 4. Map Endpoints

After `builder.Build()`:

```csharp
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();
```

Without `MapAgentBlazorEndpoints()`, the chat UI can render but the runtime endpoint will not be available.

## 5. Create a Capability Class

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

## 6. Add a Chat Surface

In your layout or page:

```razor
@using AgentBlazor.Components

<AgentChatWidget
    Title="Assistant"
    Placeholder="Ask me anything..."
    Width="28rem"
    Height="60vh" />
```

Use `AgentChatWidget` for a floating assistant.
Use `AgentChatSurface` when chat should be embedded directly in the page:

```razor
@using AgentBlazor.Components

<AgentChatSurface
    Title="Assistant"
    Description="Help users complete the current workflow."
    LockAgentToCurrentRoute="true"
    ShowAgentSelector="false"
    EnableGeneratedUi="true" />
```

## 7. Run Your App

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
    options.UseOpenAI(apiKey, "gpt-5.4-mini");

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
2. Confirm `OpenAI:Model` resolves to a tool-capable model such as `gpt-5.4-mini`
3. Ensure `AddAgentBlazor` is called before `builder.Build()`
4. Ensure `AddMudServices()` is registered
5. Ensure the MudBlazor providers are present in your layout
6. Ensure the CSS and JS assets are added in `Components/App.razor`
7. Ensure `app.MapAgentBlazorEndpoints()` is called after `builder.Build()`
8. Verify the capability class is registered with `AddWorkflow<T>`

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
- Read [CLI Guide](cli.md) if you want to generate `.agentblazor/AGENT.md`
- See [Pricing Tiers](pricing-tiers.md) for Pro features
- Check `docs/STATUS.md` for current implementation status
- Use OpenAI as the first production provider you validate in a real app
