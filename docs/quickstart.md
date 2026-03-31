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

## Next Steps

- See `samples/AgentBlazor.Starter` for a complete example
- Run `demo/AgentBlazor.Demo` to see workflow orchestration in action
- Read [Architecture](architecture.md) for deeper understanding
- See [Pricing Tiers](pricing-tiers.md) for Pro features
