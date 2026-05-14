# EF Entity Exposure Plan: Schema-Only v0.2

## Summary

Ship schema-only entity exposure as the v0.2 attempt if structured errors are stable at the 2026-05-24 mid-sprint check. Do not ship LLM-generated LINQ, SQL, or ad-hoc query execution in v0.2.

The chosen deliverable is an optional package: `AgentBlazor.EntityFrameworkCore`. This avoids forcing EF dependencies into the main AgentBlazor package while letting EF users expose read-safe entity shapes to an agent.

Research basis:

- Microsoft recommends `IDbContextFactory<TContext>` or one context per operation for Blazor EF usage: <https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core>
- Read-only EF paths should avoid tracking where queries are later added: <https://learn.microsoft.com/en-us/ef/core/querying/tracking>
- Query execution must project only needed properties and limit result sizes: <https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying>
- Dynamic SQL and schema-string query generation are unsafe without strict whitelisting: <https://learn.microsoft.com/en-us/ef/core/querying/sql-queries>
- Multi-tenancy and row isolation must remain app-owned through EF filters or typed methods: <https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy>

## Key Design

- Add `src/AgentBlazor.EntityFrameworkCore` as an optional package referencing `AgentBlazor.Core` and `Microsoft.EntityFrameworkCore`.
- Add core schema abstractions, not EF dependencies, to `AgentBlazor.Core`: schema set, entity schema, property schema, and an `IAgentDataSchemaCatalog`.
- Add an explicit agent opt-in API, for example `agent.WithDataSchemas("support-data")`, so schema metadata is only visible to selected agents.
- Add an EF registration API, for example `agentBuilder.AddEntitySchema<DemoWorkflowDbContext>("support-data", schema => ...)`.
- Default to property allowlists, not expose-all. The developer must intentionally expose each entity and each property.
- Inject schema metadata into the selected agent's instructions/context only. Do not expose it as a callable query tool in v0.2.
- Reuse existing `AgentUiDocument` table/chart/card concepts only as a future display target; v0.2 schema-only does not generate result tables from EF automatically.

Rejected for v0.2:

- No LLM-generated LINQ.
- No dynamic SQL.
- No automatic `DbSet` scanning exposed to agents.
- No query execution tool.
- No writes or mutations.
- No schema exposure to agents unless explicitly opted in.

## Mid-Sprint Gate

On 2026-05-24, choose one path:

- If structured errors are stable: implement schema-only optional package during Week 3.
- If structured errors are fragile: defer entity exposure to v0.3 and use Week 3 to harden structured errors.

Structured errors count as stable only if:

- Core structured-error tests pass.
- Binding-layer failure cases are covered.
- Demo workflows no longer spiral on recoverable tool failures.
- No open install/demo regression is blocking v0.2.

## Implementation Plan

1. Create the optional EF package and add it to the solution/package build.
2. Add core data-schema records and catalog interfaces without referencing EF.
3. Add `WithDataSchemas(...)` to `AgentRegistrationBuilder`, storing allowed schema set names on the agent registration.
4. Update the runtime adapter to append only the current agent's allowed schema summaries into the prompt/context.
5. Implement EF schema registration in the optional package using EF model metadata plus explicit developer allowlists.
6. Add a demo/reference schema for support tickets, but keep actual ticket actions as typed workflow methods.
7. Update issue #1 with the decision: v0.2 ships schema context only; constrained query templates move to v0.3.

## Test Plan

- Unit test schema allowlisting: unlisted entities/properties never appear.
- Unit test agent isolation: Agent A with `support-data` sees it; Agent B without opt-in does not.
- Unit test EF metadata extraction from a sample `DbContext`.
- Integration test prompt/tool resolution confirms schema text is present for the selected agent and absent elsewhere.
- Demo smoke test: support inbox agent can reason about exposed ticket fields, but still executes only typed workflow actions.
- Package smoke test: a non-EF CleanTest app can still install and use `AgentBlazor` without pulling EF.

## Assumptions

- Package name: `AgentBlazor.EntityFrameworkCore`.
- v0.2 scope is schema metadata only.
- Query execution, entity result canvases, row-level policy helpers, and component-generated tables are v0.3+ work.
- Main package must remain usable by non-EF apps.
