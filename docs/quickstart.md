# Quickstart

Get one working chat route into a fresh Blazor app. AgentBlazor does not create a responding agent by default. You must register one workflow.

## Install

```bash
dotnet add package AgentBlazor --prerelease
```

## Program.cs

```csharp
using AgentBlazor;
using MudBlazor.Services;
using MyApp.Components;
using MyApp.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini");

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<HelloWorkflow>("hello-workflow");
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAgentBlazorEndpoints();

app.Run();
```

## Components/_Imports.razor

```razor
@using AgentBlazor.Components
```

## Components/App.razor

```razor
<link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
<link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
...
<HeadOutlet @rendermode="InteractiveServer" />
</head>

<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
    <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
</body>
```

## Components/Layout/MainLayout.razor

```razor
@inherits LayoutComponentBase
@using MudBlazor

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<div class="page">
    @Body
</div>

<AgentChatWidget
    Title="Hello workflow"
    Placeholder="Ask the agent to say hello..." />
```

## Workflows/HelloWorkflow.cs

```csharp
using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace MyApp.Workflows;

[AgentCapability("hello_workflow")]
public sealed class HelloWorkflow
{
    [AgentAction("Say hello")]
    public Task<CapabilityResult> SayHelloAsync()
        => Task.FromResult(CapabilityResult.Success("Hello from AgentBlazor."));
}
```

## API key

Use either appsettings:

```json
{
  "OpenAI": {
    "ApiKey": "sk-...",
    "Model": "gpt-4o-mini"
  }
}
```

Or user secrets:

```bash
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-..."
```

## Run

```bash
dotnet run
```

You should see the floating widget in the bottom-right corner. Open it and ask: `Say hello`.

## Notes

- `MapAgentBlazorEndpoints()` is required or the chat UI will render without a working runtime endpoint.
- The current public package requires an explicit workflow or agent registration before the chat can respond.
- `AgentBlazorShell` is not the public quickstart path until the next preview release lands its child-content/widget fix.

## Next

- Starter sample: [samples/AgentBlazor.Starter](/home/ashdev/workspace/AgentBlazor/samples/AgentBlazor.Starter)
- Hosted WebAssembly client: [src/AgentBlazor.Client/PACKAGE_README.md](/home/ashdev/workspace/AgentBlazor/src/AgentBlazor.Client/PACKAGE_README.md)
