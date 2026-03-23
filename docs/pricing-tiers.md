# Pricing Tiers

Last updated: 2026-03-20

This document describes the current tier model and the current reality of what is wired in code.

Current product framing note:

- the workflow hub and orchestration routes are now the main product proof
- paid differentiation should continue to support intelligence and workflow guidance, not basic component control

Current conversion strategy:

- `Free` must feel complete enough to ship
- `Paid` must feel like the obvious upgrade once teams want the app to learn, guide, and prompt operators
- `Premium` should be the team and governance layer, not a renamed paid tier
- landing, README, and `/demo` should all make this upgrade path visible in under 30 seconds

## Tier Model

- `Free`
- `Paid`
- `Premium`

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

## Important Limitation

The current paid story is only partially complete.

What exists:

- action history abstraction
- adaptive suggestion service
- proactive insight service

What is still missing:

- durable persistent action history
- mature cross-session user personalization
- a finished "persistent user usage for intelligent suggestions" product story

Today the main limitation is that the shipped paid action history implementation is still in-memory, so it does not yet represent durable long-term user intelligence.

This is still the largest product-level gap in the repository today.

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

- the tier model exists
- the component-action surface is now free
- paid differentiation is intended to be intelligence-driven
- the durable persistent intelligence story has started, but is not finished yet
- the component compatibility push has reduced adoption risk on the UI side, so the biggest outstanding monetization risk is now the incomplete persistence/intelligence story rather than component gating
