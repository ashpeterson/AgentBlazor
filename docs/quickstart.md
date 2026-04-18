# Quickstart

Get AgentBlazor running in your Blazor app in under 5 minutes.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key, or an Azure OpenAI resource endpoint, deployment name, and API key or Azure credential

The most validated production path is OpenAI via `options.UseOpenAI(...)`. Azure OpenAI is supported as a first-class provider through the Microsoft Azure OpenAI client and the same `IChatClient` runtime path.

Status as of 2026-04-18:

- the non-demo test matrix is green
- the CLI now supports `init -> scaffold -> doctor -> validate`
- standard hosted WebAssembly server+client installs are now part of the supported scaffold path
- `0.1.0-preview.8` is the current source/package version
- `0.1.0-preview.8` is the latest GitHub Packages published-feed version with full clean-app, external real-app, and all-surface chat validation
- scaffolded assets preserve existing CSP nonce attributes in nonce-aware host shells

## Optional: Install The CLI

The CLI can scaffold the standard runtime wiring for a standard Blazor host and the standard hosted WebAssembly server+client path, including a provider template, but you still need to supply the real configuration values for your environment.

For private-preview installs, pin the CLI and runtime package to the same version. Do not rely on a broad `--prerelease` install when testing scaffolded workflow code because the generated `AppCapabilities.cs` file uses semantic workflow APIs from `AgentBlazor.App`.

```bash
dotnet tool install --global AgentBlazor.Cli --version 0.1.0-preview.8 --add-source https://nuget.pkg.github.com/ashpeterson/index.json
dotnet add ./MyBlazorApp/MyBlazorApp.csproj package AgentBlazor --version 0.1.0-preview.8 --source https://nuget.pkg.github.com/ashpeterson/index.json
agentblazor init ./MySolution.sln --host MyBlazorApp
agentblazor scaffold ./MySolution.sln --host MyBlazorApp --provider openai --approve
```

Use `--provider azure-openai` instead when the host app should be scaffolded for Azure OpenAI configuration.

## 1. Install the Package

```bash
dotnet add package AgentBlazor --version 0.1.0-preview.8 --source https://nuget.pkg.github.com/ashpeterson/index.json
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

Azure OpenAI API key configuration:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseAzureOpenAI(
        endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
        deploymentName: builder.Configuration["AzureOpenAI:DeploymentName"]!,
        apiKey: builder.Configuration["AzureOpenAI:ApiKey"]);
});
```

Azure identity or managed identity configuration:

```csharp
using Azure.Identity;

builder.Services.AddAgentBlazor(options =>
{
    options.UseAzureOpenAI(
        endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
        deploymentName: builder.Configuration["AzureOpenAI:DeploymentName"]!,
        credential: new DefaultAzureCredential());
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

Use `AgentChatPanel` when the assistant should sit beside a dense operational screen:

```razor
@using AgentBlazor.Components

<AgentChatPanel
    Title="Operations Assistant"
    Description="Review the current screen and guide safe production changes."
    Height="42rem"
    ShowAgentSelector="false"
    EnableGeneratedUi="true" />
```

Use `AgentChatBar` when the page needs a compact command/search-style entry point:

```razor
@using AgentBlazor.Components

<AgentChatBar
    Placeholder="Ask for a status update or next action..."
    Suggestions="@ProductionPrompts"
    EnableGeneratedUi="true" />

@code {
    private static readonly IReadOnlyList<string> ProductionPrompts =
    [
        "Summarize this page for an operations lead.",
        "List the safest next actions before changing production state.",
        "Draft a handoff note for this workflow."
    ];
}
```

## 7. Run Your App

```bash
dotnet run
```

Click the chat widget or embedded chat surface and try prompts that match production Blazor workflows:

- "Review this order queue, identify stuck orders, and recommend the safest next action."
- "Find invoices due this week, group them by risk, and draft a collector handoff note."
- "Check whether order ABC123 can be submitted, explain any missing approvals, and submit it only if policy allows."
- "Summarize the current user-management page for an auditor and list accounts that need review."
- "Find recently locked users, explain the likely causes, and draft a remediation checklist."
- "Prepare a release-readiness checklist from the visible deployment status, failed checks, and pending approvals."
- "Compare staging and production status, list blockers, and draft the release manager handoff."
- "Draft a customer-support status update from the current incident screen and include open blockers."
- "Turn the current incident timeline into a short executive update with customer impact and next owner."
- "Review this inventory screen, identify low-stock exceptions, and create a purchasing follow-up note."
- "Inspect this claims workflow, identify missing evidence, and propose the next compliant action."

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

### `AgentBlazor.App`, `CapabilityResult`, or `AgentCapability` cannot be found

This means the app is compiling against an AgentBlazor package that does not expose the semantic workflow APIs used by the scaffolded `Workflows/AppCapabilities.cs` file.

1. Pin both `AgentBlazor` and `AgentBlazor.Cli` to the same preview version.
2. Delete `bin`, `obj`, and the cached AgentBlazor package folder.
3. Restore with `--force-evaluate`.
4. Confirm `obj/project.assets.json` lists `AgentBlazor.Core.dll` under the `AgentBlazor` compile assets.

PowerShell reset:

```powershell
Remove-Item -Recurse -Force "$env:USERPROFILE\.nuget\packages\agentblazor" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue
dotnet restore .\MyBlazorApp.csproj --force --force-evaluate
dotnet build .\MyBlazorApp.csproj
```

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
