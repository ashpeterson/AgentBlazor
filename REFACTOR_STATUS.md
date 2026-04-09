# AgentBlazor — Refactor Status

Last updated: 2026-04-09

This document is now mainly historical context for the earlier architecture transition.
The current product/runtime status should be read from [docs/STATUS.md](/home/ashdev/workspace/AgentBlazor/docs/STATUS.md).

## Current Verified Snapshot

- The adapter-first runtime path is the only normal runtime path.
- Execution scope now stays bound to the caller's pushed DI scope across multi-turn workflow execution.
- Middleware now executes for both normal turns and streaming turns.
- OpenAI-compatible custom endpoint validation now rejects non-HTTP(S) URI shapes.
- Repo package source mapping now restores the full non-demo test matrix locally.

Current non-demo test status:

| Test Project | Passed | Skipped | Failed |
|--------------|--------|---------|--------|
| `AgentBlazor.Core.Tests` | 261 | 0 | 0 |
| `AgentBlazor.Components.Tests` | 98 | 1 | 0 |
| `AgentBlazor.Cli.Analysis.Tests` | 126 | 0 | 0 |
| `AgentBlazor.Cli.IntegrationTests` | 9 | 0 | 0 |
| `AgentBlazor.IntegrationTests` | 104 | 0 | 0 |

## What Was Done

A complete architectural refactor of the AgentBlazor SDK. The goal was to match the simplicity of CopilotKit for .NET/Blazor: a single attribute to make any component agent-controllable, with no boilerplate JSON schema strings, no catalog registration, and no repair loops.

### Phase 1–3: Attribute System + Discovery Engine

Three new attributes replace manual `GetCapability()` / `ExecuteActionAsync()` implementations:

```csharp
[AgentAction("Filter by risk level")]
public async Task FilterByRisk(
    [AgentParam("Risk level", AllowedValues = "high,medium,low")] string level) { ... }

[AgentReadable("Suppliers currently in view")]
public IEnumerable<Supplier> CurrentView => _filtered;
```

- **`[AgentAction]`** — marks a method as callable by the agent; optional `ActionId`, `RequiresApproval`
- **`[AgentReadable]`** — marks a property to expose as component state; optional `StateKey`, `MaxItems`
- **`[AgentParam]`** — annotates action parameters with descriptions, required flag, and allowed values
- **`AgentActionDiscovery`** — reflects these attributes at runtime to build `ComponentCapability`, read state, and dispatch `ExecuteActionAsync` calls via reflection

`AgentControllableComponentBase` base methods (`GetCapability`, `GetCurrentState`, `ExecuteActionAsync`) are now **virtual with attribute-discovery defaults** — existing overrides still compile unchanged.

### Phase 4: Built-in Wrappers Rewritten

All five built-in wrappers now use `[AgentAction]` internally:

| Wrapper | Actions |
|---------|---------|
| `AgentDataGrid` | `filter`, `sort`, `go_to_page`, `select_row`, `clear_filters` |
| `AgentForm` | `set_field`, `validate`, `submit`, `reset` |
| `AgentDialog` | `open`, `close`, `confirm` |
| `AgentTabs` | `switch_tab` |
| `AgentNavMenu` | `navigate_to`, `navigate_external` |

### Phase 5: Circuit-Scoped Registry

**Problem**: `IAgentComponentRegistry` was a singleton — all Blazor Server circuits shared one registry, causing cross-user state leakage.

**Fix**:
- `AgentComponentRegistryHub` (singleton) — `ConcurrentDictionary<string, IAgentComponentRegistry>` keyed by session ID
- `CircuitAgentComponentRegistry` (scoped) — self-registers with hub on construction using a random GUID `SessionId`; removes itself on `Dispose`
- `IAgentComponentRegistry` is now **Scoped** in DI

Each `AgentChatSurface` uses its circuit's `registry.SessionId` automatically — no manual plumbing required.

### Phase 6: Simplified Planner

Replaced `StructuredActionPlanner` (with repair loop) with `AgentPlanner` (single LLM call).

**Old approach**: Two LLM calls — first to generate a plan, second to repair it if validation failed. The first call frequently produced invalid output because the prompt was unclear.

**New approach**: One LLM call returning a structured JSON response:

```json
{
  "message": "I'll filter to show high-risk suppliers.",
  "actions": [
    { "agentId": "supplier-grid", "action": "filter", "args": { "column": "RiskScore", "operator": "gte", "value": 80 } }
  ],
  "ui": [
    { "type": "summary.card", "title": "Risk Summary", "description": "3 suppliers above threshold." }
  ],
  "needsClarification": false,
  "clarificationQuestion": null
}
```

- `message` is always required — shown directly to the user
- `actions` drive component mutations
- `ui` is optional — renders generative UI blocks (cards, tables, charts, forms)
- `needsClarification` replaces the old repair loop

The `# ACTIVE COMPONENTS` prompt section is built per-request from the scoped circuit registry, listing only components that are actually mounted in the current circuit.

### Phase 7: Simplified Runtime

`DeterministicAgentRuntime` → `AgentRuntime`. Removed:
- `IIntentClassifier` (keyword-based intent classification)
- `IIntentResolver`
- `IConversationManager`
- `IUserPreferenceService`
- `IAgentBlazorEntitlementService`
- All feature-gate checks

`AgentRuntime` resolves the correct circuit registry from `AgentComponentRegistryHub` using `request.SessionId` before each plan execution.

### Phase 8: Dead Code Deleted

Removed entire subsystems that were adding complexity without value:

- `Runtime/Intent/` — keyword intent classifier (5 files)
- `Runtime/Preferences/` — user preference service
- `Runtime/Agents/ConversationManager.cs` + `IntentResolver.cs`
- `Runtime/Conversation/FeatureGatedConversationStore.cs`
- `Runtime/Components/ComponentActionArgumentNormalizer.cs`, `ArgumentResolver.cs`, `DeterministicEntityResolver.cs`
- `Runtime/Planning/StructuredActionPlanner.cs` + `DeterministicAgentRuntime.cs`
- `Runtime/Internal/InMemoryPageStructureRegistry.cs`
- All `NoOp*ActionExecutor` files and their interfaces (`IDataGridActionExecutor`, etc.)

> **Note**: `AgentBlazor.Licensing` and `AgentBlazor.DefaultAgent` projects still exist on disk but are not referenced by the core SDK. They are candidates for full removal.

### Phase 9: Service Registration Cleanup

`AgentBlazorServiceCollectionExtensions` updated — removed ~15 registrations for deleted types. Key changes:
- `IAgentComponentRegistry` → `Scoped` (was Singleton)
- `IActionPlanner` → `AgentPlanner`
- `IAgentRuntime` → `AgentRuntime`
- `IConversationStore` → `InMemoryConversationStore` directly (no more `FeatureGatedConversationStore` wrapper)

---

## Historical Refactor Snapshot

### Test Results

| Test Project | Passed | Skipped | Failed |
|--------------|--------|---------|--------|
| `AgentBlazor.Core.Tests` | 96 | 0 | 0 |
| `AgentBlazor.Components.Tests` | 10 | 1 | 0 |
| `AgentBlazor.IntegrationTests` | 27 | 9 | 0 |

The 1 skipped component test is intentional: row inference from filter context was explicitly removed (rowKey is now required). The 9 skipped integration tests are AG-UI streaming tests disabled by design.

### Source Layout (Post-Refactor)

```
src/
  AgentBlazor.Core/
    Attributes/
      AgentActionAttribute.cs       ← NEW
      AgentReadableAttribute.cs     ← NEW
      AgentParamAttribute.cs        ← NEW
    Runtime/
      Discovery/
        AgentActionDiscovery.cs     ← NEW
      Planning/
        AgentPlanner.cs             ← NEW (replaces StructuredActionPlanner)
        AgentRuntime.cs             ← NEW (replaces DeterministicAgentRuntime)
        PlanExecutor.cs
        PlanValidator.cs
      Components/
        AgentComponentRegistryHub.cs     ← NEW
        CircuitAgentComponentRegistry.cs ← NEW
        InMemoryAgentComponentRegistry.cs
        NoOpComponentActionExecutor.cs   (routes to discovery-based dispatch)
  AgentBlazor.Components/
    Wrappers/
      AgentControllableComponentBase.cs  ← virtual defaults added
      AgentDataGrid.razor                ← [AgentAction] methods
      AgentForm.razor                    ← [AgentAction] methods
      AgentDialog.razor                  ← [AgentAction] methods
      AgentTabs.razor                    ← [AgentAction] methods
      AgentNavMenu.razor                 ← [AgentAction] methods
```

### Known Non-Blocking Items

- `AgentBlazor.Licensing/` and `AgentBlazor.DefaultAgent/` packages still exist on disk — should be removed from solution and deleted
- Demo app (`demo/AgentBlazor.Demo`) agent instructions are still the long-form ~90-line version — can be shrunk to ~10 lines

---

## What's Next

### Immediate (Phase 11 — Demo Simplification)

Trim `demo/AgentBlazor.Demo/Program.cs` agent instructions from ~90 lines to ~10:

```csharp
options.AgentInstructions = """
    Help users manage supplier risk workflows.
    For charts prefer named data sources:
    - demo.suppliers.risk.by-region
    - demo.suppliers.risk.tier-distribution
    - demo.onboarding.volume.monthly
    - demo.mitigation.volume.monthly
    """;
```

Add one example page (`/examples/custom-action`) showing the `[AgentAction]` pattern for developer reference — this is the primary SDK selling point and needs a live demo.

### Clean Up Dead Projects

Remove `AgentBlazor.Licensing` and `AgentBlazor.DefaultAgent` from `AgentBlazor.sln` / `AgentBlazor.slnx` and delete their directories. They are vestigial from the pre-refactor architecture.

### Developer Experience Polish

The refactor achieved the target developer surface. The next friction point is getting from zero to working in < 5 minutes. Recommended:

1. **NuGet package** — publish a pre-release to nuget.org so the quickstart (`dotnet add package AgentBlazor`) actually works
2. **`docs/quickstart.md`** — update to reflect new attribute-based API (current version still references old `GetCapability()` pattern)
3. **README.md** — root readme is missing; add one with the one-page "install → register → `[AgentAction]`" story

### Generative UI Expansion

Current UI blocks: `summary.card`, `form.draft`, `table.view`, `chart.view`. High-value additions:
- **`progress.tracker`** — visual step tracker for multi-stage workflows
- **`approval.card`** — structured card for `RequiresApproval=true` actions with accept/reject buttons
- **`list.view`** — lightweight alternative to table for simple item lists

### AG-UI Streaming (Integration Tests)

9 integration tests covering AG-UI streaming are currently skipped. Completing streaming support would unlock real-time token streaming in the chat surface — meaningful for perceived responsiveness.

### Multi-Provider Testing

The `ProviderAdapters` project exists but test coverage against real providers (OpenAI, Azure OpenAI, Anthropic) is light. Adding a provider smoke test suite would catch prompt-format regressions early.

---

## Key Bugs Found and Fixed During Refactor

These are worth remembering for future work:

| Bug | Root Cause | Fix |
|-----|-----------|-----|
| `JsonElement` args type mismatch | `System.Text.Json` deserializes `Dictionary<string, object?>` values as `JsonElement`, not CLR types | `NormalizeArgs()` in `AgentPlanner.cs` unwraps to `string`/`int`/`bool` |
| Executor never called for component type agentIds | When LLM returns component type name (e.g. `"AgentDataGrid"`) as `agentId`, `PlanExecutor` injected it as `merged["agentId"]`, causing registry lookup to fail | Skip `TargetAgentId` injection in `PlanExecutor.BuildExecutionArguments` when `TargetAgentId == ComponentId` |
| Wrong response text on failed execution | `AgentRuntime` always used `planMessage` as response text, silencing NeedsClarification errors | Use `BuildResponseText(executionResult)` when `executionResult.Succeeded == false` |
| `AgentTabs.switch_tab` param name | Tests used `"tab_index"` but canonical parameter name is `"index"` | Changed all test mocks to use `"index"` |
