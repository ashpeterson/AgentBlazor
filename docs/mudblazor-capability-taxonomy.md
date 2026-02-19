# MudBlazor Capability Taxonomy (v1)

Last updated: 2026-02-18

## Purpose

Define the stable AgentBlazor capability contract for MudBlazor integration in v1.

Runtime source:
- `src/AgentBlazor.Core/Components/AgentComponentV1CapabilityProfile.cs`

Profile identifier:
- `agentblazor.components.v1`

## Component IDs (v1 fixed set)

- `AgentDataGrid`
- `AgentDialog`
- `AgentForm`
- `AgentNavMenu`
- `AgentTabs`

## Actions (v1)

### `AgentDataGrid`
- `filter` (approval: `false`)
- `clear_filters` (approval: `false`)
- `sort` (approval: `false`)
- `select_row` (approval: `false`)
- `go_to_page` (approval: `false`)
- `navigate_to_row` (approval: `false`)
- `set_page` (approval: `false`)

### `AgentDialog`
- `open` (approval: `false`)
- `close` (approval: `false`)
- `confirm` (approval: `true`)

### `AgentForm`
- `set_field` (approval: `false`)
- `validate` (approval: `false`)
- `reset` (approval: `false`)
- `submit` (approval: `true`)

### `AgentNavMenu`
- `navigate_to` (approval: `false`)
- `navigate_external` (approval: `true`)

### `AgentTabs`
- `switch_tab` (approval: `false`)

All v1 actions include a JSON input schema (`InputSchema`) and explicit approval metadata (`RequiresApproval`).

## Versioning Rules

1. `agentblazor.components.v1` component IDs and existing action IDs are immutable.
2. v1-compatible changes are additive only:
   - New optional properties in existing action schemas.
   - New actions on existing v1 components.
   - New v1 components only if they do not change existing semantics.
3. Breaking changes require a new profile major version:
   - Renaming/removing component IDs.
   - Renaming/removing actions.
   - Tightening schema requirements in a way that invalidates prior payloads.
4. Runtime defaults should keep prior profile behavior stable once shipped.

## Notes

- Capability policy controls remain `AllowedComponents` + `AllowedActions`.
- Approval behavior is enforced by framework tool invocation context in runtime and hosted AG-UI paths.
