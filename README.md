# AgentBlazor

A natural-language agent framework for Blazor Server apps. Add an AI assistant that can control your UI components in five minutes.

## Features

- **AgentDataGrid** - Filter, sort, select rows via natural language
- **AgentTabs** - Switch tabs by name
- **AgentDialog + AgentForm** - Open dialogs, fill and submit forms
- **Generative UI** - Agent-generated charts, tables, and cards in chat
- **Custom Components** - Add `[AgentAction]` / `[AgentReadable]` to any Blazor component

## Quick Start

### 1. Install the package

```bash
dotnet add package AgentBlazor
```

### 2. Register services in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAgentBlazor(options =>
{
    // OpenAI
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: "gpt-4o-mini");

    // — or Ollama (local, free) —
    // options.UseOllama("llama3.2", "http://localhost:11434/v1");
});

var app = builder.Build();
// ... middleware ...
app.MapAgentBlazorEndpoints();
app.Run();
```

### 3. Add assets to App.razor

```html
<!-- <head> -->
<link rel="stylesheet" href="_content/AgentBlazor/agentblazor.css" />

<!-- before </body> -->
<script src="_content/AgentBlazor/agentblazor.js"></script>
```

### 4. Add the chat widget to your layout

```razor
<!-- MainLayout.razor -->
<AgentChatWidget
    Title="Assistant"
    Placeholder="Ask me anything..."
    EnableGeneratedUi="true"
    Theme="@ChatTheme.Dark()"
    Width="31rem"
    Height="68vh" />
```

### 5. Use agent-controllable components

```razor
<AgentDataGrid TItem="SupplierRow"
               AgentId="supplier-grid"
               Items="@_suppliers"
               RowKeyProperty="SupplierId"
               Dense="true" Hover="true">
    <Columns>
        <PropertyColumn T="SupplierRow" TProperty="string"
                        Property="x => x.SupplierName" Title="Supplier" />
        <PropertyColumn T="SupplierRow" TProperty="int"
                        Property="x => x.RiskScore" Title="Risk Score" />
    </Columns>
</AgentDataGrid>
```

Now prompts like these work automatically:
- "filter to EMEA region"
- "sort by risk score descending"
- "select the highest-risk row"

## Supported AI Providers

| Provider | Configuration |
|----------|---------------|
| **OpenAI** | `options.UseOpenAI(apiKey, model)` |
| **Azure OpenAI** | `options.UseAzureOpenAI(endpoint, deployment, apiKey)` |
| **Ollama** | `options.UseOllama(model, endpoint)` - Free, runs locally |

## Environment Variables

Set your API key as an environment variable:

```bash
# OpenAI
export OPENAI_API_KEY=sk-...

# Azure OpenAI
export AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com
export AZURE_OPENAI_API_KEY=...
```

## Custom Agent Components

Add `AgentControllableComponentBase` + attributes to expose any component:

```csharp
[AgentComponent(AgentIdPrefix = "risk-counter")]
public partial class RiskCounter : AgentControllableComponentBase
{
    private int _highRiskCount = 12;

    [AgentReadable]
    public int HighRiskCount => _highRiskCount;

    [AgentAction]
    public Task ClearHighRisk()
    {
        _highRiskCount = 0;
        return RequestComponentRefreshAsync();
    }
}
```

Convention defaults:
- `ComponentType` is inferred from class name (or `[AgentComponent(ComponentType = ...)]`).
- `AgentId` is auto-generated (or fixed with `[AgentComponent(AgentId = ...)]`).
- `[AgentAction]` id defaults to `snake_case` method name.
- Non-nullable parameters are inferred as required.
- Enum parameters automatically expose allowed values.
- `[AgentParam]` is only needed for custom descriptions/constraints.

## Form Auto-Generation

Use `AgentFormPageBase<TModel>` for automatic form action generation:

```razor
@page "/my-form"
@inherits AgentFormPageBase<MyFormModel>

@code {
    protected override string AgentIdValue => "my-form";
    protected override string FormDisplayName => "My Form";
}
```

The agent automatically gets a `fill_my_form` action with all model properties as parameters.

## Demo

Run the demo app:

```bash
cd demo/AgentBlazor.Demo
dotnet run
```

Open https://localhost:5001 and try prompts like:
- "onboard Apex Components from Germany, high risk tier, priority 2"
- "filter suppliers by EMEA region"
- "show a bar chart of sales by region"

## License

MIT
