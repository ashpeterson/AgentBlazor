# Generative UI Spec v0

Last updated: 2026-02-24

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
- `AgentUiActionInvocation`

Block kinds:
- `Card`
- `Form`
- `Table`

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

For planner-generated UI documents in chat flows:

```razor
<AgentChatSurface EnableGeneratedUi="true" />
```

`EnableGeneratedUi="true"` injects `agentblazor.ui.generate=true` into turn context so the planner can return `generatedUi` and runtime can forward it as `GeneratedUi`.

## Behavior

1. Incoming document is validated via `AgentUiDocument.TryValidate(...)`.
2. Form field values are tracked locally by block id and included in action payloads.
3. Every action emits `OnActionInvoked` with `AgentUiActionInvocation`.
4. When `ForwardActionsToRuntime=true` and action has `Prompt`, the component forwards to `IAgentRuntime`.

## Current Scope Limits (v0)

1. No nested layout containers yet.
2. No streaming partial UI patches yet.
3. Table row-level action bindings are not yet included (block-level actions only).

## Next Planned Extensions

1. State delta updates for incremental UI refresh.
2. Standardized action metadata for approval and interrupt/resume.
3. Richer component block types (charts, timeline, file previews).
