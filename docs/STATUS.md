# AgentBlazor Development Status

Last updated: 2026-04-23

## Production Readiness

Current label: **private preview / published-feed validated**.

The project is not yet ready for a broad, unsupported production release. It is ready for controlled private-preview validation against standard Blazor Web App hosts because the non-demo test matrix is green, the runtime review fixes are in place, the CLI now passes fresh-app, official Microsoft sample, independent real-world standard-host validation, independent hosted-WebAssembly validation, and independent custom-host review-first validation, the OpenAI-backed runtime adapter path has real workflow smoke coverage, and the current package build has local package smoke, published-feed install, and published-feed all-surface browser validation.

Production gates:

- Validate CLI scaffold/doctor/validate on at least three external Blazor apps. The standard, hosted-WebAssembly, and review-first custom-host lanes now have real-app coverage.
- Add more independent real-world OSS validation beyond templates and official samples. `CleanArchitectureWithBlazorServer` now covers the larger auth/custom-layout/multi-project path; `whisper.net` now covers hosted WebAssembly; `oqtane.framework` now covers the materially different custom/Oqtane-style review-first path.
- Real OpenAI-backed workflow validation is now covered by `ProviderAdapterIntegrationTests`: simple chat, semantic capability invocation, approval gating, blocked/recovery/retry, streaming/reconnect, cancellation, concurrency, and session-state continuity.
- Azure OpenAI is wired through the Microsoft Azure OpenAI client and the shared `IChatClient` runtime path, with API-key and `TokenCredential` registration coverage. Live Azure deployment validation remains app-owner specific.
- Published-feed package validation is complete for `AgentBlazor`, `AgentBlazor.Client`, and `AgentBlazor.Cli` version `0.1.0-preview.9`; this replaced `0.1.0-preview.3` dependency float, stale immutable `0.1.0-preview.4`, `0.1.0-preview.5` downgrade-conflict, and `0.1.0-preview.6` CSP nonce findings with a Microsoft Agents 1.1-compatible package set that passes clean-app, real-app, all-surface browser, and hosted WebAssembly remote-client validation.
- Current source and latest published-feed validated package are now `0.1.0-preview.9`. The source keeps `global.json` roll-forward set to `latestMinor` and web-app runtime framework pins set to `10.0.6` for newer .NET 10 preview SDK environments.
- Finish hosted WebAssembly CLI automation: the first browser-safe remote client package, server endpoint, fresh WebAssembly package smoke, and generated hosted-WASM browser validation now pass; CLI browser-client auto-scaffold remains review-first.
- Pro tier automated storage validation now covers concurrent multi-user usage and downgrade fallback through the real `UseProLicense()` SQLite service graph; production Pro claims still require a controlled app-owner pilot for retention, authorization, backup, dashboard access, and rollback.
- Validate the exact preview package with a small external test group before production claims.
- Document supported host shapes and review-first/unsupported host behavior.
- Run demo/e2e separately if the public demo site is part of the production release.
- Keep the public paid-tier promise scoped to durable intelligence, analytics, audit, and suggestions until a real production pilot validates operational behavior.

## Current Product Shape

AgentBlazor is now positioned as a Blazor-first agentic UI framework with two primary demo surfaces:

- `/` explains the product and routes users into the current story
- `/demo` now jumps straight into the featured live workflow
- `/demo/components` is now a supporting reference for drop-in agentic components for Blazor apps

The current story is no longer "many unrelated demo routes". It is:

1. learn the platform on the landing page
2. validate the workflow-first story in the featured response-orchestration flow
3. use the component explorer only as a supporting reference when needed

The current visible demo funnel is intentionally narrow:

- `/`
- `/docs`
- `/demo`
- `/demo/workflows/response-orchestration?reset=true`
- `/demo/workflows/release-dossier?reset=true` as a secondary proof
- `/demo/components` as a supporting reference surface

The acquisition story is now intentionally simple too:

- `Free` should look shippable in one sprint
- `Paid` should look like the app gets smarter with use
- `Premium` should read as the team/governance layer

The free-plan onboarding path is now also explicit:

- `/docs` gives the developer-facing setup path
- `samples/AgentBlazor.Starter` is the current golden-path starter
- `AddWorkflow<T>()` is the lowest-ceremony workflow registration path in code

The runtime realignment is now materially underway:

- the external runtime adapter path is the default when a chat client/provider is present
- the legacy planner/runtime bridge has now been removed from the normal product path and deleted from the codebase
- semantic capabilities are now a first-class authoring surface in code
- normalized execution, approval, policy, and context-freshness contracts now exist and are consumed by the adapter path
- the remaining plan-oriented helper models now live under `Runtime/ExecutionPlans` rather than `Runtime/Planning`
- supplier-compliance, file-audit, recipe-release, incident-escalation, response-orchestration, and release-dossier workflow validation now exist as focused integration proof, not only demo wiring
- execution-scope handling has now been corrected so adapter execution respects the caller's pushed DI scope across multi-turn workflow runs
- middleware now executes for both `RunTurnAsync` and `RunTurnStreamingAsync`
- provider endpoint validation now rejects non-HTTP(S) custom endpoints
- real OpenAI-backed adapter validation now covers runtime tool execution, approval gating, blocked/recovery/retry behavior, streaming/reconnect, cancellation, concurrency, and session-state continuity
- Azure OpenAI provider registration now covers API-key and Azure `TokenCredential` authentication paths, and CLI scaffold can emit `--provider azure-openai` startup wiring
- local and published-feed package validation now prove the `AgentBlazor` package and `AgentBlazor.Cli` tool install and run from a clean app without project references; CLI display and scaffolded package versions now derive from assembly package metadata and align to the current source/package version, now `0.1.0-preview.9`
- the private-preview GitHub Packages workflow now publishes the runtime package, WebAssembly client package, and CLI tool package; workflow run `24680658866` published and archived `0.1.0-preview.9` from commit `faaeb6842ca90f4bd4cdeea070b1a28e30886463`
- published `0.1.0-preview.9` validation confirms Microsoft Agents 1.1 compatibility, the AG-UI async session serializer, semantic workflow APIs, CSP nonce preservation for scaffolded MudBlazor/AgentBlazor assets, hosted WebAssembly remote-client chat, and SDK roll-forward/package-lock compatibility with `Microsoft.AspNetCore.App.Internal.Assets` `10.0.6` for newer .NET 10 preview SDK environments
- repo package source mapping now allows the full non-demo test matrix to restore and run locally
- existing-app scaffold now keeps MudBlazor imports scoped to the patched layout provider file instead of adding `@using MudBlazor` globally, avoiding QuickGrid `PropertyColumn` tag collisions found in the official `dotnet/blazor-samples` Blazor Web App
- modern Blazor Web Apps with companion WebAssembly client projects are detected as standard hosts with a separate UI project; server startup/shell edits are safe, while client layout/chat edits remain review-first
- external hosted WebAssembly validation now confirms this behavior on a real server+client OSS app; server host scaffold/build/manifest validation passes while browser-client layout/chat edits remain explicit manual-review work
- hosted WebAssembly readiness and scaffold output now explicitly call out that browser-client layout, asset, provider, and chat edits are review-first until a browser-safe package split or remote/server-backed client chat path is selected
- `AgentBlazor.Client` now provides browser-safe `AgentRemoteChatWidget`, `AgentRemoteChatSurface`, `AgentRemoteChatPanel`, and `AgentRemoteChatBar` components that call the server-side runtime through `MapAgentBlazorRemoteChat()`
- earlier local WebAssembly client package smoke passed in a fresh `blazorwasm` app: installed `AgentBlazor.Client` from a local feed, mounted remote widget/surface/panel/bar, and built Release with `0` warnings and `0` errors. Workdir: `/tmp/agentblazor-client-wasm-CewXpL/WasmClient`.
- generated hosted WebAssembly browser validation now passes against a fresh `dotnet new blazor --interactivity WebAssembly --all-interactive` app: packed local `AgentBlazor`, `AgentBlazor.Client`, and `AgentBlazor.Cli` `0.1.0-preview.9`; installed server/client packages from an isolated local feed; mapped `MapAgentBlazorRemoteChat()`; registered `HttpClient` in the WebAssembly client; submitted prompts through `AgentRemoteChatWidget`, `AgentRemoteChatSurface`, `AgentRemoteChatPanel`, and `AgentRemoteChatBar`; and verified widget minimize/reopen. Report: `tests/e2e/artifacts/hosted-wasm-remote-chat/2026-04-20T17-22-45-020Z/report.md`.
- `AgentRemoteChatWidget` now exposes `CssClass` and `Style` overrides so host apps can move the fixed widget away from existing bottom-right action bars, cookie banners, or support controls.
- existing-app scaffold now respects plan-specific startup edits, avoids duplicate `AddMudServices(...)` when registration is composed outside `Program.cs`, inserts AgentBlazor registration after composed service chains such as `.AddServerUI(...)`, maps endpoints before async `RunAsync`, targets discovered existing root pages, and preserves UTF-8 BOMs on edited existing files
- project-file scaffold now inserts package/project references without reserializing the whole `.csproj`, preserving XML declarations and MSBuild target expressions such as `@(Files->...)`
- project-file scaffold now detects the nearest Central Package Management `Directory.Packages.props` and emits unversioned project `PackageReference` entries plus matching `PackageVersion` entries, validated against `thecodewrapper/CH.CleanArchitectureBlazor`
- the floating `AgentChatWidget` now has a visible minimize control, Escape-to-minimize behavior, and Playwright selectors for prompt-entry/minimize/reopen coverage
- external real-app chat validation now covers all shipped chat entry points, not only the floating widget: the runner can validate `AgentChatWidget` on the app route, inject a temporary route into the cloned app, and prompt-test `AgentChatSurface`, `AgentChatPanel`, and `AgentChatBar` with deterministic or no-provider assertions
- the external chat runner now supports `AGENTBLAZOR_PACKAGE_SOURCE_MODE=published` to validate the exact package and CLI from GitHub Packages instead of the source tree, including isolated NuGet config and credential setup for the cloned app
- latest publish workflow is green in run `24680658866`; latest published-feed all-surface external chat validation is green for `0.1.0-preview.9` with report `tests/e2e/artifacts/external-chat-widget/2026-04-20T17-33-11-449Z/report.md`; latest published-feed hosted WebAssembly remote-chat validation is green with report `tests/e2e/artifacts/hosted-wasm-remote-chat/2026-04-20T17-31-59-002Z/report.md`; latest real-usability nightly is green in run `24598561465`
- paid-tier persistence now has an automated multi-user validation test, `UseProLicense_HandlesConcurrentMultiUserPaidStorage`, covering concurrent action history, audit, inspector, analytics, and smart suggestion usage with one Pro data directory

## Shipped and Working

### Core Runtime

- Deterministic runtime pipeline is shipped:
  - request context capture
  - capability and UI tool projection
  - adapter-led execution
  - normalized `ExecutionPlan`
  - policy / approval checks
  - deterministic UI execution
- Runtime policy and validation hardening is in place:
  - mounted live-component validation
  - tier-aware action filtering
  - deterministic blocked-action diagnostics
  - clarification recovery for explicit field edits
- AG-UI hosting is implemented and integrated with the runtime.
- Shared-state infrastructure is shipped:
  - `IAgentSharedStateStore`
  - in-memory default provider
  - optional JSON file persistence
  - snapshot + delta events
- Multi-agent V1/V2 foundations are shipped:
  - route lock
  - explicit handoff commands
  - approval policies
  - transfer policies
  - loop guards
- Embedded inspector is shipped and usable:
  - run timeline
  - planning/validation/execution phases
  - stream filters
  - payload lenses
  - handoff/run correlation

### Blazor UI Surface

- Built-in chat surfaces are shipped:
  - `AgentChatSurface`
  - `AgentChatPanel`
  - `AgentChatWidget`
  - `AgentChatBar`
- `AgentChatWidget` supports explicit minimize/reopen affordances and Escape-to-minimize keyboard behavior.
- `AgentChatSurface`, `AgentChatPanel`, and `AgentChatBar` now expose stable automation selectors for external browser validation, and `AgentChatBar` exposes accessible prompt/send controls.
- The floating widget open path was stabilized:
  - no fresh DOM-style open flash
  - state now transitions in a stable widget shell
- Generative UI rendering is shipped natively in Blazor through:
  - `AgentGenerativeSurface`
  - `AgentUiDocument`
  - generated card/form/table/chart blocks
- Declarative import adapters are now present:
  - `A2UI -> AgentUiDocument`
  - `Open-JSON-UI -> AgentUiDocument`

### Agentic Components

The current built-in component set is:

- `AgentDataGrid`
- `AgentDialog`
- `AgentForm`
- `AgentNavMenu`
- `AgentTabs`
- `AgentSelect`
- `AgentAutocomplete`
- `AgentDatePicker`
- `AgentDateRangePicker`
- `AgentTreeView`
- `AgentStepper`
- `AgentCommandBar`
- `AgentFileUpload`
- attribute-based custom components via `AgentControllableComponentBase`

Most high-surface MudBlazor-backed components have now been moved onto native-first implementations:

- `AgentDataGrid -> MudDataGrid`
- `AgentDialog -> MudDialog`
- `AgentForm -> MudForm`
- `AgentNavMenu -> MudNavMenu`
- `AgentTabs -> MudTabs`
- `AgentSelect -> MudSelect`
- `AgentAutocomplete -> MudAutocomplete`
- `AgentDatePicker -> MudDatePicker`
- `AgentDateRangePicker -> MudDateRangePicker`
- `AgentTreeView -> MudTreeView`
- `AgentStepper -> MudStepper`
- `AgentFileUpload -> MudFileUpload`

Baseline compatibility proof is now materially in place, not just planned:

- the components explorer exposes focused live examples for every shipped Mud-backed `Agent*` component
- rendered compatibility coverage exists in `AgentBlazor.Components.Tests`
- browser coverage exercises the public landing, workflow funnel, and focused component routes

Release posture:

- the next package should be treated as a parity-foundation preview for real-project validation
- public proof is strong enough for an initial NuGet prerelease, but not yet for claiming full complex-screen parity across every MudBlazor workflow

The components explorer is now a docs-style surface:

- top navigation bar
- left component catalog with independent scrolling
- centered component detail/live example panel
- right contents rail
- floating `AgentChatWidget` for prompts

### Demo Journey

The current demo app flow is intentionally unified:

- Home explains the platform
- Workflow Hub proves the workflow-first story
- Agentic Components shows the reusable primitives as a reference surface

This replaced the earlier fragmented "try the demo" / separate-feeling routes.

## Pricing and Tier Status

The tier model still exists:

- `Free`
- `Paid`
- `Premium`

But the component action surface has been realigned:

- core component actions are free again
- DataGrid paging/selection/navigation is free
- `AgentForm.submit` is free
- external navigation is free

Current paid differentiation is service-oriented, not component-action-oriented:

- action history
- adaptive suggestions
- proactive insights
- usage analytics
- audit logging
- smart suggestions

Current funnel intent:

- landing page and README should make the free quickstart obvious
- `/demo` should make the orchestration proof obvious
- `Paid` should be introduced as compounding workflow intelligence rather than gated primitive control

Important limitation:

- persistent cross-session user intelligence is still a preview foundation, not a mature personalization system
- paid storage now uses SQLite-backed durable services and has automated multi-user validation, but still needs production-pilot backup, retention, authorization, and rollback proof
- the currently wired suggestion path is pattern/route-based durable guidance with optional LLM fallback, not a full durable user-profile system yet

## Verification Snapshot

Latest local verification:

- `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj -nologo`
- `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj -nologo`
- `dotnet test tests/AgentBlazor.Cli.Analysis.Tests/AgentBlazor.Cli.Analysis.Tests.csproj -nologo`
- `dotnet test tests/AgentBlazor.Cli.IntegrationTests/AgentBlazor.Cli.IntegrationTests.csproj -nologo`
- `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj -nologo`

Latest test status:

- `AgentBlazor.Core.Tests`: `264/264`
- `AgentBlazor.Components.Tests`: `108/109` passed, `1` skipped
- `AgentBlazor.Cli.Analysis.Tests`: `135/135`
- `AgentBlazor.Cli.IntegrationTests`: `0/9` passed, `9` skipped
- `AgentBlazor.IntegrationTests`: `122/122`

Latest real-app CLI validation:

- Fresh standard Blazor Web App: `dotnet build` passed; `doctor` readiness `9/9`; `validate` readiness `9/9`, validation `3/3`. Workdir: `/tmp/agentblazor-prod-validation-standard-scoped-imports-20260412202054`.
- Fresh Blazor Web App with WebAssembly interactivity: server host scaffold and build passed; `doctor` readiness `7/9` with client layout/chat warnings; `validate` readiness `7/9`, validation `3/5` with manual-review warnings. Workdir: `/tmp/agentblazor-prod-validation-webapp-wasm-safe-20260412201455`.
- External official Microsoft `dotnet/blazor-samples/10.0/BlazorSample_BlazorWebApp`: baseline build passed, scaffold/build passed, `doctor` readiness `9/9`, `validate` readiness `9/9`, validation `3/3`. Workdir: `/tmp/agentblazor-external-validation-quickgridfix-20260412201933/blazor-samples/10.0/BlazorSample_BlazorWebApp`.
- Independent real-world OSS `neozhu/CleanArchitectureWithBlazorServer` at `4ef0b7c599be97d93049028e7b9a641f237cc5c7`: baseline restore/build passed; scaffold preview/approve passed after fixing composed service-chain insertion, duplicate MudBlazor registration avoidance, async `RunAsync`, existing root-page targeting, and BOM preservation; rebuild passed with upstream warnings; `doctor` readiness `9/9`; `validate` readiness `9/9`, validation `3/3`. Workdir: `/tmp/agentblazor-realworld-validation-20260412210804/CleanArchitectureWithBlazorServer`.
- Independent real-world OSS `oqtane/oqtane.framework` at `6299412fa5806169e7d93c4a3e43e0467a28688b`: baseline restore/build passed with `0` warnings and `0` errors; scaffold preview detected Oqtane-style advanced host and kept startup/shell/layout/chat review-first; scaffold approve wrote only safe package/project references plus the starter workflow file; rebuild passed with `0` warnings and `0` errors; `doctor` readiness intentionally remained `1/9` with manual-review items; `validate` manifest checks passed `3/3` with the same expected manual-review items. Workdir: `/tmp/agentblazor-oqtane-validation-20260413152042/oqtane.framework`.
- Independent real-world hosted WebAssembly OSS `sandrohanea/whisper.net` at `6fb7ba7706ccfdbe1f54b6b6ff96302593e52505`, target `examples/BlazorApp/BlazorApp/BlazorApp.csproj`: baseline build required `dotnet workload restore` to install `wasm-tools`, then restore/build passed with one upstream `ReconnectModal` Razor warning; scaffold preview/approve patched server host startup, shell assets, imports, references, and starter workflow while leaving client layout/chat as manual review; rebuild passed with the same upstream warning; `doctor` readiness `7/9`; `validate` readiness `7/9`, validation `3/5` with MudBlazor provider and chat surface manual-review warnings. Workdir: `/tmp/agentblazor-hostedwasm-validation-alt-20260413172633/whisper.net`.
- Hosted WebAssembly candidate `davidfowl/TodoApp` at `307a1eadbbd77a3004c318f2377e4818bc400af6` was skipped for scaffold validation because `global.json` pins SDK `9.0.100` and this validation machine only has .NET SDK `10.0.106`.

Latest published-feed real-app validation:

- GitHub Packages workflow `publish-github-packages-preview` run `24680658866` passed for `0.1.0-preview.9` on commit `faaeb6842ca90f4bd4cdeea070b1a28e30886463`.
- Independent external OSS `damienbod/BlazorSecurityNet10`, target `BlazorApp/BlazorApp.csproj`, installed `AgentBlazor` and `AgentBlazor.Cli` `0.1.0-preview.9` from `https://nuget.pkg.github.com/ashpeterson/index.json` using isolated NuGet config/cache/tool paths.
- Published CLI validation passed `agentblazor --version`, `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, post-scaffold `dotnet restore`, post-scaffold `dotnet build`, `doctor` readiness `9/9`, and `validate` readiness `9/9`, validation `3/3`.
- Runtime HTTP smoke passed with the rendered home page containing AgentBlazor and MudBlazor static assets, preserving the CSP nonce behavior validated in earlier `damienbod/BlazorSecurityNet10` runs.
- Published-feed all-surface browser validation passed against the same external app with Playwright prompt validation across `AgentChatWidget`, `AgentChatSurface`, `AgentChatPanel`, and `AgentChatBar` using deterministic provider responses and production-style prompts. Report: `tests/e2e/artifacts/external-chat-widget/2026-04-20T17-33-11-449Z/report.md`.
- Published-feed hosted WebAssembly remote-chat validation passed in a generated server+client Blazor Web App with `AgentBlazor`, `AgentBlazor.Client`, and `AgentBlazor.Cli` `0.1.0-preview.9`; remote widget/surface/panel/bar prompt submission and widget minimize/reopen passed. Report: `tests/e2e/artifacts/hosted-wasm-remote-chat/2026-04-20T17-31-59-002Z/report.md`.
- GitHub Packages workflow `publish-github-packages-preview` run `24441459326` passed for `0.1.0-preview.6` on commit `13336a8779c761a340472ad2f1530dd3bdb68c12`.
- Independent real-world OSS `neozhu/CleanArchitectureWithBlazorServer` at `4ef0b7c599be97d93049028e7b9a641f237cc5c7`, target `src/Server.UI/Server.UI.csproj`, installed `AgentBlazor` and `AgentBlazor.Cli` `0.1.0-preview.6` from `https://nuget.pkg.github.com/ashpeterson/index.json` using isolated NuGet cache/tool paths.
- Package inspection confirmed `AgentBlazor` `0.1.0-preview.6` came from commit `13336a8779c761a340472ad2f1530dd3bdb68c12`, depends on the Microsoft Agents 1.1 API family, contains `SerializeSessionCoreAsync`, and includes current semantic workflow APIs.
- Published CLI validation passed `agentblazor --version`, `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, post-scaffold `dotnet restore`, post-scaffold `dotnet build`, `doctor` readiness `9/9`, and `validate` readiness `9/9`, validation `3/3`.
- Runtime HTTP smoke passed after forcing `dotnet run --no-launch-profile --urls http://127.0.0.1:5197`; the rendered home page contained AgentBlazor static assets. Build retained upstream app warnings for vulnerable packages, nullable/analyzer diagnostics, MudBlazor analyzers, and SQLite RID usage, but had `0` errors.
- Validation workdir: `/tmp/agentblazor-published-realapp-cleanarch-preview6-20260415071751/CleanArchitectureWithBlazorServer`.

Latest real-provider validation:

- Real OpenAI provider config was resolved from `demo/AgentBlazor.Demo/appsettings.Development.json` without printing the API key.
- `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ProviderAdapterIntegrationTests` passed `30/30`.
- Coverage includes OpenAI chat response, semantic workflow capability invocation, approval-required workflow execution, blocked/recovery/retry workflow execution, streaming/reconnect replay, cancellation, concurrent workflow runs, and deterministic session-state continuity.

Latest package validation:

- Current source and latest published-feed version is `0.1.0-preview.9` at commit `faaeb6842ca90f4bd4cdeea070b1a28e30886463`. This release adds `AgentBlazor.Client` for browser-safe hosted WebAssembly remote chat, keeps the SDK roll-forward change to `latestMinor`, keeps `Microsoft.AspNetCore.App.Internal.Assets` lockfile entries at `10.0.6`, and keeps explicit `RuntimeFrameworkVersion` `10.0.6` pins for the demo and starter web apps.
- GitHub Packages workflow `publish-github-packages-preview` run `24680658866` passed on commit `faaeb6842ca90f4bd4cdeea070b1a28e30886463` for `0.1.0-preview.9`. The workflow passed restore, build, tests, e2e, local package smoke, runtime package publish, WebAssembly client package publish, CLI package publish, and artifact upload.
- Published-feed hosted WebAssembly validation for `0.1.0-preview.9` passed in a generated server+client app: package install for `AgentBlazor`, `AgentBlazor.Client`, and `AgentBlazor.Cli`, server `MapAgentBlazorRemoteChat()` endpoint, WebAssembly `HttpClient`, remote widget/surface/panel/bar prompt submission, and widget minimize/reopen all passed. Report: `tests/e2e/artifacts/hosted-wasm-remote-chat/2026-04-20T17-31-59-002Z/report.md`.
- Published-feed real-app validation for `0.1.0-preview.9` passed against `damienbod/BlazorSecurityNet10`: baseline restore/build, package install, CLI install, `agentblazor --version`, `init`, `scaffold --diff`, `scaffold --approve`, restore, build, `doctor`, `validate`, runtime HTTP smoke, and all-surface browser prompt validation all passed.
- Published-feed all-surface browser validation for `0.1.0-preview.9` passed against `damienbod/BlazorSecurityNet10`: widget, surface, panel, and bar all accepted production-style prompts and rendered deterministic provider responses after CLI install. Report: `tests/e2e/artifacts/external-chat-widget/2026-04-20T17-33-11-449Z/report.md`.
- GitHub Packages workflow `publish-github-packages-preview` run `24441459326` passed on commit `13336a8779c761a340472ad2f1530dd3bdb68c12` for `0.1.0-preview.6`. Published-feed package inspection confirmed current commit metadata, Microsoft Agents 1.1 dependencies, the AG-UI host async session serializer, and current semantic workflow APIs.
- Published-feed real-app validation for `0.1.0-preview.6` passed against `neozhu/CleanArchitectureWithBlazorServer`: package install, CLI install, `agentblazor --version`, `init`, `scaffold --diff`, `scaffold --approve`, restore, build, `doctor`, `validate`, and deterministic runtime HTTP smoke all passed.
- Local package smoke for `0.1.0-preview.4` passed in `/tmp/agentblazor-preview4-local-smoke-20260415064042`: installed `AgentBlazor` and `AgentBlazor.Cli` from a local feed with NuGet.org available for dependencies, confirmed `agentblazor --version` reported `0.1.0-preview.4`, ran `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, `dotnet build`, `doctor`, and `validate`. Build passed with `0` warnings and `0` errors; `doctor` readiness passed `9/9`; `validate` readiness passed `9/9`, validation `3/3`; runtime HTTP smoke with placeholder `OpenAI__ApiKey` rendered the home page, `AgentChatWidget`, and AgentBlazor static assets.
- Real-app package validation against `neozhu/CleanArchitectureWithBlazorServer` at `4ef0b7c599be97d93049028e7b9a641f237cc5c7` exposed that `0.1.0-preview.3` allowed NuGet to float Microsoft Agents packages to `1.1.0`, causing startup to fail with `TypeLoadException` for `SerializeSessionCoreAsync`. Current source now targets the Microsoft Agents 1.1 API family and implements `SerializeSessionCoreAsync`.
- GitHub Packages workflow `publish-github-packages-preview` run `24440492708` succeeded for `0.1.0-preview.4`, but real-app published-feed validation proved GitHub Packages already contained an older immutable `0.1.0-preview.4` runtime package from commit `45da291b84441c75e50af4c16cbed33ccb31cc5d`. That stale package lacked current `AgentBlazor.App` semantic workflow APIs and failed real-app scaffold build. The workflow no longer uses `--skip-duplicate`; duplicate package versions now fail instead of being reported as successful publishes.
- GitHub Packages workflow `publish-github-packages-preview` run `24440963258` succeeded for `0.1.0-preview.5`, and published-feed inspection confirmed commit `00ca09a1932a70c3fc966383d609cf41fd81d9b9`, exact Agents `1.0.0-preview.260209.1` dependencies, and the `AgentBlazor.App` API. Real-app restore then failed with `NU1107` because `CleanArchitectureWithBlazorServer` already references `Microsoft.Agents.AI.OpenAI` `1.1.0` through its infrastructure project. Current source is now moved to the Microsoft Agents 1.1 API family.
- GitHub Packages workflow `publish-github-packages-preview` run `24420548131` passed on commit `193ccdfc92f2b6e618b7dafa6e6228cfe2597171` and uploaded artifact `agentblazor-packages-0.1.0-preview.3`.
- Clean Blazor Web App `/tmp/agentblazor-github-consumer-validation-preview3-20260414201600/PackageConsumer` installed `AgentBlazor` and `AgentBlazor.Cli` version `0.1.0-preview.3` from `https://nuget.pkg.github.com/ashpeterson/index.json` using isolated NuGet cache/tool paths and no repo-local package feed.
- Published-feed `agentblazor --version` reported `0.1.0-preview.3`; `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, and `dotnet build` passed with `0` warnings and `0` errors.
- Published-feed `doctor` readiness passed `9/9`; `validate` readiness passed `9/9`, validation `3/3`.
- Runtime startup was smoke-tested from the published package with a placeholder `OpenAI__ApiKey`; the clean app served the home page, `AgentChatWidget`, and AgentBlazor/MudBlazor static assets. Live model calls were not run in this published-feed consumer smoke.
- Earlier GitHub Packages `0.1.0-preview.2` runtime package validation found a stale immutable package build that did not contain current scaffold/runtime symbols. `0.1.0-preview.3` later passed clean-app validation but failed real-app runtime compatibility. `0.1.0-preview.6` passed Clean Architecture validation but `damienbod/BlazorSecurityNet10` exposed that scaffolded assets did not preserve CSP nonces. Use `0.1.0-preview.9` or later for private-preview testing.
- Packed `AgentBlazor.0.1.0-preview.2.nupkg`, `AgentBlazor.Cli.0.1.0-preview.2.nupkg`, and internal dependency packages to `/tmp/agentblazor-package-validation-preview2-20260413/packages`.
- Clean Blazor Web App `/tmp/agentblazor-package-validation-preview2-20260413/work/PackageSmoke` installed the local `AgentBlazor` package and `AgentBlazor.Cli` tool with no repo-local `ProjectReference` entries; `agentblazor --version` reported `0.1.0-preview.2`.
- Package-installed app `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, and `dotnet build` passed; build had `0` warnings and `0` errors.
- Package-installed app `doctor` readiness passed `9/9`; `validate` readiness passed `9/9`, validation `3/3`.
- The package validation exposed and fixed the stale CLI scaffold/display version, which previously used `1.0.0`, and a package-first onboarding gap where an app with `AgentBlazor` already installed still needed a direct `MudBlazor` package reference added by scaffold.
- Previous `0.1.0-preview.1` packaged runtime smoke runner `/tmp/agentblazor-package-validation-20260413/work/PackageRuntimeSmoke` referenced `AgentBlazor` only and completed real OpenAI-backed normal and streaming semantic workflow calls with `PACKAGE_SMOKE_OK`; streaming produced `24` events.
- The earlier GitHub Packages published-feed preflight was blocked by missing local credentials; that is now resolved by the authenticated `gh` session and the `0.1.0-preview.9` validation above.

Latest browser status:

- full end-to-end Playwright suite passed
- current suite count: `4/4`
- real-usability nightly passed in workflow run `24598561465` after explicit `RuntimeFrameworkVersion` `10.0.6` pins stabilized locked restore for the demo and starter web apps

Coverage includes:

- landing page journey
- workflow-first launch into the featured orchestration route
- components explorer layout and focused component routes

## Current Gaps

### Product Gaps

- Persistent user-level intelligence is not complete:
  - no durable `IActionHistoryStore` implementation yet
  - paid suggestions are not yet a mature long-term personalization system
- Hosted WebAssembly client chat automation is not complete yet:
  - companion client projects are detected correctly
  - server host wiring and workflow setup build
  - the initial browser-safe remote chat package and endpoint exist with component and endpoint tests
  - a fresh standalone WebAssembly client app builds with all remote chat surfaces from the packed `AgentBlazor.Client` package
  - generated hosted-WASM browser validation passes across remote widget, surface, panel, and bar prompt submission
  - CLI auto-scaffold for browser-client edits is still review-first/manual
- Some component demos still prove isolated control better than full workflow depth, but the workflow showcase now spans multiple blocked and approval-gated scenarios.

### Demo Gaps

- The broader demo now covers supplier, file, recipe, incident, response-orchestration, and release-dossier workflows with shared decision-support patterns, explicit recovery paths on the richer scenarios, and two cross-system orchestration routes, but it is still a curated showcase rather than a complete playground.
- Compatibility proof now exists for every shipped Mud-backed `Agent*` component, but some surfaces are still only proven in one or two shapes:
  - `AgentTreeView` still needs deeper hierarchy and expansion scenarios
  - composed workflow proof should keep expanding beyond the current supplier, file, recipe, incident, response-orchestration, and release-dossier workflow set
  - `AgentDataGrid` still needs stronger public proof around richer server-backed and templated usage
- `AgentFileUpload` should be treated as host-workflow-first:
  - file-name actions are useful for deterministic workflow state and demos
  - real browser file payloads still remain host-owned and should not be implied to be agent-synthesizable
- The components explorer is strong for drop-in primitives, but richer production narratives are still uneven across:
  - `AgentDataGrid`
  - `AgentTreeView`
  - `AgentCommandBar`
  - `AgentFileUpload`

### Platform Gaps

- Declarative interoperability exists, but only as an adapter subset today, not full schema coverage for every external declarative UI feature.
- MCP tool integration exists, but a broader production-grade hosted-app contract still needs more depth if open-ended hosting becomes a larger platform story.

## Production Roadmap

### Phase 1: Ship Free Tier (Ready Now)

| Item | Status |
|------|--------|
| Core runtime | Done |
| All 14 components | Done |
| Demo app with workflows | Done |
| CLI tool (`agentblazor`) | Done |
| Create `docs/quickstart.md` | Done |
| Publish NuGet pre-release | Private preview published |

### Phase 2: Complete Pro Tier (Done)

| Item | Status |
|------|--------|
| `SqliteActionHistoryStore` | Done |
| `SqliteAgentInspectorStore` | Done |
| Wire stores into `UseProLicense()` | Done |
| License key server validation | Optional (format validation exists) |
| User profile intelligence | Future enhancement |

### Phase 3: Enterprise Features (Future)

| Item | Status |
|------|--------|
| SSO/SAML integration | Not started |
| Audit log export | Not started |
| Role-based action permissions | Not started |
| Usage analytics dashboard | Not started |

### Ongoing

1. Keep tightening the workflow-first shell and UX so the orchestration routes are the default story.
2. Continue expanding workflow-first proof into broader production-style scenarios.
3. Keep tightening the live demo journey for fast product videos.
4. Begin package/module split after demo/product proof is strong enough.

Current note:
- The runtime review pass from 2026-04-09 is now closed:
  - execution scope is preserved correctly across adapter turns
  - middleware runs through both standard and streaming turns
  - OpenAI-compatible custom endpoints enforce `http`/`https`
  - package source mapping no longer blocks `AgentBlazor.Components.Tests` or `AgentBlazor.Cli.IntegrationTests`
- `AgentBlazorOptions.DefaultAgent` is now explicitly obsolete as a legacy compatibility surface; host apps should move toward explicit `AddAgent(...)` registration, and normal runtime resolution no longer synthesizes or prefers a built-in default agent.
- Adapter-backed inspector and trace persistence now record normalized step-oriented execution data as the canonical devtools shape; remaining legacy action/result payloads should only survive where the legacy runtime path still needs them.
- The landing page, workflow hub, and components explorer now reinforce a workflow-first journey, with the component surface positioned as a fallback reference rather than a default destination.
- The workflow hub and workflow routes now use route-specific assistant profiles and semantic-first prompt guidance, so the assistant defaults line up with the workflow-first product story instead of falling back to generic component language.
- `/docs`, `README.md`, and `samples/AgentBlazor.Starter` now present one package-first free onboarding path centered on `AddAgentBlazor(... ConfigureBuilder(... AddWorkflow<T> ...))`, with repo-local source mode treated as maintainer-only validation rather than the public story.
- Prompt tracing and report-style consumers now expose normalized workflow-step views as the primary reporting language, with planner-era action/result lists left as compatibility storage rather than the first-class presentation model.
- Component-mocking and report-generation test helpers now prefer normalized `ExecutionPlan` data and only fall back to legacy action/result payloads when the legacy runtime path returns no plan.
- the legacy planner runtime, `LegacyAgentRuntimeAdapter`, `IAgentRuntime`, `IAgentRuntimeStreaming`, `AgentRuntime`, `AgentPlanner`, and `PlanExecutor` have now been removed from the codebase entirely; remaining plan-oriented helpers are execution-model utilities rather than a hidden runtime path.
- Hosted AG-UI response metadata now reports normalized execution-step counts before falling back to legacy planned-action counts.
- `AgUiHostingIntegrationTests` now run fully adapter-first, including reconnect/stop control coverage through adapter-native test doubles; the hosted AG-UI suite no longer depends on any legacy runtime seam.
- focused response/history tests now treat `ExecutionPlan` as the primary model and use `LegacyPlannedActions` / `LegacyExecutionResults` only when they are explicitly validating compatibility behavior.
- the shared `/demo` shell now presents the experience as a workflow hub and uses route-aware assistant guidance so workflow routes lead with semantic workflow language rather than component-control framing.
- prompt-trace reports and inspector phase labels now use workflow-step terminology, so workflow runs read consistently across reports, chat, workflow pages, and inspector/devtools.
- prompt tracing, component mocking/reporting, shared-state, hosted AG-UI, and runtime integration coverage now all run adapter-first by default.
- Public response/history records now expose normalized execution-plan state directly and label the old planned-action / execution-result lists as legacy compatibility payloads instead of implying they are co-equal primary data.
- The workflow hub now includes an incident-escalation scenario that exercises `AgentTreeView`, `AgentTabs`, `AgentStepper`, `AgentCommandBar`, and `AgentDialog` together behind semantic capabilities, with focused integration proof for blocked, approval-gated, and recovery-driven execution.
- The recipe-release workflow now includes a semantic recovery playbook that clears safe metadata blockers and proves blocked -> recover -> approval-gated draft flow through focused integration coverage.
- The file-audit workflow now includes a semantic recovery playbook that replaces rejected files and proves blocked -> recover -> retry-success flow through focused integration coverage.
- The supplier-compliance workflow now includes a semantic recovery playbook that clears severe supplier blockers and proves blocked -> recover -> approval-gated remediation drafting through focused integration coverage.
- The workflow hub now includes a response-orchestration route that composes supplier remediation, audit evidence, and incident escalation into one approval-gated recovery-aware response packet, giving the showcase its first real cross-system workflow proof.
- The response-orchestration route now deep-links into the live supplier, file-audit, and incident workflow surfaces with shared session state and route-scoped focus, so the showcase is no longer limited to a single composite orchestration shell.
- The response-orchestration route now carries guided return progression too: subsystem pages return with surface/state metadata, and the orchestration shell recommends the next live route instead of treating each handoff as isolated.
- Focused integration proof now covers a full cross-surface orchestration journey: supplier progression, file progression, incident progression, and final approval-gated response-packet completion in one shared-session scenario.
- The response-orchestration shell now processes guided route returns directly and renders a live journey board for supplier, file, incident, and packet phases, so cross-surface progress is visible in the demo without requiring a manual reassessment step after each return.
- The workflow hub now features the response-orchestration route as the strongest current production-style path, with an explicit cross-system progression panel rather than treating all workflow cards as equivalent isolated demos.
- The response-orchestration route can now advance the next guided subsystem stage itself through a semantic orchestration action and visible demo controls, so the shared session can move from supplier -> file -> incident readiness directly inside the orchestration shell before the final approval-gated packet step.
- The response-orchestration shell now keeps a visible orchestration activity trail, and the supplier, file, and incident workflow pages now explain their current orchestration contribution directly in the live surface, so the cross-route demo reads more like one coordinated application workflow than a hub plus disconnected pages.
- The workflow hub now includes a second cross-system production-style route, `release-dossier`, which coordinates recipe release readiness and audit evidence into one approval-gated release dossier with guided live-surface handoffs.
- The recipe release page now participates in orchestration handoff/return flow, so recipe readiness is no longer isolated from the broader multi-surface showcase story.
- The workflow hub now includes an explicit suggested live-demo sequence and promotes the orchestration routes as featured live demos, so `/demo` reads more like a presenter flow than a flat route directory.
- The featured response-orchestration and release-dossier routes now sit at the center of a much narrower workflow-first demo shell, so the product story is easier to grasp and faster to present live.
- The landing page and `/demo` hub have now been refactored into a faster, lower-text demo shell that pushes two orchestration-led live demos first and hides older reference material behind supporting routes.
- `DefaultAgentOptions` is now explicitly hidden and marked legacy in code, so the remaining built-in default-agent surface reads as migration scaffolding rather than a normal first-class host authoring model.
- the public legacy default-agent registration entry points are now removed, and the remaining built-in default-agent surface is migration-only component-catalog scaffolding rather than runtime behavior.
- implicit built-in default-agent registration and implicit default-agent runtime fallback are now removed from the normal service path; explicit registered agents and route/context targeting are the only non-legacy resolution modes.
- the normal `IAgentRuntimeAdapter` registration now returns a no-provider response when no chat client/runtime adapter is configured, instead of silently falling back to a hidden planner runtime.
- the normal `AddAgentBlazorServices()` path is now adapter-only; there is no `IAgentRuntime` registration path left in the product container.
- `PromptTracingTests`, `ComponentMockingTests`, `ComponentMockingReportTests`, `SharedStateStoreTests`, `AgentRuntimeIntegrationTests`, and `AgUiHostingIntegrationTests` now run adapter-first, so the old planner-runtime architecture is no longer a test blocker.
- the adapter now has an explicit opt-in legacy component-tool alias path for compatibility experiments, but it is disabled by default so normal tool projection stays normalized and single-shaped.
- the public demo surface is now intentionally minimal: the old Dojo, parity pages, redirects, and playbook route are gone from the visible product story.

## Reference Docs

- Architecture: `docs/architecture.md`
- Expansion roadmap: `docs/component-expansion-plan.md`
- MudBlazor compatibility roadmap: `docs/mudblazor-compatibility-roadmap.md`
- NuGet prerelease checklist: `docs/nuget-prerelease-checklist.md`
- GitHub Packages private preview: `docs/github-packages-private-preview.md`
- Tier model: `docs/pricing-tiers.md`
- Pro tier operations: `docs/pro-tier-operations.md`
- Runtime realignment plan: `docs/runtime-realignment-plan.md`
