# Pricing Tiers

Last updated: 2026-04-15

This document describes the tier model, pricing strategy, and implementation status.

## Market Context

Based on 2026 market research:

| Competitor | Free | Paid | Notes |
|------------|------|------|-------|
| LangSmith | 5k traces/mo | $39/seat/mo | Python-first, no Blazor |
| Vercel v0 | $5 credits/mo | $20/mo | React-only, generative |
| Syncfusion Blazor | Community license | ~$995/year | Components only, no agent runtime |
| Telerik Blazor | Trial only | ~$999/year | Components only, no agent runtime |

AgentBlazor occupies a unique position: the only Blazor-native agentic UI framework with deterministic execution, approval workflows, and MudBlazor integration.

## Pricing Strategy

| Tier | Price | Target | Value |
|------|-------|--------|-------|
| **Free** | $0 | Solo devs, POCs | Full runtime, all components, dev tools |
| **Pro** | $29/seat/mo | Teams | Persistent intelligence, unlimited workflows |
| **Enterprise** | Custom | Large orgs | SSO, audit logs, SLA, dedicated support |

Rationale for $29/seat/mo:
- Below LangSmith ($39) for easier .NET adoption
- Above commodity pricing to signal serious product
- Per-seat aligns with enterprise .NET budgeting
- Monthly allows try-before-commit

## Tier Model

- `Free`
- `Pro` (formerly "Paid")
- `Enterprise` (formerly "Premium")

Tier primitives live in `AgentBlazor.Licensing`.

## Configuration

Recommended path:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseProLicense("AB-PRO-..."); // Paid
    // or:
    // options.UseProLicense("AB-ENT-..."); // Premium
});
```

`UseProLicense(...)` currently:

- validates the key format
- sets `AgentBlazorOptions.LicensedTier`
- swaps in paid service implementations

Dev tools are separate from paid licensing:

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseDevTools(autoShow: true);
});
```

The inspector is not a paid-only capability.

## Current Component Action Tier Map

Source of truth:

- `src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs`

### Free

All currently shipped built-in component actions are free.

That remains compatible with the current product direction after the MudBlazor compatibility work:

- drop-in component adoption should not be blocked by paid gates
- paid value should come from intelligence and persistence, not from basic UI control

This includes:

- `AgentDataGrid`
  - `filter`
  - `sort`
  - `clear_filters`
  - `select_row`
  - `navigate_to_row`
  - `go_to_page`
  - `set_page`
- `AgentDialog`
  - `open`
  - `close`
  - `confirm`
- `AgentForm`
  - `set_field`
  - `validate`
  - `reset`
  - `submit`
- `AgentNavMenu`
  - `navigate_to`
  - `navigate_external`
- `AgentTabs`
  - `switch_tab`
- `AgentSelect`
  - `open`
  - `close`
  - `set_value`
  - `clear`
- `AgentAutocomplete`
  - `set_query`
  - `select_option`
  - `clear`
- `AgentDatePicker`
  - `set_date`
  - `clear`
- `AgentDateRangePicker`
  - `set_range`
  - `clear`
- `AgentTreeView`
  - `expand`
  - `collapse`
  - `select_node`
- `AgentStepper`
  - `go_to_step`
  - `next`
  - `previous`
- `AgentCommandBar`
  - `invoke_command`
  - `list_commands`
- `AgentFileUpload`
  - `attach`
  - `remove`
  - `list_files`

## Why This Changed

The current product boundary is:

- core component interaction is part of the free platform
- paid value should come from intelligence, history, and insights

This means the repo no longer treats baseline component actions as monetized capabilities.

## What Paid Currently Enables

`UseProLicense(licenseKey, dataDirectory?)` currently swaps in:

- `IActionHistoryStore -> SqliteActionHistoryStore` (durable)
- `IAdaptiveSuggestionService -> LlmAdaptiveSuggestionService`
- `IProactiveInsightService -> LlmProactiveInsightService`
- `IAgentInspectorStore -> SqliteAgentInspectorStore` (durable)

Data is persisted to SQLite databases in the specified `dataDirectory` (defaults to current directory):
- `agentblazor-history.db` - Action history
- `agentblazor-inspector.db` - Inspector runs
- `agentblazor-audit.db` - Audit log

## Free Tier Service Defaults

Default free registrations are:

- `IActionHistoryStore -> NullActionHistoryStore`
- `IAdaptiveSuggestionService -> StaticSuggestionService`
- `IProactiveInsightService -> NullProactiveInsightService`
- `IAgentInspectorStore -> NullAgentInspectorStore`

Note:

- `UseDevTools()` can still replace the null inspector store for development without a paid license

## Implementation Status

### What Exists

| Service | Interface | Implementation | Status |
|---------|-----------|----------------|--------|
| Action History | `IActionHistoryStore` | `SqliteActionHistoryStore` | ✅ Durable |
| Adaptive Suggestions | `IAdaptiveSuggestionService` | `LlmAdaptiveSuggestionService` | ✅ Working |
| Proactive Insights | `IProactiveInsightService` | `LlmProactiveInsightService` | ✅ Working |
| Inspector Store | `IAgentInspectorStore` | `SqliteAgentInspectorStore` | ✅ Durable |
| Usage Analytics | `IUsageAnalyticsService` | `SqliteUsageAnalyticsService` | ✅ NEW |
| Audit Log | `IAuditLogService` | `SqliteAuditLogService` | ✅ NEW |
| Smart Suggestions | `ISmartSuggestionService` | `SqliteSmartSuggestionService` | ✅ NEW |

### Completed

- `SqliteActionHistoryStore` - Durable action history with user/session indexing, pattern aggregation, and execution metrics
- `SqliteAgentInspectorStore` - Durable inspector runs with execution plan storage
- `SqliteUsageAnalyticsService` - Analytics queries over action history (summary, trends, anomalies)
- `SqliteAuditLogService` - Compliance-ready audit trail with CSV/JSON export
- `SqliteSmartSuggestionService` - Pattern-based suggestions with sequence analysis and LLM fallback
- `UseProLicense()` wiring with configurable data directory

### Future Enhancements

| Item | Priority |
|------|----------|
| License key server validation | Optional |
| User profile intelligence aggregation | Medium |
| Cross-session personalization | Medium |

### Production Ready

The paid tier now delivers durable "app learns over time" functionality. Action history, inspector data, and audit data persist to SQLite databases across app restarts.

Automated validation now covers concurrent multi-user paid storage through the real `UseProLicense()` SQLite service graph:

- 4 users
- 12 sessions
- 48 action-history records
- matching audit events
- inspector runs
- usage analytics and agent performance
- route suggestions and sequence-pattern suggestions

Operational guidance lives in `docs/pro-tier-operations.md`.

## Enforcement Status

Tier enforcement is still real even though current component actions are free.

The runtime still:

- computes allowed actions based on policy and tier
- blocks actions deterministically when required
- returns user-readable diagnostics for blocked actions
- surfaces the same blocked outcomes through both standard runtime and AG-UI paths

At the moment, that enforcement matters more for future paid intelligence/services than for the currently shipped component actions.

## Recommended Product Boundary

The current repository supports this product direction best:

### Free

- deterministic runtime
- built-in agentic components
- Blazor-native chat surfaces
- AG-UI hosting
- workflow hub and agentic component demos
- dev tools / inspector for development

Sell it as:

- "make your Blazor app agent-capable"
- "ship the workflow layer without waiting for enterprise procurement"
- "prove value with live orchestration and deterministic execution first"
- "start free, wire one agent, and show the workflow in one sprint"

### Paid

- action history-backed intelligence
- adaptive suggestions
- proactive insights
- future durable user-behavior personalization

Sell it as:

- "the product gets smarter with use"
- "operators see the right next step sooner"
- "workflow guidance improves instead of staying static"
- "free gets the workflow live; paid makes the workflow compound"

### Premium

- reserved for deeper governance, analytics, or enterprise controls once those are real product features

Sell it as:

- "team-grade workflow governance"
- "audit and oversight for agent-driven operations"
- "analytics and policy depth for large deployments"

## Demo Funnel Note

The current product funnel should stay simple:

- `/` sells the story fast
- `/demo` makes the free path feel deployable
- orchestration routes prove the workflow outcome
- `Paid` is introduced as compounding intelligence, not feature withholding

## Summary

Current truth:

- Free tier is private-preview ready and package-validated, not yet broad-production ready
- Pro tier has durable SQLite persistence, dashboard surface, and automated multi-user storage validation, but still needs a controlled production pilot before broad production claims
- All component actions are free (correct product boundary)
- Paid differentiation is intelligence-driven, not feature-gated

Go-to-market readiness:

- Free: Ready for controlled private-preview validation from GitHub Packages
- Pro ($29/seat/mo): Feature-complete preview with automated multi-user storage validation, not production-piloted
- Enterprise: Future tier after SSO, deeper governance, and operational support are real
