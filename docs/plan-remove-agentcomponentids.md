# Plan: Remove AgentComponentIds — Zero Page Declarations for Agent Routes

**Status: Implemented.** Mount-time registration + intent→route (no AgentComponentIds, no AgentPageDiscovery).

## Goal

Customers should **not** add any page-level code for the agent to work. Swapping `MudDataGrid` → `AgentDataGrid` (and similar for other components) must be enough. No `AgentComponentIds`, no `ValueMappings`, no Program.cs route config.

This plan focuses on **removing the `AgentComponentIds` requirement** so the framework discovers “which route hosts which agent component” automatically.

---

## Current State

- **AgentPageDiscovery** (Core) scans assemblies for types with `[Route]` **and** a static property `AgentComponentIds`. It fills `AgentBlazorOptions.UnmountedComponentRoutes` (componentId → route).
- **DeterministicAgentRuntime** uses `UnmountedComponentRoutes` to prepend a “navigate to route” step when the first plan step targets a component that isn’t mounted (e.g. user on Home, says “show me high risk suppliers” → navigate to `/suppliers` then filter).
- **Demo** currently has `public static string[] AgentComponentIds { get; } = [AgentComponentCapabilityProfile.AgentDataGridComponentId];` on `Suppliers.razor` — this is what we want to remove.

---

## Robust Solutions (Research Summary)

| Approach | How it works | Pros | Cons |
|----------|---------------|------|------|
| **1. Source generator** | At build time, analyze the app’s compilation: find `[Route]` types, find Razor-generated `BuildRenderTree`, detect `OpenComponent(seq, typeof(ComponentType))` / `OpenComponent<T>()`, map component types to our component IDs, generate a static route map. | No page code; works before first visit; single build-time step. | Requires app to reference the analyzer (one csproj line); generator must understand Razor-generated C#. |
| **2. Mount-time registration** | When an agent component (e.g. `AgentDataGrid`) is rendered, it calls a service to register `(componentId, currentUri)`. The runtime uses this “live” registry for navigation. | No page code; no build tooling; works with any host. | Route unknown until user has visited the page at least once; “navigate to suppliers” from Home fails until `/suppliers` was ever opened. |
| **3. IL / reflection scan** | At startup, reflect over assemblies: for each `[Route]` type, get `BuildRenderTree` method body, parse IL for type tokens used in `OpenComponent`. Map those types to component IDs. | No page code; no source generator reference. | IL parsing is brittle (compiler changes, obfuscation); more complex and error-prone. |
| **4. Parse .razor files** | At build or startup, read `.razor` files from disk and parse markup for `<AgentDataGrid>`, `<AgentForm>`, etc. | Conceptually simple. | Requires file system access to app’s source; doesn’t work from a library that only sees the compiled assembly; build-time would need a task that writes a manifest. |

**Recommendation:** Use **1 (source generator)** as the primary, supported way to get “component → route” with zero page code. Use **2 (mount-time registration)** as a **complement**: whenever a component mounts, register its route so the registry is updated as the user navigates. Merge both: startup uses generated map (if present), and mount-time updates add or override routes. This gives:

- **With generator:** Full map at startup; “show me high risk suppliers” from Home works immediately.
- **Without generator:** After the user has visited a page once, that route is in the registry; first-time navigation from Home to a never-visited page still fails unless we add a fallback (e.g. “I don’t know where that is” or use nav menu links).

---

## Implementation Plan

### Phase 1: Mount-time registration (no new package, works everywhere)

1. **Route registration service**
   - Add `IRouteRegistry.RegisterComponentRoute(string componentId, string path)` (or extend existing registry) so a component can register “I am AgentDataGrid at /suppliers”.
   - Ensure `AgentBlazorOptions.UnmountedComponentRoutes` is writable and is the single source of truth, or add a “live” component→route store that is merged when resolving routes.

2. **Agent components call register on mount**
   - In each agent component base (e.g. `AgentDataGrid`, `AgentNavMenu`, `AgentForm`, …), in `OnInitialized`/`OnInitializedAsync`, resolve current URI (e.g. via `NavigationManager` or a scoped service that has it) and call the registry: e.g. `RegisterComponentRoute(AgentDataGridComponentId, currentPath)`.
   - Use a scoped or app-level service that has access to `NavigationManager` so components don’t need to take it as a parameter.

3. **DeterministicAgentRuntime**
   - Keep using `UnmountedComponentRoutes` (or the merged view). No change to prepend logic; it already uses “component not mounted → get route → prepend navigate”. The registry will now be populated by mount-time as well.

4. **Backward compatibility**
   - Keep **AgentPageDiscovery** and `AgentComponentIds` working: if a page declares `AgentComponentIds`, discovery still fills the map. So existing apps don’t break.

Result: No `AgentComponentIds` needed if we also add the source generator. Without the generator, navigation works only to pages the user has already visited (or that declare `AgentComponentIds`).

### Phase 2: Source generator (zero declarations, works before first visit)

1. **New project: AgentBlazor.SourceGenerator**
   - Class library, targets `netstandard2.0` (or as required by the generator SDK), references `Microsoft.CodeAnalysis.CSharp` (and optionally Razor / workspace packages if needed). Implements `IIncrementalGenerator` or `ISourceGenerator` that runs on the **app’s** compilation (the app references this project as `OutputItemType="Analyzer"`).

2. **Generator logic**
   - Find all types with `[Route]` (e.g. by scanning for `RouteAttribute` on types).
   - For each such type, find the method that contains the render tree (e.g. `BuildRenderTree` or the Razor-generated equivalent). The Razor compiler generates a partial class with `OpenComponent(sequence, typeof(SomeComponent))` or `OpenComponent<SomeComponent>(sequence)`.
   - Detect `OpenComponent` invocations and extract the component type (from `typeof(...)` argument or generic type parameter).
   - Map known agent component types to our component IDs (e.g. `AgentDataGrid` → `AgentComponentCapabilityProfile.AgentDataGridComponentId`). Only include types that are in our profile (AgentDataGrid, AgentForm, AgentTabs, AgentNavMenu, AgentDialog); ignore others.
   - For each (componentId, route) pair, build a dictionary. Route comes from the `[Route]` template on the page type (same as today’s discovery).

3. **Generated API**
   - Emit a static class in the app’s assembly, e.g. `AgentBlazor_GeneratedRoutes`, with a static property or method that returns `IReadOnlyDictionary<string, string>` (componentId → route). Use a known type name so the host can find it by reflection.

4. **Hosting integration**
   - In `AgentBlazorRegistrationOptions.ApplyOptions`, after (or instead of) calling `AgentPageDiscovery.DiscoverAgentPages`, try to load the generated type from the entry assembly (or from each `AssembliesToScan`): e.g. `assembly.GetType("AgentBlazor_GeneratedRoutes")` or a name we document. If found, get the dictionary and merge into (or set) `options.UnmountedComponentRoutes`. So generated routes take precedence or are merged; existing discovery can still run for apps that use `AgentComponentIds`.

5. **Demo and docs**
   - Remove `AgentComponentIds` from `Suppliers.razor` (and any other demo pages).
   - Add a reference to the source generator from the Demo project (and document in the main README): e.g. `ProjectReference ... OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
   - Document that for “zero page code,” the app should reference the source generator; then no `AgentComponentIds` (or Program.cs route config) is needed.

### Phase 3: Cleanup and deprecation

1. **Docs**
   - State that `AgentComponentIds` is **optional** and only needed if the app does **not** use the source generator and wants navigation to work before the user has visited a page. Prefer “reference the generator and add no page code.”

2. **Optional: Deprecate AgentComponentIds**
   - Mark the static property approach as obsolete in XML docs and point to the source generator. Do not remove discovery until a major version when we can break compatibility.

3. **Tests**
   - Add tests that: (1) with a mock assembly that has a generated-style type, UnmountedComponentRoutes is populated; (2) mount-time registration adds a route and the runtime can resolve it.

---

## File / Type Changes Summary

| Area | Change |
|------|--------|
| **Core** | `IRouteRegistry`: add `RegisterComponentRoute(componentId, path)` (or equivalent). `InMemoryRouteRegistry`: implement it (merge into existing or separate store used for component→route). |
| **Core** | `AgentPageDiscovery`: keep as-is for backward compat; optionally skip or run after “generated” merge so generated wins. |
| **Components** | Each agent component (AgentDataGrid, AgentForm, AgentTabs, AgentNavMenu, AgentDialog): in OnInitialized, get current URI and call `IRouteRegistry.RegisterComponentRoute(...)`. Need a way to get NavigationManager (injection or via a small service). |
| **Hosting** | `ApplyOptions`: after discovery, try to load `AgentBlazor_GeneratedRoutes` from scanned assemblies and merge into `UnmountedComponentRoutes`. |
| **New** | `AgentBlazor.SourceGenerator`: generator that emits `AgentBlazor_GeneratedRoutes` with (componentId → route) from [Route] + OpenComponent analysis. |
| **Demo** | Remove `AgentComponentIds` from Suppliers.razor; add analyzer reference to the generator. |

---

## Risks and Mitigations

- **Razor-generated code shape:** Different Blazor/Razor versions might generate different C#. Mitigation: base the generator on the common pattern (`OpenComponent(seq, typeof(T))` / `OpenComponent<T>(seq)`); test against the current SDK; document supported Blazor version.
- **Multiple assemblies:** App might have pages in more than one assembly. Mitigation: run the generator in the app project (which compiles all referenced projects); the generated type lives in the app assembly and lists all discovered pages from the compilation. If the app uses multiple app-like projects, each would need to reference the generator and expose its own generated type; hosting could scan all `AssembliesToScan` for the known type name and merge.
- **First-time navigation without generator:** If the app doesn’t use the generator and doesn’t use `AgentComponentIds`, navigation to a page the user has never visited fails. Mitigation: document that for “zero config” navigation before first visit, the source generator is required; mount-time alone is best-effort.

---

## Out of Scope (Already Addressed Elsewhere)

- **ValueMappings / semantic values:** Already removed from the demo; framework provides built-in fallbacks. No change in this plan.
- **Program.cs config:** Already removed; discovery and (after this) generator + mount-time remove the need for UnmountedComponentRoutes in Program.cs.

---

## Success Criteria

1. Demo has **no** `AgentComponentIds` (or any similar page-level declaration) and no Program.cs route configuration.
2. “Show me high risk suppliers” from Home still navigates to `/suppliers` and applies the filter (with the generator referenced).
3. Existing apps that use `AgentComponentIds` continue to work.
4. New apps can achieve “only swap to AgentDataGrid” by referencing the source generator and adding no other code.
