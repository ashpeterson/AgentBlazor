# AgentBlazor.EntityFrameworkCore

Optional Entity Framework Core integration for exposing read-safe entity schema to AgentBlazor agents.

This package is schema-only. It does not execute queries, generate LINQ, generate SQL, scan every `DbSet`, or grant data mutation capability.

Install:

```bash
dotnet add package AgentBlazor.EntityFrameworkCore
```

Pinned install:

```bash
dotnet add package AgentBlazor.EntityFrameworkCore --version 0.2.0
```

Use `0.2.0` or later. This release includes the corrected EF package shape and tool-friendly schemas for date-like workflow parameters.

Register your `DbContext` with `IDbContextFactory<TContext>`:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("AppDb"));
});
```

Expose only safe entities and properties:

```csharp
using AgentBlazor.EntityFrameworkCore;

options.ConfigureBuilder(agentBuilder =>
{
    agentBuilder.AddEntitySchema<AppDbContext>("support-data", schema =>
    {
        schema.Entity<SupportTicket>("support_tickets", entity =>
        {
            entity.Property(x => x.Id, "Ticket identifier");
            entity.Property(x => x.Status, "Current ticket status");
            entity.Property(x => x.CreatedUtc, "Creation timestamp");
        });
    });

    agentBuilder.AddWorkflow<SupportCapabilities>("support-agent", agent =>
    {
        agent.WithDataSchemas("support-data");
    });
});
```

Execution remains app-owned. Use normal `[AgentAction]` methods with EF projections, row limits, authorization, and tenant filtering. Do not pass model-generated SQL or LINQ into EF.

Full docs: https://github.com/ashpeterson/AgentBlazor/blob/master/docs/entity-framework.md
