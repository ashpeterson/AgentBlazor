# Pricing Tiers (MudBlazor Integration)

Last updated: 2026-02-18

This document defines `AB-MUD-033` tier boundaries for MudBlazor integration features and maps them to runtime entitlement checks.

## Tier Model

- `Free`
- `Paid`
- `Premium`

Entitlements are configured through:

```csharp
using AgentBlazor.Licensing;

builder.Services.AddAgentBlazorLicensing(AgentBlazorTier.Paid);
```

## Mud Feature Map

| Feature key | Mud actions (`Component.Action`) | Required tier |
|---|---|---|
| `agentblazor.components.datagrid.basic` | `AgentDataGrid.filter`, `AgentDataGrid.clear_filters`, `AgentDataGrid.sort` | `Free` |
| `agentblazor.components.datagrid.advanced` | `AgentDataGrid.select_row`, `AgentDataGrid.navigate_to_row`, `AgentDataGrid.go_to_page`, `AgentDataGrid.set_page` | `Paid` |
| `agentblazor.components.dialog.flow` | `AgentDialog.open`, `AgentDialog.close`, `AgentDialog.confirm` | `Free` |
| `agentblazor.components.form.assist` | `AgentForm.set_field`, `AgentForm.validate`, `AgentForm.reset` | `Free` |
| `agentblazor.components.form.submission` | `AgentForm.submit` | `Premium` |
| `agentblazor.components.navigation.internal` | `AgentNavMenu.navigate_to` | `Free` |
| `agentblazor.components.navigation.external` | `AgentNavMenu.navigate_external` | `Premium` |
| `agentblazor.components.tabs.navigation` | `AgentTabs.switch_tab` | `Free` |

Source of truth in code: `src/AgentBlazor.Core/Components/AgentComponentTierBoundaries.cs`.

## Entitlement Enforcement Path

Checks are applied after agent policy (`AllowedComponents`/`AllowedActions`) and before framework tool registration:

- Runtime path: `src/AgentBlazor.Core/Runtime/FrameworkBackedAgentRuntime.cs`
- Hosted AG-UI path: `src/AgentBlazor.Hosting/AgentBlazorHostedAgentFactory.cs`
- Shared filtering helper: `src/AgentBlazor.Core/Components/ComponentActionPolicy.cs`

If a tier blocks all allowed actions, runtime/hosting return policy-tier diagnostics and skip action execution.

## Packaging Boundary Summary

- `Free`: baseline Mud chat-driven workflows for common grid/dialog/form/navigation actions.
- `Paid`: advanced grid control (`select_row`, `navigate_to_row`, `go_to_page`, `set_page`).
- `Premium`: sensitive/high-impact actions (`AgentForm.submit`, external navigation).

## Source Alignment

- AG-UI protocol docs: https://docs.ag-ui.com/
- AG-UI source referenced by project: `C:\Git\repos\ag-ui`
- Microsoft Agent Framework docs: https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp
- Microsoft Agent Framework source referenced by project: `C:\Git\Grouptree\agent-framework`
- MudBlazor source: `C:\Git\repos\MudBlazor`
- MudBlazor MIT license: `C:\Git\repos\MudBlazor\LICENSE`
