# AgentBlazor.EntityFrameworkCore

Optional Entity Framework Core integration for exposing read-safe entity schema to AgentBlazor agents.

This package is schema-only. It does not execute queries, generate LINQ, generate SQL, or grant data mutation capability.

```csharp
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
