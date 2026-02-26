# Generative UI Spec v0

Last updated: 2026-02-25

## Goal

Define a minimal typed UI contract that agents can return and Blazor can render natively through `AgentGenerativeSurface`.

## Contract Types

Source of truth:
- `src/AgentBlazor.Core/Components/AgentGenerativeUiSpec.cs`

Primary types:
- `AgentUiDocument`
- `AgentUiBlock`
- `AgentUiAction`
- `AgentUiField`
- `AgentUiTableColumn`
- `AgentUiChartSeries`
- `AgentUiActionInvocation`

Block kinds:
- `Card`
- `Form`
- `Table`
- `Chart`

Spec version constant:
- `agentblazor.ui.v0`

## Example JSON

```json
{
  "specVersion": "agentblazor.ui.v0",
  "blocks": [
    {
      "id": "riskSummary",
      "kind": "Card",
      "title": "Highest Risk Supplier",
      "description": "Alpine Components has risk score 82.",
      "actions": [
        {
          "id": "openSupplier",
          "label": "Open Supplier",
          "prompt": "open supplier details for Alpine Components",
          "arguments": {
            "supplierName": "Alpine Components"
          }
        }
      ]
    },
    {
      "id": "onboardingForm",
      "kind": "Form",
      "title": "Supplier Onboarding",
      "fields": [
        { "name": "SupplierName", "label": "Supplier Name", "type": "text", "required": true },
        { "name": "Region", "label": "Region", "type": "text" }
      ],
      "actions": [
        {
          "id": "submitOnboarding",
          "label": "Submit",
          "prompt": "submit supplier onboarding form"
        }
      ]
    },
    {
      "id": "riskTrend",
      "kind": "Chart",
      "title": "Risk Trend",
      "chartType": "Line",
      "labels": ["Mon", "Tue", "Wed"],
      "series": [
        { "name": "RiskScore", "data": [62, 68, 71] }
      ]
    },
    {
      "id": "deliveryTrend",
      "kind": "Chart",
      "title": "Delivery Forecast",
      "chartDataSource": "app.delivery.forecast",
      "chartDataArguments": { "horizon": 6 }
    }
  ]
}
```

## Rendering in Blazor

```razor
@using AgentBlazor.Core.Components

<AgentGenerativeSurface Document="@GeneratedDocument"
                        AgentName="AgentBlazor UI Agent"
                        ForwardActionsToRuntime="true"
                        OnActionInvoked="HandleGeneratedActionAsync" />
```

For planner-generated UI in chat flows:

```razor
<AgentChatSurface EnableGeneratedUi="true" />
```

`EnableGeneratedUi="true"` injects `agentblazor.ui.generate=true` into turn context.
The planner returns deterministic `uiToolCalls` (not freeform blocks), and runtime renders those calls into `AgentUiDocument` before forwarding it as `GeneratedUi`.

## Behavior

1. Planner emits `uiToolCalls` using known tool ids from `IAgentUiToolCatalog`.
2. Runtime renders tool calls to `AgentUiDocument` and validates via `AgentUiDocument.TryValidate(...)`.
3. Form field values are tracked locally by block id and included in action payloads.
4. Every action emits `OnActionInvoked` with `AgentUiActionInvocation`.
5. When `ForwardActionsToRuntime=true` and action has `Prompt`, the component forwards to `IAgentRuntime`.
6. Chart blocks can use inline data (`chartType` + `labels` + `series`) or a named chart data source (`chartDataSource` + optional `chartDataArguments`) resolved by the host app.

## Current Scope Limits (v0)

1. No nested layout containers yet.
2. No streaming partial UI patches yet.
3. Table row-level action bindings are not yet included (block-level actions only).

## Next Planned Extensions

1. State delta updates for incremental UI refresh.
2. Standardized action metadata for approval and interrupt/resume.
3. Richer component block types (timeline, file previews).
