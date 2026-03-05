# Pricing Tiers (Current Wiring)

Last updated: 2026-03-05

This document describes the current tier model and what is wired in code today.

## Tier Model

- `Free`
- `Paid`
- `Premium`

Tier primitives live in `AgentBlazor.Licensing/AgentBlazorTier.cs`.

## How Tiers Are Configured

Primary path (recommended):

```csharp
builder.Services.AddAgentBlazor(options =>
{
    options.UseProLicense("AB-PRO-..."); // Paid
    // or: options.UseProLicense("AB-ENT-..."); // Premium
});
```

`UseProLicense(...)` currently:

- Validates key format (`AB-PRO-` or `AB-ENT-`, minimum length 24)
- Sets `AgentBlazorOptions.LicensedTier`
- Replaces free defaults with paid service implementations

Legacy/hosted compatibility path also exists:

```csharp
builder.Services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);
```

## Component Action Tier Map (Source of Truth)

Defined in `src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs`.

| Feature key | Component actions | Required tier |
|---|---|---|
| `agentblazor.components.datagrid.basic` | `AgentDataGrid.filter`, `AgentDataGrid.clear_filters`, `AgentDataGrid.sort` | `Free` |
| `agentblazor.components.datagrid.advanced` | `AgentDataGrid.select_row`, `AgentDataGrid.navigate_to_row`, `AgentDataGrid.go_to_page`, `AgentDataGrid.set_page` | `Paid` |
| `agentblazor.components.dialog.flow` | `AgentDialog.open`, `AgentDialog.close`, `AgentDialog.confirm` | `Free` |
| `agentblazor.components.form.assist` | `AgentForm.set_field`, `AgentForm.validate`, `AgentForm.reset` | `Free` |
| `agentblazor.components.form.submission` | `AgentForm.submit` | `Premium` |
| `agentblazor.components.navigation.internal` | `AgentNavMenu.navigate_to` | `Free` |
| `agentblazor.components.navigation.external` | `AgentNavMenu.navigate_external` | `Premium` |
| `agentblazor.components.tabs.navigation` | `AgentTabs.switch_tab` | `Free` |
| `agentblazor.components.select.basic` | `AgentSelect.open`, `AgentSelect.close`, `AgentSelect.set_value`, `AgentSelect.clear` | `Free` |
| `agentblazor.components.autocomplete.basic` | `AgentAutocomplete.set_query`, `AgentAutocomplete.select_option`, `AgentAutocomplete.clear` | `Free` |
| `agentblazor.components.datepicker.basic` | `AgentDatePicker.set_date`, `AgentDatePicker.clear` | `Free` |
| `agentblazor.components.daterangepicker.basic` | `AgentDateRangePicker.set_range`, `AgentDateRangePicker.clear` | `Free` |
| `agentblazor.components.treeview.basic` | `AgentTreeView.expand`, `AgentTreeView.collapse`, `AgentTreeView.select_node` | `Free` |
| `agentblazor.components.stepper.basic` | `AgentStepper.go_to_step`, `AgentStepper.next`, `AgentStepper.previous` | `Free` |
| `agentblazor.components.commandbar.basic` | `AgentCommandBar.invoke_command`, `AgentCommandBar.list_commands` | `Free` |
| `agentblazor.components.fileupload.basic` | `AgentFileUpload.attach`, `AgentFileUpload.remove`, `AgentFileUpload.list_files` | `Free` |

## What Changes by Tier Today

### Free (default)

- `IActionHistoryStore` -> `NullActionHistoryStore`
- `IAdaptiveSuggestionService` -> `StaticSuggestionService`
- `IProactiveInsightService` -> `NullProactiveInsightService`
- `IAgentInspectorStore` -> `NullAgentInspectorStore`
- Runtime extension hooks via `IAgentRuntimeEventSubscriber`
- Conversation persistence options (`UseConversationStore<TStore>()`, `UseJsonFileConversationStore(...)`)

### Paid / Premium (via `UseProLicense`)

- `IActionHistoryStore` -> `InMemoryActionHistoryStore`
- `IAdaptiveSuggestionService` -> `LlmAdaptiveSuggestionService`
- `IProactiveInsightService` -> `LlmProactiveInsightService`
- `IAgentInspectorStore` -> `InMemoryAgentInspectorStore`

## Enforcement Status

Action-to-tier boundaries are now hard-enforced at runtime:

- agent policy + tier filters are applied before planning when building allowed component actions
- blocked outcomes return deterministic user-readable diagnostics with current tier context
- validation also emits explicit tier-required diagnostics when a planned action is above current entitlement
- AG-UI hosted and standard runtime paths both surface the same blocked-action behavior

Service activation differences by tier (history/suggestions/inspector/insights) still apply in addition to action gating.

## Packaging Summary

- `Free`: Core runtime + wrapper actions + basic chat UX
- `Paid`: Adds adaptive suggestions and action history-backed intelligence
- `Premium`: Adds highest tier for boundaries intended for sensitive actions (for example form submission/external navigation) plus full paid service stack
