# AgentBlazor Generative UI + Complex Workflow Handoff

## Snapshot (As of February 25, 2026)
This branch has completed the practical Phase 1/2/3 path for internal generated UI components and a real-data demo workflow.

The demo now behaves like an end-user app using persisted data (SQLite) instead of deterministic hardcoded chat outputs.

## What We Are Trying To Achieve
Build AgentBlazor so generated UI and agent actions are reusable in any Blazor app with minimal setup:
1. Keep core and components domain-agnostic.
2. Keep demo-specific logic only in the demo app.
3. Make generated UI blocks chat-native and production-shaped (card/form/table/chart).
4. Let chart components pull data from app-owned data sources (not hardcoded arrays in component payloads).
5. Demonstrate a realistic multi-step workflow that flows across pages and components.

## Where We Are Now
Phase 1 is functionally done.
Phase 2 is functionally done.
Phase 3 is functionally done.

## What Has Been Done

### 1) Generated UI Componentization (Phase 1)
Implemented internal generated UI components and moved rendering out of inline surface branches:
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedCard.razor`
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedForm.razor`
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedTable.razor`
- `src/AgentBlazor.Components/GenerativeUI/AgentGeneratedChart.razor`
- `src/AgentBlazor.Components/GenerativeUI/AgentGenerativeSurface.razor`

UX pass completed:
- darker style, better composition, larger chat render canvas
- CSS updates across generated block styles and chat surface styles

### 2) Chart as First-Class Block + Data Source Resolver (Phase 2)
Added chart spec/tool support in core:
- `src/AgentBlazor.Core/Components/AgentGenerativeUiSpec.cs`
- `src/AgentBlazor.Core/Components/AgentUiToolCatalog.cs`
- `src/AgentBlazor.Core/Components/AgentChartDataSource.cs`

Added registration path for chart data resolvers:
- `src/AgentBlazor.Hosting/AgentBlazorRegistrationOptions.cs`
- Supports direct resolver and DI factory resolver overloads.

Chart block rendering now supports:
1. Inline chart payloads (`chartType`, `labels`, `series`)
2. Data-source mode (`dataSource`, optional `dataArguments`) resolved via DI

### 3) Spillover Cleanup
Removed demo deterministic overrides and demo-only tool catalog usage that leaked behavior assumptions:
- Deleted `demo/AgentBlazor.Demo/Services/E2eDeterministicChatClient.cs`
- Deleted `demo/AgentBlazor.Demo/Services/DemoAgentUiToolCatalog.cs`
- Removed deterministic service replacement path from demo startup

Also removed component-level chart resolver param plumbing from public wrappers so setup route is centralized via service registration.

### 4) Real Persisted Workflow in Demo (Phase 2 hardening + Phase 3 foundation)
Added demo DB model and services:
- `demo/AgentBlazor.Demo/Data/DemoWorkflowDbContext.cs`
- `demo/AgentBlazor.Demo/Data/SupplierEntity.cs`
- `demo/AgentBlazor.Demo/Data/OnboardingRequestEntity.cs`
- `demo/AgentBlazor.Demo/Services/SupplierWorkflowService.cs`
- `demo/AgentBlazor.Demo/Services/DemoWorkflowDatabaseSeeder.cs`
- `demo/AgentBlazor.Demo/Services/DemoChartDataSources.cs`

Startup wiring:
- `demo/AgentBlazor.Demo/Program.cs`
- `demo/AgentBlazor.Demo/appsettings.json`

Behavior now:
- Onboarding submit persists new request and creates supplier
- Suppliers page reads from DB service
- Workflow and chart summaries read from DB service
- Generated chart tool can use live data source IDs:
  - `demo.suppliers.risk.by-region`
  - `demo.suppliers.risk.tier-distribution`
  - `demo.onboarding.volume.monthly`

### 5) Complex Workflow Demo Surface (Phase 3)
Added a dedicated page combining multiple agent-capable components and generated UI chat:
- `demo/AgentBlazor.Demo/Components/Pages/Demo/Workflow.razor`

This page includes:
1. KPI summary cards from real DB data
2. Onboarding queue grid (`AgentDataGrid`)
3. Supplier risk grid (`AgentDataGrid`)
4. Tabs (`AgentTabs`) controlling queue/risk context
5. Generated UI chat surface (`AgentChatSurface`, `EnableGeneratedUi=true`)

Navigation and discoverability updated:
- `demo/AgentBlazor.Demo/Components/Layout/DemoNavMenu.razor`
- `demo/AgentBlazor.Demo/Components/Pages/Demo/DemoHome.razor`
- `demo/AgentBlazor.Demo/Components/Pages/Demo/GenerativeUi.razor`

### 6) Planner Context Improvement
Agent instructions now flow into structured planner prompt:
- `src/AgentBlazor.Core/Runtime/Planning/IActionPlanner.cs`
- `src/AgentBlazor.Core/Runtime/Planning/DeterministicAgentRuntime.cs`
- `src/AgentBlazor.Core/Runtime/Planning/StructuredActionPlanner.cs`

This allows app-specific constraints like preferring chart data sources over inline fake chart values.

### 7) E2E Coverage for Workflow (Phase 3)
Updated and added Playwright specs:
- `tests/e2e/specs/generative-ui.spec.cjs`
- `tests/e2e/specs/workflow.spec.cjs`

Workflow e2e now verifies:
1. workflow page renders queue/risk/chat
2. onboarding submission persists and appears on workflow queue

## Validation Status
Executed and passing:
1. `dotnet build AgentBlazor.sln`
2. `dotnet test tests/AgentBlazor.Core.Tests/AgentBlazor.Core.Tests.csproj`
3. `dotnet test tests/AgentBlazor.Components.Tests/AgentBlazor.Components.Tests.csproj`
4. `npm --prefix tests/e2e run test:e2e`

Note: Playwright required local setup in this branch:
1. `npm --prefix tests/e2e install`
2. `npm --prefix tests/e2e run install:browsers`

## Important Design Decisions Locked In
1. No deterministic/mock chat client in demo startup path.
2. No demo-domain logic in core tool catalog.
3. Chart data should come from app-registered resolver via DI.
4. AddAgentBlazor setup remains the main integration path.
5. Demo is allowed to keep domain-specific sample data and services because it represents an end-user app.

## What Is Next

### Recommended Phase 4 (Consolidation + Productization)
1. Add focused unit tests for `SupplierWorkflowService` calculations and ID generation.
2. Add component tests for `AgentGeneratedChart` resolver states (loading/error/no-data).
3. Add one integration test that verifies generated chart block using `dataSource` end-to-end.
4. Write official docs page for chart data-source contract and resolver registration patterns.
5. Decide if demo DB should use migrations instead of `EnsureCreated`.
6. Add CI step for Playwright browser install + e2e execution if not already present.

### Optional Product Enhancements
1. Add resolver caching policy hooks for expensive chart queries.
2. Add explicit chart resolver timeout/cancellation policy settings.
3. Add richer chart argument schema guidance in tool catalog prompt docs.

## Quick Start For Successor
1. Run demo: `dotnet run --project demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj`
2. Open `/demo/workflow`.
3. Submit a new onboarding request in `/demo/onboarding`.
4. Return to `/demo/workflow` and verify queue/supplier changes.
5. Open generated UI chat and ask for chart views using workflow prompts.
6. Run validation commands listed above before pushing.

## Notes For Handoff
Current repo has many modified files from this full effort, including generated UI refactor and demo architecture changes. Before final merge, group commits by concern:
1. Core/hosting chart resolver + planner context changes
2. Component rendering/style changes
3. Demo data/workflow changes
4. E2E updates
