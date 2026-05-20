# Entity Framework Core Schema Exposure

`AgentBlazor.EntityFrameworkCore` lets an AgentBlazor agent see a read-safe description of selected EF Core entities while keeping data access behind your typed workflow actions.

This is schema-only planning context. It does not execute queries, generate LINQ, generate SQL, scan every `DbSet`, or grant write access.

## Install

```bash
dotnet add package AgentBlazor.EntityFrameworkCore
```

If you pin versions, use the same AgentBlazor version for the runtime and EF package:

```bash
dotnet add package AgentBlazor
dotnet add package AgentBlazor.EntityFrameworkCore
```

Use `0.2.0` or later. This release includes the corrected EF package shape and tool-friendly schemas for date-like workflow parameters.

## DbContext Setup

Use `IDbContextFactory<TContext>`. This follows the Blazor EF Core guidance to create a context per operation instead of sharing one context across interactive UI work.

```csharp
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("AppDb"));
});
```

## Register A Schema

Register only the entities and properties the agent may reason about. Properties are allowlisted; unlisted fields are not exposed.

```csharp
using AgentBlazor;
using AgentBlazor.EntityFrameworkCore;

builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini");

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddEntitySchema<AppDbContext>("support-data", schema =>
        {
            schema.WithDescription("Read-safe support ticket fields.");

            schema.Entity<SupportTicket>("support_tickets", entity =>
            {
                entity.WithDescription("Tickets visible to the support workflow.");
                entity.Property(ticket => ticket.Id, "Ticket identifier such as TCK-1042.");
                entity.Property(ticket => ticket.Status, "Current ticket status.");
                entity.Property(ticket => ticket.Priority, "Priority label.");
                entity.Property(ticket => ticket.CreatedUtc, "Creation timestamp.");
            });
        });

        agentBuilder.AddWorkflow<SupportInboxCapabilities>("support-agent", agent =>
        {
            agent.WithRoutePrefixes("/support");
            agent.WithDataSchemas("support-data");
        });
    });
});
```

## Keep Execution Typed

The schema helps the model understand what concepts exist. Actual reads and writes still go through your `[AgentAction]` methods.

```csharp
using AgentBlazor.App;
using AgentBlazor.Attributes;
using Microsoft.EntityFrameworkCore;

[AgentCapability("support_inbox")]
public sealed class SupportInboxCapabilities(IDbContextFactory<AppDbContext> dbFactory)
{
    [AgentAction("Show open support tickets")]
    public async Task<CapabilityResult> ShowOpenTicketsAsync(int days = 7)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var tickets = await db.SupportTickets
            .AsNoTracking()
            .Where(ticket => ticket.WaitingOnReply)
            .OrderByDescending(ticket => ticket.Priority)
            .Take(20)
            .Select(ticket => new
            {
                ticket.Id,
                ticket.Status,
                ticket.Priority
            })
            .ToArrayAsync();

        return CapabilityResult
            .Success($"Found {tickets.Length} open tickets.")
            .WithOutput("tickets", tickets);
    }
}
```

The action owns tenant filtering, authorization, row limits, projections, and validation. AgentBlazor does not bypass those app-owned controls.

## Safety Rules

- Use explicit property allowlists.
- Do not expose sensitive fields such as emails, tokens, addresses, notes, secrets, or internal IDs unless they are safe for the model to see.
- Keep multi-tenancy and row-level security in your app's EF filters or typed workflow methods.
- Keep result sizes bounded with projection and `Take(...)`.
- Do not accept model-produced SQL or LINQ as input.

## Current Boundary

v0.2 schema exposure is intentionally narrow:

- Included: schema metadata for planning context.
- Included: per-agent opt-in with `WithDataSchemas(...)`.
- Included: EF model validation that exposed properties are mapped.
- Not included: query execution tools.
- Not included: component canvas generation from EF results.
- Not included: dynamic SQL, generated LINQ, or writes.

Constrained query templates and result canvas rendering are future work.
