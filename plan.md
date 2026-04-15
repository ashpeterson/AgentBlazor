# AgentBlazor Plan (Living Document)

Last updated: 2026-04-15
Owner: AgentBlazor core team
Status: Active working plan

## Current Status Snapshot

- Adapter-first runtime is the shipped default path.
- Semantic capability workflows are the primary authoring and demo surface.
- Existing-app CLI onboarding now supports `init`, `scaffold`, `doctor`, and `validate`.
- Existing-app scaffold now avoids global `@using MudBlazor` imports and scopes MudBlazor to the patched layout provider file to avoid QuickGrid component tag collisions.
- Blazor Web Apps with companion WebAssembly client projects are detected; server host edits remain automatic, while client layout/chat edits are review-first until a browser-safe AgentBlazor client path exists.
- External hosted WebAssembly validation now confirms that split on a real server+client OSS app; server host scaffold/build/manifest validation passes while browser-client providers/chat stay manual-review.
- Existing-app scaffold now handles composed service-chain startup paths such as `.AddServerUI(...)`, avoids duplicate MudBlazor service registration, maps endpoints before async `RunAsync`, targets discovered existing root pages, and preserves UTF-8 BOMs on edited existing files.
- Project-file scaffold now uses minimal package/project reference insertion instead of reserializing `.csproj` files, preserving XML declarations and MSBuild target expressions.
- CLI scaffold package references and CLI display version now derive from assembly package metadata and align to `0.1.0-preview.5` for the current build instead of the stale `1.0.0` value.
- Private-preview GitHub Packages publishing covers both the runtime package and CLI tool package; `0.1.0-preview.5` is the current validation package after real-app runtime smoke exposed dependency float in `0.1.0-preview.3` and the `0.1.0-preview.4` feed version proved stale/immutable.
- The 2026-04-09 runtime review fixes are in place:
  - execution scope is preserved across turns
  - middleware runs in both normal and streaming turns
  - OpenAI-compatible endpoint validation rejects non-HTTP(S) URIs
- Current non-demo verification:
  - `AgentBlazor.Core.Tests`: `261/261`
  - `AgentBlazor.Components.Tests`: `98/99`, `1` skipped
  - `AgentBlazor.Cli.Analysis.Tests`: `131/131`
  - `AgentBlazor.Cli.IntegrationTests`: `9/9`
  - `AgentBlazor.IntegrationTests`: `105/105`
- Current real-provider validation:
  - `ProviderAdapterIntegrationTests`: `30/30` with real OpenAI provider config from `demo/AgentBlazor.Demo/appsettings.Development.json`; coverage includes chat response, semantic capability invocation, approval gating, blocked/recovery/retry, streaming/reconnect, cancellation, concurrency, and session-state continuity
- Current package validation:
  - GitHub Packages workflow `publish-github-packages-preview` run `24420548131` passed on commit `193ccdfc92f2b6e618b7dafa6e6228cfe2597171` for `0.1.0-preview.3`
  - local package smoke for `0.1.0-preview.4` passed clean-app install, CLI install, scaffold, restore, build, `doctor` `9/9`, `validate` `3/3`, and runtime HTTP smoke
  - real-app package validation against `CleanArchitectureWithBlazorServer` exposed dependency float in `0.1.0-preview.3`; current source pins Microsoft Agents dependencies exactly
  - clean Blazor Web App install from `https://nuget.pkg.github.com/ashpeterson/index.json` passed `AgentBlazor` package install, `AgentBlazor.Cli` tool install, `agentblazor --version`, `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, `dotnet build`, `doctor` `9/9`, and `validate` `3/3` with no repo-local package feed
  - runtime HTTP smoke from the published package passed with a placeholder `OpenAI__ApiKey`, rendering the home page, `AgentChatWidget`, and AgentBlazor/MudBlazor static assets
  - earlier GitHub Packages `0.1.0-preview.2` runtime package validation found a stale immutable package, `0.1.0-preview.3` later exposed a real-app dependency-range issue, and `0.1.0-preview.4` was an older immutable feed package; use `0.1.0-preview.5` or later for private-preview testing
  - packed `AgentBlazor.0.1.0-preview.2.nupkg`, `AgentBlazor.Cli.0.1.0-preview.2.nupkg`, and internal dependency packages
  - clean Blazor Web App install from local package source passed CLI tool install, `agentblazor --version`, `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, `dotnet build`, `doctor` `9/9`, and `validate` `3/3` with no repo-local project references
  - previous `0.1.0-preview.1` packaged runtime smoke runner referenced `AgentBlazor` only and completed real OpenAI-backed normal and streaming semantic workflow calls with `PACKAGE_SMOKE_OK`
- Current real-app CLI validation:
  - fresh standard Blazor Web App: build passed, `doctor` `9/9`, `validate` `3/3`
  - official Microsoft `dotnet/blazor-samples/10.0/BlazorSample_BlazorWebApp`: baseline build passed, scaffold/build passed, `doctor` `9/9`, `validate` `3/3`
  - independent real-world OSS `neozhu/CleanArchitectureWithBlazorServer` at `4ef0b7c599be97d93049028e7b9a641f237cc5c7`: baseline restore/build passed, scaffold approve/build passed, `doctor` `9/9`, `validate` `3/3`
  - independent real-world OSS `oqtane/oqtane.framework` at `6299412fa5806169e7d93c4a3e43e0467a28688b`: baseline restore/build passed, review-first safe scaffold/build passed, `doctor`/`validate` correctly retained Oqtane manual-review items
  - independent real-world hosted WebAssembly OSS `sandrohanea/whisper.net` at `6fb7ba7706ccfdbe1f54b6b6ff96302593e52505`: baseline build passed after `dotnet workload restore` installed `wasm-tools`, scaffold approve/build passed, `doctor` `7/9`, `validate` `3/5` with expected WebAssembly client provider/chat manual-review warnings
  - hosted WebAssembly candidate `davidfowl/TodoApp` at `307a1eadbbd77a3004c318f2377e4818bc400af6` was environment-blocked by `global.json` pinning SDK `9.0.100`; this validation machine only has SDK `10.0.104`
  - fresh Blazor Web App with WebAssembly interactivity: server host scaffold/build passed; client layout/chat warnings remain manual-review

## Production Readiness Plan

Current readiness: private preview / published-feed validated.

1. Validate against real apps:
   - standard Blazor Web App - fresh app and official Microsoft sample passed
   - hosted WebAssembly server+client app - passed with `sandrohanea/whisper.net`; client UI remains intentionally review-first
   - legacy/custom host that should remain review-first - passed with `oqtane.framework`
   - independent real-world OSS larger app with auth, custom layouts, and multiple projects - passed with `CleanArchitectureWithBlazorServer`
   - independent real-world OSS materially different host shape - passed with `oqtane.framework`
   - capture repository URL, commit SHA, baseline restore/build, scaffold diff, approve/build, doctor, validate, and manual edits
2. Verify provider-backed execution:
   - passed with OpenAI configuration from app settings
   - passed semantic capability call
   - passed approval-gated action
   - passed blocked/recovery path
   - passed streaming chat turn
3. Verify packaging:
   - passed local preview package pack/install/build/doctor/validate/runtime smoke
   - GitHub Packages workflow now publishes both `AgentBlazor` and `AgentBlazor.Cli`
   - published `0.1.0-preview.3`, then found real-app dependency float
   - publish and validate `0.1.0-preview.5`
   - remove repo-local source assumptions from public quickstart paths
4. Verify release surface:
   - CLI `init -> scaffold --diff -> scaffold --approve -> doctor -> validate`
   - demo/e2e if public site is included in release
   - docs state supported and unsupported host shapes clearly
5. Production pilot:
   - one known real app
   - explicit owner/support channel
   - rollback plan
   - no untriaged non-demo test failures

Broad production release should wait until these gates are complete.

## 1. Project Intent

Build a sellable, production-grade **agentic UX layer for ASP.NET Core Blazor (.NET 10) on top of MudBlazor**.

Business model target:
- Free tier
- Paid tier
- Premium tier

Primary outcomes:
- Excellent developer onboarding (MudBlazor-style)
- Out-of-the-box agentic UX over MudBlazor components
- Extensible agent model for customer-specific workflows

## 2. Core Product Clarification (Important)

AgentBlazor must ship with a **first-party built-in agent** that already understands MudBlazor component capabilities plus AgentBlazor chat/agent capabilities.

Meaning:
- Customers do **not** need to build their own agent to use chat-driven UI control.
- The built-in chat UI components should work immediately with this built-in agent.
- Customers can optionally register additional/custom agents for domain-specific behavior.
- Customers should be able to pass component/capability metadata (or whitelist/blacklist) to shape what agents can do.

This built-in capability is a product requirement, not an optional add-on.

## 3. Product Principles

1. Default-first: "Install + register + use component" should work with no custom agent code.
2. Protocol-aligned: AG-UI events/messages/state are first-class.
3. Framework-native: Use Microsoft Agent Framework patterns and types.
4. Extensible safely: Customers can add agents/tools/capabilities without breaking defaults.
5. Commercial-ready: Feature partitioning, telemetry, and governance from early phases.
6. MudBlazor-first: extend MudBlazor for agentic control instead of rebuilding general-purpose UI primitives.

## 4. Initial Scope

In scope:
- Blazor integration package(s) built on top of MudBlazor
- Service registration API
- Built-in component-aware agent
- Custom agent registration
- Core chat/agent UI surfaces and MudBlazor interaction adapters
- Tool/event/state rendering primitives
- Demo site as both test bed and public showcase

Out of scope for first release:
- Full marketplace/ecosystem
- Rebuilding broad general-purpose UI components that MudBlazor already provides (tables, forms, dialogs, layout)
- Full voice stack before text/tool/state loop is stable

## 5. Proposed Solution Structure

```text
src/
  AgentBlazor.Core/                 # options, registries, capability models, orchestration contracts
  AgentBlazor.Hosting/              # server-side AG-UI hosting helpers and endpoint wiring
  AgentBlazor.Components/           # Razor Class Library (chat/agent UI + MudBlazor integration surfaces)
  AgentBlazor.DefaultAgent/         # built-in component-aware agent + planners/tools
  AgentBlazor.ProviderAdapters/     # OpenAI/Azure/Anthropic/Ollama adapters
  AgentBlazor.Licensing/            # tier/entitlement checks
demo/
  AgentBlazor.Demo/                 # public/demo app
tests/
  AgentBlazor.Core.Tests/
  AgentBlazor.Components.Tests/
  AgentBlazor.IntegrationTests/
docs/
  quickstart.md
  architecture.md
  pricing-tiers.md
```

## 6. Public API Direction (Draft)

### 6.1 Minimum onboarding

```csharp
using AgentBlazor.Services;
using MudBlazor.Services;

builder.Services.AddMudServices();

builder.Services.AddAgentBlazorServices(options =>
{
    options.Provider = AgentProvider.OpenAI(...); // or AzureOpenAI/Anthropic/Ollama
});
```

```razor
@using AgentBlazor
@using MudBlazor
```

```html
<script src="_content/AgentBlazor/AgentBlazor.min.js"></script>
<link href="_content/AgentBlazor/AgentBlazor.min.css" rel="stylesheet" />
```

### 6.2 Built-in default agent behavior

```csharp
builder.Services.AddAgentBlazorServices(options =>
{
    options.DefaultAgent.Enabled = true;
    options.DefaultAgent.Name = "AgentBlazor UI Agent";
    options.DefaultAgent.ComponentCatalogMode = ComponentCatalogMode.AllShippedComponents;
});
```

### 6.3 Custom extension points

```csharp
builder.Services.AgentBlazor
    .AddAgent("supplier-risk-agent", agent =>
    {
        agent.WithInstructions("Domain-specific supplier navigation and risk analysis.");
        agent.WithAllowedComponents("MudDataGrid", "MudDialog", "MudForm");
        agent.WithAllowedActions("MudDataGrid.Filter", "MudDialog.Open", "MudForm.Validate");
        agent.WithToolsFromAssembly(typeof(SupplierTools).Assembly);
    })
    .ConfigureComponentCatalog(catalog =>
    {
        catalog.Enable("MudDataGrid", "Filter", "Sort", "NavigateToRow");
        catalog.Enable("MudDialog", "Open", "Close");
        catalog.Enable("MudForm", "SetField", "Validate", "Submit");
    });
```

## 7. Agent Model

### 7.1 Built-in first-party agent

Responsibilities:
- Understand shipped MudBlazor/AgentBlazor capability schema
- Translate user intent into component-safe actions
- Emit AG-UI-friendly tool/lifecycle/state events
- Support chat-first navigation, retrieval, and action flows

### 7.2 Additional user agents

Capabilities:
- Add one or many custom agents
- Register scoped/domain tools
- Restrict allowed component actions by policy
- Compose with or override built-in default routing

### 7.3 Routing strategy (target)

Default:
- Built-in agent handles all requests unless an explicit custom route applies.

Optional:
- Rule-based delegation (intent, tags, component domain)
- Priority order and fallback chain

## 8. Component Capability System

Create a formal **Component Capability Catalog** used by both UI and agents, with MudBlazor capability profiles as first-class targets.

Each component should publish:
- Component ID
- Supported actions
- Input schema for actions
- Safety level / approval requirement
- Event/state output contracts

This enables:
- Built-in agent awareness
- Custom agent registration with explicit allowed capabilities
- Tier-based feature gating

## 9. Delivery Plan

## Phase 0 - Foundation and contracts
- Create solution skeleton
- Establish package boundaries
- Define options/contracts for registration and agent/capability catalog
- Add baseline CI build + test pipeline

Exit criteria:
- Compiles cleanly
- Public API stubs agreed
- Architecture doc committed

## Phase 1 - MudBlazor-first onboarding
- Implement `AddAgentBlazorServices(...)`
- Implement MudBlazor co-registration guidance (`AddMudServices`, `_Imports`, assets)
- Add first getting-started sample in demo

Exit criteria:
- New Blazor app can install MudBlazor + AgentBlazor and enable agentic behavior in <10 minutes

## Phase 2 - Built-in default agent (critical)
- Implement first-party component-aware agent
- Connect to at least one provider adapter (OpenAI/Azure OpenAI first)
- Ensure chat + key MudBlazor interaction scenarios work out-of-the-box without user-defined agents

Exit criteria:
- User can prompt chat UI and control sample MudBlazor components with zero custom agent code

## Phase 3 - Extensibility for customer agents
- Implement `AddAgent(...)` and catalog configuration
- Add policy model for allowed components/actions
- Support multiple agents and fallback routing

Exit criteria:
- Built-in + custom agents can coexist and route predictably

## Phase 4 - AG-UI depth
- Tool call visualization
- State snapshot/delta support
- Human-in-the-loop approval components
- Thread/run observability in UI and logs

Exit criteria:
- End-to-end lifecycle/tool/state flows visible and testable

## Phase 5 - Commercial readiness
- Tier gating framework (free/paid/premium)
- Usage telemetry and entitlement checks
- Hardened docs and demo scenarios

Exit criteria:
- Internal release candidate for first external pilot customers

## 10. Immediate Next Actions (Now)

1. Create initial .NET solution/project structure.
2. Implement `AgentBlazorOptions`, `DefaultAgentOptions`, `ComponentCapabilityCatalog`.
3. Implement `AddAgentBlazorServices(...)` with provider + default agent wiring.
4. Define MudBlazor v1 capability profile (initially: `MudTable`/`MudDataGrid`, dialogs, forms, navigation).
5. Add first demo page showing built-in agent controlling MudBlazor components.
6. Add integration tests validating policy-safe MudBlazor action execution through framework tools.
7. Track execution backlog in `docs/mudblazor-pivot-checklist.md`.

## 11. Risks and Mitigations

Risk: protocol/runtime churn in preview packages.
- Mitigation: wrap external dependencies behind internal abstractions and pin tested versions.

Risk: built-in + custom agent behavior conflicts.
- Mitigation: explicit routing precedence and deterministic fallback rules.

Risk: capability model becomes too loose.
- Mitigation: schema-first component capability contracts and validation at startup.

Risk: MudBlazor upstream API changes impact adapters/capabilities.
- Mitigation: version pinning + compatibility matrix + adapter contract tests.

Risk: monetization bolted on too late.
- Mitigation: feature flags + entitlement checks introduced before broad component expansion.

## 12. Definition of Done (for v1 private preview)

- Install MudBlazor + register AgentBlazor + chat works with built-in default agent.
- At least one custom agent can be added and routed.
- MudBlazor capability catalog can be passed/configured by consumer.
- AG-UI lifecycle/tool/state flows are visible and stable.
- Demo app documents both default and custom-agent paths.
- Core automated test suite passes in CI.

## 13. Decision Log

2026-02-18:
- D001: Product requires a built-in first-party component-aware agent by default.
- D002: Custom user agents are extension points, not prerequisites.
- D003: Component capability catalog is the shared contract between UI and agents.
- D004: Product pivots to MudBlazor-first UI strategy; AgentBlazor extends/controls MudBlazor rather than replacing it.
- D005: MudBlazor licensing basis for commercial use is MIT (verified from source license file).

## 14. Change Log

Use this section to append incremental updates each time we refine scope, architecture, or milestones.

2026-02-18:
- Completed Phase 1 onboarding baseline:
  - `AddAgentBlazorServices(...)` in place
  - RCL static assets published under `_content/AgentBlazor/...`
  - Provider components added (`AgentThemeProvider`, `AgentPopoverProvider`, `AgentDialogProvider`, `AgentSnackbarProvider`)
  - `AgentBlazorShell` component added for quick bootstrap
  - Demo app updated to reference AgentBlazor CSS/JS + shell + status page
  - Quickstart documentation updated to match implemented onboarding flow

2026-02-18:
- Phase 2 runtime baseline implemented:
  - Built-in component-aware runtime (`IAgentRuntime`) now maps prompts to component actions
  - Default no-op executor simulates component action execution from chat UI
  - `AgentChatWidget` upgraded from placeholder to interactive runtime-backed chat
  - Provider adapter hook added with OpenAI/AzureOpenAI stub adapters
  - Integration test added for OpenAI adapter path

2026-02-18:
- Phase 2 provider adapters upgraded:
  - OpenAI adapter now executes real provider calls via Microsoft Agent Framework (`AsAIAgent(...).RunAsync(...)`) when valid credentials are present
  - Azure OpenAI adapter now executes real provider calls via Microsoft Agent Framework when endpoint/deployment/key are present
  - Deterministic demo fallback retained for placeholder/missing credentials to keep demo and tests stable (later superseded by hard-rule framework-only migration)

2026-02-18:
- Hard-rule migration to framework-native runtime path:
  - Removed custom keyword/rule planning runtime
  - Added `FrameworkBackedAgentRuntime` using Microsoft Agent Framework `ChatClientAgent`
  - Runtime now exposes component actions as framework tools (`AIFunction`) and executes via tool invocation
  - Removed custom provider response adapter abstraction/path
  - Provider registrations now supply framework `IChatClient` instances directly for runtime execution

2026-02-18:
- AG-UI streaming baseline implemented on framework runtime:
  - Added `IAgentRuntimeEventStream` with AG-UI lifecycle/tool/state events (`RUN_STARTED`, `TEXT_MESSAGE_CONTENT`, `TOOL_CALL_RESULT`, `STATE_SNAPSHOT`, `RUN_FINISHED`)
  - Added hosting endpoint `POST /agentblazor/agui/run` streaming `text/event-stream`
  - Added integration test for stream lifecycle ordering: run started -> content -> run finished
  - Added specification/source reference index in `docs/spec-references.md`

2026-02-18:
- AG-UI compliance increment for Phase 4:
  - Added `STATE_DELTA` stream emission using JSON Patch operations for planned-action/execution-result state updates
  - Updated `TOOL_CALL_RESULT` payload shape to include AG-UI-aligned fields (`messageId`, `content`, `role`)
  - Added approval-gated tool execution path for `RequiresApproval` actions
  - Approval requests now emit AG-UI `CUSTOM` events with name `FUNCTION_APPROVAL_REQUEST`
  - Added integration tests for state delta emission and approval-required execution gating
  - SSE serialization now uses web/camelCase JSON options with null field suppression for AG-UI compatibility

2026-02-18:
- Hosting migration to Microsoft AG-UI framework endpoint:
  - `AddAgentBlazorHosting()` now registers framework AG-UI services (`AddAGUI`)
  - `MapAgentBlazorAgUiRun(...)` now delegates to framework `MapAGUI(...)` on a hosted `ChatClientAgent`
  - Added `AgentBlazorHostedAgentFactory` to build hosted agent instructions + component tools from registry/catalog
  - `/agentblazor/agui/run` now accepts AG-UI `RunAgentInput` request payload shape

2026-02-18:
- Hard-rule framework migration continuation:
  - Removed custom core AG-UI runtime stream contracts (`IAgentRuntimeEventStream`, `AgUiEvent*`) and their DI wiring
  - Runtime now uses framework `ChatClientAgent` only for turn execution and tool orchestration
  - Implemented `WithToolsFromAssembly(...)` execution by mapping registered assemblies to framework tools via `AIFunctionFactory.Create(MethodInfo, ...)`
  - Hosted AG-UI factory now uses the same framework tool model (component tools + registered assembly tools)
  - Approval gating now evaluates invocation context from framework tool middleware context (`agentblazor_context` / AG-UI `ag_ui_context`)

2026-02-18:
- Phase 3 policy increment (components + actions):
  - Added per-action allow-list model to agent registration (`AllowedActions`)
  - Added builder APIs for action policy configuration (`WithAllowedActions("Component.Action")` and tuple overload)
  - Runtime and hosted AG-UI agent factory now filter component actions before framework tool registration using both `AllowedComponents` and `AllowedActions`
  - Added integration coverage for action-policy enforcement and updated quickstart/architecture/spec reference docs

2026-02-18:
- Plan pivot to MudBlazor-first product direction:
  - AgentBlazor is positioned as an agent orchestration/control layer on top of MudBlazor
  - Roadmap and DoD now target MudBlazor capability integration instead of building a standalone general-purpose component library
  - Immediate next actions prioritize MudBlazor capability profiling and adapter-driven execution paths

2026-02-18:
- `AB-MUD-001` completed (MudBlazor v1 capability taxonomy):
  - Added `AgentComponentV1CapabilityProfile` (`mudblazor.v1`) with fixed v1 component/action IDs
  - Added JSON input schemas and explicit `RequiresApproval` flags for all v1 Mud actions
  - Wired profile into default shipped component catalog
  - Added core test coverage for taxonomy presence/schema/approval metadata
  - Added taxonomy versioning-rules documentation (`docs/mudblazor-capability-taxonomy.md`)

2026-02-18:
- `AB-MUD-010` completed (typed Mud executor contracts):
  - Added explicit execution contracts (`IDataGridActionExecutor`, `IDialogActionExecutor`, `IFormActionExecutor`, `INavigationActionExecutor`)
  - Added request models and default no-op implementations (`src/AgentBlazor.Core/Runtime/AgentActionExecutors.cs`)
  - Registered replaceable defaults in `AddAgentBlazorServices(...)`
  - Added tests for default registration and custom replacement behavior

2026-02-18:
- `AB-MUD-011` completed (runtime dispatch to typed Mud executors):
  - Updated `NoOpComponentActionExecutor` to route known MudBlazor v1 actions to typed executor contracts
  - Unknown action mappings now return safe actionable failures instead of silent simulated success
  - Added core tests covering known-action dispatch and unknown-action failure behavior

2026-02-18:
- `AB-MUD-012` completed (shared policy enforcement + observability):
  - Added shared policy evaluation contract (`ComponentActionPolicyEvaluation`) used by both runtime and hosted AG-UI factory
  - Kept `AllowedComponents` + `AllowedActions` filtering before framework tool registration on all Mud actions
  - Added policy-filter diagnostics for blocked actions in runtime responses/logging and hosted-agent logging/instructions
  - Added tests validating non-execution of disallowed actions with observable policy feedback

2026-02-18:
- `AB-MUD-013` completed (approval-gating alignment on framework flow):
  - Added shared approval policy helper (`ComponentActionApprovalPolicy`) for both runtime and hosted AG-UI agent execution
  - Ensured `RequiresApproval` Mud actions only execute when approval context is present (runtime `agentblazor_context` and hosted AG-UI `ag_ui_context`)
  - Added integration tests for Mud approval-required runtime execution (`MudForm.submit`) with and without approvals
  - Added hosted endpoint integration tests for `/agentblazor/agui/run` validating approval gating via framework `MapAGUI(...)` request/stream behavior

2026-02-18:
- `AB-MUD-030` completed (Mud capability execution matrix tests):
  - Expanded integration coverage for Mud approval-required actions under allowed/disallowed approval context
  - Added mixed-tool test verifying assembly tools (`WithToolsFromAssembly`) and Mud tools execute together in a single framework run
  - Kept policy-safe behavior (`AllowedComponents`/`AllowedActions`) validated within Mud capability scenarios

2026-02-18:
- `AB-MUD-031` completed (AG-UI endpoint Mud run tests):
  - Added `/agentblazor/agui/run` integration tests for Mud action execution and approval gating on hosted framework path
  - Verified framework AG-UI `text/event-stream` responses include expected lifecycle and tool-result events
  - Verified hosted approval behavior matches runtime policy outcomes for `RequiresApproval` Mud actions

2026-02-18:
- `AB-MUD-020` completed (demo MudDataGrid flow):
  - Added `/mud-grid-agent` demo page rendering MudDataGrid state controlled by agent-triggered Mud actions
  - Added demo `IDataGridActionExecutor` + state store to visibly apply `filter`, `sort`, `navigate_to_row`, and `set_page`
  - Updated demo host registration for MudBlazor (`MudBlazor` package, `AddMudServices`, Mud providers/assets) and aligned custom agent policy to MudDataGrid actions

2026-02-18:
- `AB-MUD-021` completed (demo MudDialog + MudForm flow):
  - Added `/mud-dialog-form-agent` demo page with agent-driven dialog visibility, form-field mutation, validation, and submission status rendering
  - Added demo dialog/form state adapter and typed executors (`IDialogActionExecutor`, `IFormActionExecutor`) for Mud capability actions
  - Added policy-constrained `supplier-onboarding-agent` for MudDialog/MudForm actions with approval-gated submit path preserved

2026-02-18:
- `AB-MUD-022` completed (mixed default/custom Mud routing demo):
  - Added `/mud-agent-routing` demo page to compare default route and policy-constrained custom routes over Mud capability actions
  - Added route execution panel that runs prompts against explicit route targets and surfaces planned/executed actions for deterministic comparison
  - Added integration test proving explicit custom route policy blocks disallowed Mud actions while default route allows execution
  - Updated quickstart/docs with custom-agent policy routing guidance for Mud capabilities

2026-02-18:
- `AB-MUD-032` completed (compatibility matrix + version pinning strategy):
  - Added tested compatibility matrix for .NET/MudBlazor/Agent Framework packages (`docs/compatibility-matrix.md`)
  - Introduced centralized package pinning (`Directory.Packages.props`) across solution projects
  - Enabled restore lock files and CI locked-mode validation (`Directory.Build.props`, `.github/workflows/ci.yml`)
  - Added SDK pinning with `global.json` and updated docs to reference the new strategy

2026-02-18:
- `AB-MUD-033` completed (tier packaging boundaries for Mud integration):
  - Added Mud action tier mapping (`Free`/`Paid`/`Premium`) in `AgentComponentTierBoundaries`
  - Added entitlement filtering in both framework runtime and hosted AG-UI tool registration paths
  - Added runtime/hosting tests proving paid tier blocks premium Mud actions (`MudForm.submit`) even when approval context is present
  - Added tier packaging and entitlement mapping documentation (`docs/pricing-tiers.md`)

2026-02-18:
- Added issue-by-issue implementation backlog for MudBlazor pivot:
  - `docs/mudblazor-pivot-checklist.md` defines milestone-based execution, issue IDs, dependencies, and acceptance criteria

2026-02-18:
- Wrapper + runtime context baseline increment completed:
  - Added framework-agnostic component runtime contracts and registry (`IAgentControllable`, `IAgentComponentRegistry`, `InMemoryAgentComponentRegistry`, state/action models)
  - Registered component registry in `AddAgentBlazorServices(...)` for default runtime/hosting consumption
  - Added initial MudBlazor wrapper components (`AgentDataGrid`, `AgentDialog`, `AgentSelect`, `AgentTabs`, `AgentNavMenu`) that register/unregister with runtime registry and publish capability/state metadata
  - Updated framework runtime and hosted AG-UI agent factory to include registered component snapshots in instructions/prompt context and run metadata
  - Updated demo pages to use wrappers (`/mud-grid-agent` via `AgentDataGrid`, `/mud-dialog-form-agent` via `AgentDialog`) and fixed generic `PropertyColumn` type binding under wrapper usage
  - Added/updated tests to validate DI registration and runtime inclusion of registered wrapper snapshots

2026-02-18:
- Cleanup + alignment pass completed:
  - Added shared wrapper base class to remove duplicated register/unregister lifecycle code across Mud wrapper components
  - Consolidated registered component snapshot construction into shared runtime helper used by both runtime and hosted AG-UI factory paths
  - Added capability preset API (`UseAgentCapabilityPreset`) with `V1Minimal` and `V1Full` options
  - Added tests for preset behavior/additive overrides and updated quickstart/checklist documentation

2026-02-18:
- Wrapper execution + hosted snapshot parity hardening completed:
  - Implemented real execution wiring for `AgentDataGrid` actions (`filter`, `sort`, `set_page`, `navigate_to_row`) with state mutation, callback propagation, and effective-item shaping
  - Implemented real execution wiring for `AgentSelect` actions (`open`, `close`, `set_value`) including MudSelect menu control via component reference
  - Added component-level wrapper action tests validating state/callback behavior
  - Updated AG-UI hosted mapping to use a factory-backed agent wrapper so hosted runs resolve fresh component snapshot instructions per invocation
  - Added hosted AG-UI integration test proving registered component snapshots appear in hosted instructions while approval context behavior remains intact

2026-02-18:
- Demo UI modernization pass completed:
  - Replaced legacy Bootstrap-influenced layout shell with a MudBlazor-first custom modern shell (`MainLayout`, `NavMenu`) and responsive navigation
  - Removed Bootstrap CSS import from demo `App.razor` to avoid style collisions
  - Added global design tokens/theme polish in demo `wwwroot/app.css` (typography, surfaces, gradients, spacing, focus/readability)
  - Refreshed key pages (`Home`, `AgentBlazorStatus`, `Counter`, `Weather`, `Error`, `NotFound`) to consistent MudBlazor card/table/button patterns
  - Updated Mud demo page scoped CSS for cohesive panel styling with shared surface language

2026-02-18:
- Demo theme system increment completed:
  - Added runtime-selectable visual themes in demo shell (`Aurora Blue`, `Coastline Mint`) via `MudThemeProvider` theme switching
  - Added top-bar theme selector and themed CSS token overrides (`demo-theme--aurora`, `demo-theme--coastline`)
  - Aligned navigation and shell styling to theme tokens for consistent cross-page appearance

2026-02-18:
- Phase 5 telemetry baseline completed:
  - Added telemetry contracts in core (`IAgentBlazorTelemetrySink`, `AgentBlazorRunTelemetryEvent`, run event/outcome enums, telemetry source constants)
  - Added default no-op telemetry sink registration in `AddAgentBlazorServices(...)`
  - Added runtime telemetry emission in `FrameworkBackedAgentRuntime` for run `Started`/`Finished` with provider/policy/tier context and execution counts
  - Added hosted AG-UI telemetry emission in `FactoryBackedHostedAgent` for both non-streaming and streaming run paths
  - Added tests for default telemetry sink registration plus runtime/hosted telemetry event emission coverage

2026-02-18:
- Re-review refactor/refinement pass completed:
  - Added demo telemetry sink (`DemoTelemetrySink`) and wired it as the active `IAgentBlazorTelemetrySink` in demo host registration
  - Upgraded `/agentblazor-status` page with a run telemetry table (source/agent/kind/outcome/counts/detail) and manual refresh action for observability
  - Refactored hosted telemetry event creation in `FactoryBackedHostedAgent` via shared event builder helper for cleaner, less duplicated run/cancel/failure paths
  - Updated quickstart and architecture docs to include telemetry sink extension guidance and current next-step hardening direction

2026-02-19:
- Built-in agent focus pass completed (custom demo agents removed, extensibility retained):
  - Removed demo-time custom agent registrations so demo flows run through the built-in `AgentBlazor UI Agent` only
  - Updated Mud demo pages and built-in playground copy to reflect built-in-agent-only execution path
  - Extended planned action payload to carry tool arguments and wired framework tool invocations to forward common action arguments into executors
  - Updated demo MudDataGrid executor/state to use forwarded arguments for deterministic filtering/sorting/paging/focus behavior instead of coarse toggles
  - Added integration coverage for argument forwarding from framework tool calls to executor planned actions
