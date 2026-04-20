# Pro Tier Operations Guide

Last updated: 2026-04-20

Use this guide when enabling AgentBlazor Pro features in a real Blazor app.

## What Pro Enables

`UseProLicense(licenseKey, dataDirectory?)` enables durable paid services:

- `SqliteActionHistoryStore` for action history, execution metrics, user/session lookup, and route/action patterns.
- `SqliteAgentInspectorStore` for persisted inspector runs and execution plans.
- `SqliteUsageAnalyticsService` for summaries, top actions, agent performance, trends, and anomaly detection.
- `SqliteAuditLogService` for queryable compliance events and CSV/JSON export.
- `SqliteSmartSuggestionService` for pattern-based and route-popularity suggestions, with optional LLM fallback.

The current Pro story is "the app learns from real use." It is not a license gate around basic component control.

## Enable Pro

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-5.4-mini");

    options.UseProLicense(
        licenseKey: builder.Configuration["AgentBlazor:LicenseKey"]!,
        dataDirectory: builder.Configuration["AgentBlazor:DataDirectory"]);
});
```

If `dataDirectory` is omitted, SQLite files are created in the app's current working directory. For production pilots, configure an explicit app-owned directory.

Expected files:

- `agentblazor-history.db`
- `agentblazor-inspector.db`
- `agentblazor-audit.db`

## Operational Expectations

- Use one writable data directory per deployed app instance.
- Back up the three SQLite files if action history, audit, or inspector data must survive host loss.
- Keep the data directory outside ephemeral deployment folders that are replaced on each release.
- Protect the data directory with the same access controls as app logs because prompts, user ids, action ids, error messages, routes, and audit metadata can be sensitive.
- Run retention cleanup as an app maintenance task if the app has high action volume. The SQLite stores expose prune methods for action history, inspector runs, and audit logs.
- Treat the Pro dashboard as an operator/admin surface. Do not expose it to normal end users without authorization.

## Validation Status

Automated validation now covers:

- Pro license tier selection and invalid license-key failure modes.
- `UseProLicense()` replacing free no-op services with SQLite-backed paid services.
- Persistence across service-provider restarts.
- Concurrent multi-user writes across action history, audit log, inspector runs, analytics, and smart suggestions using one Pro data directory.
- Pro dashboard rendering persisted overview, audit, and pattern data.

Current test anchors:

- `UseProLicense_PersistsPaidDataAcrossServiceProviderRestart`
- `UseProLicense_HandlesConcurrentMultiUserPaidStorage`
- `AgentProDashboardTests.Render_ShowsPersistedPaidDataAcrossOverviewAuditAndPatternsTabs`

## Current Limits

- SQLite-backed Pro storage is validated for a single app instance using one local data directory. It is not a distributed multi-node storage layer.
- There is no hosted license-validation service yet. Current license checks validate key shape and configure local tier/services.
- Tenant isolation is app-owned. Include tenant/user identifiers in your app's user ids, routes, or metadata when you need tenant-scoped reporting.
- Upgrade and downgrade are configuration changes: enabling Pro creates/uses durable SQLite services; removing Pro returns to free no-op services and leaves existing SQLite files untouched.
- The Pro features are suitable for controlled production pilots, not broad unsupported production claims, until at least one real app owner validates retention, authorization, backups, dashboard access, and rollback in their environment.

## Pilot Checklist

1. Configure `AgentBlazor:LicenseKey` through the app's normal secret store.
2. Configure `AgentBlazor:DataDirectory` to a persistent writable folder.
3. Verify the app process can create and write the three SQLite files.
4. Restrict dashboard routes to authorized operators/admins.
5. Run the app through representative users and sessions.
6. Confirm action history, audit export, analytics, and suggestions reflect those users without mixing identities unexpectedly.
7. Restart the app and confirm data persists.
8. Test rollback by disabling Pro and confirming the app still runs with free no-op services.
9. Decide backup and retention settings before storing production-sensitive audit data.
