# Quickstart

Get one AgentBlazor route working first. Keep the CLI out of the first install.

## Prerequisites

- `net8.0` through `net10.0` are supported
- use the .NET 10 SDK when working from this repo or running the included demo/sample apps
- an OpenAI API key, or Azure OpenAI endpoint/deployment/key or credential

## 1. Install the Package

```bash
dotnet add package AgentBlazor --version 0.1.0-preview.10
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
        model: builder.Configuration["OpenAI:Model"]!);

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<SupportInboxCapabilities>("support-inbox", agent =>
        {
            agent.WithRoutePrefixes("/support");
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
[AgentCapability("support_inbox")]
public sealed class SupportInboxCapabilities
{
    [AgentAction("Show open tickets that still need a reply")]
    public Task<CapabilityResult> ShowOpenTicketsAsync(
        [AgentParam("Include tickets from the last N days", Required = false)] int days = 7)
    {
        return Task.FromResult(
            CapabilityResult.Success($"Highlighted tickets from the last {days} days.")
                .WithNextActions("Explain the queue", "Draft a reply"));
    }

    [AgentAction("Draft a reply for the highlighted tickets", RequiresApproval = true)]
    public Task<CapabilityResult> DraftReplyAsync()
    {
        return Task.FromResult(
            CapabilityResult.Success("Prepared the reply draft.")
                .WithNextActions("Review the reply", "Approve the draft"));
    }
}
```

## 6. Add a Chat Surface

In your layout or page:

```razor
@using AgentBlazor.Components

<AgentChatWidget
    Title="Support inbox"
    Placeholder="Show open tickets, explain the queue, or draft a reply..."
    Width="28rem"
    Height="60vh" />
```

Use `AgentChatWidget` for a floating assistant.
Use `AgentChatSurface` when chat should be embedded directly in the page:

```razor
@using AgentBlazor.Components

<AgentChatSurface
    Title="Support inbox"
    Description="Review the current queue and prepare the next safe step."
    LockAgentToCurrentRoute="true"
    ShowAgentSelector="false"
    EnableGeneratedUi="true" />
```

## Hosted WebAssembly Client Chat

For hosted WebAssembly apps, keep the full AgentBlazor runtime on the server project and use the browser-safe client package in the `.Client` project.

Server project:

```csharp
app.MapAgentBlazorEndpoints();
app.MapAgentBlazorRemoteChat();
```

Client project:

```bash
dotnet add ./MyApp.Client/MyApp.Client.csproj package AgentBlazor.Client --version 0.1.0-preview.10
```

Client `_Imports.razor`:

```razor
@using AgentBlazor.Client.Chat
```

Client layout or page:

```razor
<AgentRemoteChatWidget Endpoint="/agentblazor/chat/run" Title="Assistant" />
```

The browser-safe client package also includes `AgentRemoteChatSurface`, `AgentRemoteChatPanel`, and `AgentRemoteChatBar`. These components call the server runtime over HTTP and do not require the server-first `AgentBlazor` component package or MudBlazor providers in the WebAssembly client.

If your app already has fixed bottom-right controls, move the floating widget with host-owned overrides:

```razor
<AgentRemoteChatWidget Endpoint="/agentblazor/chat/run"
                       Title="Assistant"
                       Style="right: 2rem; bottom: 7rem;" />
```

The hosted WebAssembly path has been validated in a generated server+client Blazor Web App by installing packed local `AgentBlazor` and `AgentBlazor.Client` packages, mapping `MapAgentBlazorRemoteChat()`, registering client `HttpClient`, submitting prompts through remote widget/surface/panel/bar, and verifying widget minimize/reopen behavior.

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
2. Confirm `OpenAI:Model` resolves to a tool-capable OpenAI chat model
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

## Next Steps

- Run `samples/AgentBlazor.Starter` to see the smallest working route-scoped setup
- Read [Advanced CLI](advanced/cli.md) if you want scaffold help for an existing app
- Use OpenAI as the first production provider you validate in a real app
