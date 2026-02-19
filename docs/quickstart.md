# Quickstart

## Goal

Get AgentBlazor running with a built-in component-aware agent using minimal setup.

## 1. Install Package

```bash
dotnet add package AgentBlazor
```

`AgentBlazor` includes component wrappers plus transitive runtime/hosting/provider wiring.

## 2. Register Services in `Program.cs`

```csharp
using AgentBlazor;
using MudBlazor.Services;

builder.Services.AddMudServices();
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI("sk-...", "gpt-4o-mini");
});
```

`AddAgentBlazor(...)` applies sensible defaults internally:
- Registers AgentBlazor runtime + component registry.
- Registers AG-UI hosting services.
- Enables the built-in default agent.

Optional provider variants:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseAzureOpenAI("https://my-resource.openai.azure.com/", "gpt-4o-mini");
});
```

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseOllama("qwen2.5-coder:7b");
});
```

## 3. Add `@using` in `_Imports.razor`

```razor
@using AgentBlazor.Components
@using MudBlazor
```

## 4. Use Components on a Page

```razor
<AgentDataGrid AgentId="supplier-grid" Items="@Suppliers">
    ...
</AgentDataGrid>

<AgentChatWidget />
```

Optional persistent docked chat:

```razor
<div class="d-flex" style="height: 100vh;">
    <div class="flex-grow-1">
        @* App content *@
    </div>
    <AgentChatPanel Width="350px" />
</div>
```

## 5. Add AG-UI Endpoint (optional, for hosted AG-UI stream clients)

```csharp
using AgentBlazor;

app.MapAgentBlazorAgUiRun();
```

## 6. Verify

Run your app and confirm:
- Chat widget renders.
- Prompt actions execute against registered agent-aware wrappers.
- AG-UI stream endpoint responds at `POST /agentblazor/agui/run` when mapped.
- In the demo app, `/mud-nav-tabs-agent` shows AgentNavMenu + AgentTabs actions executing from chat prompts.

## 7. Advanced Configuration (optional)

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI("sk-...");
    options.AgentInstructions = "Supply chain management app. Risk score >= 70 is high risk.";
    options.Configure(agentBlazor =>
    {
        // Advanced framework options (component catalog, policies, etc.).
    });
});
```
