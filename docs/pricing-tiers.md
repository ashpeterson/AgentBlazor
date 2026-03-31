# Pricing Tiers

Last updated: 2026-03-31

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

`UseProLicense(...)` currently swaps in:

- `IActionHistoryStore -> InMemoryActionHistoryStore`
- `IAdaptiveSuggestionService -> LlmAdaptiveSuggestionService`
- `IProactiveInsightService -> LlmProactiveInsightService`
- `IAgentInspectorStore -> InMemoryAgentInspectorStore`

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
| Action History | `IActionHistoryStore` | `InMemoryActionHistoryStore` | In-memory only |
| Adaptive Suggestions | `IAdaptiveSuggestionService` | `LlmAdaptiveSuggestionService` | Working |
| Proactive Insights | `IProactiveInsightService` | `LlmProactiveInsightService` | Working |
| Inspector Store | `IAgentInspectorStore` | `InMemoryAgentInspectorStore` | In-memory only |

### Production Roadmap

| Priority | Task | Status | Effort |
|----------|------|--------|--------|
| 1 | `SqliteActionHistoryStore` | Planned | 2-3 days |
| 2 | `SqliteAgentInspectorStore` | Planned | 1 day |
| 3 | License key server validation | Planned | 1 day |
| 4 | User profile intelligence aggregation | Planned | 2-3 days |

### Critical Gap

The paid value story ("app learns over time") requires durable persistence. Current in-memory stores reset on app restart, breaking the core paid proposition.

Once `SqliteActionHistoryStore` ships, the paid tier delivers real value.

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

- Free tier is production-ready and shippable
- Pro tier infrastructure exists but persistence is incomplete
- All component actions are free (correct product boundary)
- Paid differentiation is intelligence-driven, not feature-gated
- Remaining work: ~1 week to complete durable persistence

Go-to-market readiness:

- Free: Ready to ship to NuGet
- Pro ($29/seat/mo): Ready after SqliteActionHistoryStore
- Enterprise: Ready after SSO/audit log implementation
