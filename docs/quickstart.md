# Quickstart

Get one working AgentBlazor chat widget into a fresh Blazor app. Tested against a clean `dotnet new blazor` project with `AgentBlazor 0.2.0-preview.3`.

Hosted demo: https://demo.agentblazor.com/demo/workflows/support-inbox

AgentBlazor does not create a responding agent by default. You must register at least one workflow.

## 1. Install

```bash
dotnet add package AgentBlazor --version 0.2.0-preview.3
```

## 2. Program.cs

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
        agentBuilder.AddWorkflow<HelloWorkflow>("hello");
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

## 3. Components/_Imports.razor

```razor
@using AgentBlazor.Components
```

## 4. Components/App.razor

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <ResourcePreloader />
    <link rel="stylesheet" href="@Assets["_content/MudBlazor/MudBlazor.min.css"]" />
    <link rel="stylesheet" href="@Assets[AgentBlazorAssetPaths.Css]" />
    <link rel="stylesheet" href="@Assets["app.css"]" />
    <link rel="stylesheet" href="@Assets["MyApp.styles.css"]" />
    <ImportMap />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>

<body>
    <Routes @rendermode="InteractiveServer" />
    <ReconnectModal />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
    <script src="@Assets["_content/MudBlazor/MudBlazor.min.js"]"></script>
    <script src="@Assets[AgentBlazorAssetPaths.Js]"></script>
</body>
</html>
```

## 5. Components/Layout/MainLayout.razor

Wrap your existing layout content:

```razor
@inherits LayoutComponentBase

<AgentBlazorShell>
    <div class="page">
        <div class="sidebar">
            <NavMenu />
        </div>

        <main>
            <div class="top-row px-4">
                <a href="https://learn.microsoft.com/aspnet/core/" target="_blank">About</a>
            </div>

            <article class="content px-4">
                @Body
            </article>
        </main>
    </div>

    <div id="blazor-error-ui" data-nosnippet>
        An unhandled error has occurred.
        <a href="." class="reload">Reload</a>
        <span class="dismiss">🗙</span>
    </div>
</AgentBlazorShell>
```

## 6. Workflows/HelloWorkflow.cs

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

## 7. API Key

Use either `appsettings.Development.json`:

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
dotnet user-secrets set "OpenAI:Model" "gpt-4o-mini"
```

## 8. Run

```bash
dotnet run
```

You should see the floating widget in the bottom-right corner. Open it and ask:

```text
Say hello
```

## Notes

- `MapAgentBlazorEndpoints()` is required. Without it, the widget can render but cannot call the runtime.
- `AgentBlazorShell` includes the widget in `0.2.0-preview.3`.
- A registered workflow is required for the chat to respond.

## Next

- [Hosted demo](https://demo.agentblazor.com/demo/workflows/support-inbox)
- [0.2.0 release notes](releases/0.2.0.md)
- [Starter sample](../samples/AgentBlazor.Starter/README.md)
- [Hosted WebAssembly client](../src/AgentBlazor.Client/PACKAGE_README.md)
- [Recoverable capability errors](capability-errors.md)
- [Entity Framework Core schema exposure](entity-framework.md)
