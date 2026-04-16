# Production Plan

Last updated: 2026-04-15

Honest assessment and actionable plan to move from private preview to production.

## Readiness Label

Current readiness: **private preview / published-feed validated**.

AgentBlazor is not yet ready for a broad, unsupported production release. The non-demo test matrix is green, the reviewed runtime/CLI defects have been fixed, and the current private-preview build now has real-app, provider-backed workflow, local package-install, and GitHub Packages published-feed validation. The next gate is controlled validation by external test users before production claims.

Current validated baseline:

- `AgentBlazor.Core.Tests`: `261/261`
- `AgentBlazor.Components.Tests`: `98/99`, `1` skipped
- `AgentBlazor.Cli.Analysis.Tests`: `132/132`
- `AgentBlazor.Cli.IntegrationTests`: `9/9`
- `AgentBlazor.IntegrationTests`: `105/105`

Recent hardening complete:

- adapter execution preserves caller-owned DI scopes across turns
- middleware runs through normal and streaming runtime paths
- OpenAI-compatible endpoint validation rejects non-HTTP(S) URI shapes
- package source mapping no longer blocks the full non-demo test matrix
- standard Blazor Web App CLI scaffold paths are now passing fresh-app and official-sample real-project validation
- independent real-world Clean Architecture Blazor Server validation now passes baseline build, scaffold, rebuild, `doctor`, and `validate`
- independent real-world Oqtane framework validation now passes baseline build and review-first safe scaffold/rebuild while preserving manual-review guidance for host-specific startup and UI wiring
- independent real-world hosted WebAssembly validation now passes baseline build, server-host scaffold, rebuild, `doctor`, and `validate` with expected client UI manual-review warnings
- real OpenAI-backed adapter validation now covers simple chat, semantic workflow tool invocation, approval gating, blocked/recovery/retry, streaming/reconnect, cancellation, concurrency, and session-state continuity
- Azure OpenAI provider registration now uses the Microsoft Azure OpenAI client on the same `IChatClient` runtime path, with API-key and `TokenCredential` coverage; live Azure deployment validation remains a per-app gate
- local and published-feed package validation now prove `AgentBlazor` and `AgentBlazor.Cli` can be installed into a clean app without repo-local project references, built, and checked by `doctor`/`validate`
- CLI scaffold/package version output now derives from assembly package metadata and matches the preview package version instead of the previous hardcoded `1.0.0`
- current source is now `0.1.0-preview.8`; it updates SDK roll-forward for newer .NET 10 preview SDKs and needs a publish/validation run before replacing `0.1.0-preview.7` as the latest published-feed validated version
- GitHub Packages private-preview workflow now publishes both the runtime package and CLI tool package and uploads both package artifacts; workflow run `24443709690` passed for `0.1.0-preview.7` from commit `5809ddcfda282e5a70bd89649a901e9599d89ac4`
- published-feed real-app validation now proves `AgentBlazor` and `AgentBlazor.Cli` `0.1.0-preview.7` install from GitHub Packages, scaffold, restore, build, pass `doctor`/`validate`, and render AgentBlazor assets in external Blazor apps including CSP/nonce-aware shells
- WebAssembly companion-client UI paths are detected and left review-first instead of auto-writing browser-client AgentBlazor chat code into browser-only projects
- scaffolded MudBlazor imports are scoped to the layout provider file instead of being added globally, avoiding QuickGrid `PropertyColumn` tag conflicts
- existing-app scaffold now respects plan-specific startup edits, supports composed service chains such as `.AddServerUI(...)`, preserves existing MudBlazor service registration, targets discovered root pages, handles async `RunAsync`, and preserves UTF-8 BOMs on edited files

## Current State

### Free Tier - Private Preview Ready

- Core runtime works in the adapter-first path.
- The shipped component surface builds and has rendered component test coverage.
- The CLI can scaffold standard Blazor Web App hosts and reports WebAssembly companion-client UI work as manual review.
- The starter sample is the golden-path local reference.
- Package metadata and preview versioning exist.
- Remaining before broad production:
  - run the private-preview package through a small external validation group
  - sign off demo/e2e if the demo site is part of the public production story

### Paid Tier - Feature-Complete Preview, Not Production-Proven

- SQLite persistence exists for action history, inspector runs, audit, analytics, and suggestions.
- `AgentProDashboard` exists.
- License format validation and service replacement exist.
- Remaining before broad production:
  - prove paid storage under realistic multi-user app usage
  - decide the public promise around persistent intelligence and personalization
  - add a paid-tier guide with operational expectations and limitations
  - validate upgrade/downgrade behavior and failure modes

---

## Phase 0: Real-App Validation Gate

Target: prove the current private-preview build against real applications before publishing production claims.

Validation log:

- 2026-04-12 fresh standard Blazor Web App: `scaffold --approve`, `dotnet build`, `doctor`, and `validate` passed; readiness `9/9`, validation `3/3`. Workdir: `/tmp/agentblazor-prod-validation-standard-scoped-imports-20260412202054`.
- 2026-04-12 fresh Blazor Web App with WebAssembly interactivity: server host scaffold and build passed; `doctor`/`validate` intentionally reported client layout/chat manual-review warnings because the current AgentBlazor UI package boundary is server-first. Workdir: `/tmp/agentblazor-prod-validation-webapp-wasm-safe-20260412201455`.
- 2026-04-12 external official Microsoft sample `dotnet/blazor-samples/10.0/BlazorSample_BlazorWebApp`: baseline build passed; scaffold/build/doctor/validate passed after scoping MudBlazor imports to `MainLayout.razor`; readiness `9/9`, validation `3/3`. Workdir: `/tmp/agentblazor-external-validation-quickgridfix-20260412201933/blazor-samples/10.0/BlazorSample_BlazorWebApp`.
- 2026-04-12 independent real-world OSS app `https://github.com/neozhu/CleanArchitectureWithBlazorServer.git` at `4ef0b7c599be97d93049028e7b9a641f237cc5c7`: baseline restore/build passed; scaffold preview exposed and drove fixes for composed service-chain insertion, duplicate MudBlazor registration avoidance, async `RunAsync` endpoint mapping, existing root-page targeting, and BOM preservation; scaffold approve/build passed; `doctor` readiness `9/9`; `validate` readiness `9/9`, validation `3/3`. Build retained upstream warnings, including `MimeKit` NU1902, nullable/analyzer warnings, MudBlazor analyzer warnings, and SQLite RID warnings. Workdir: `/tmp/agentblazor-realworld-validation-20260412210804/CleanArchitectureWithBlazorServer`.
- 2026-04-13 independent real-world OSS framework `https://github.com/oqtane/oqtane.framework.git` at `6299412fa5806169e7d93c4a3e43e0467a28688b`: baseline restore/build passed with `0` warnings and `0` errors; scaffold preview correctly detected an Oqtane-style advanced host and limited apply to safe package/project references plus the starter workflow file; preview exposed and drove the project-file writer fix so XML declarations and MSBuild `->` target expressions are preserved; scaffold approve/rebuild passed with `0` warnings and `0` errors. `doctor` intentionally reported readiness `1/9` with manual-review startup/shell/layout/chat items; `validate` reported manifest checks `3/3` plus the same expected manual-review items. Workdir: `/tmp/agentblazor-oqtane-validation-20260413152042/oqtane.framework`.
- 2026-04-13 independent real-world hosted WebAssembly app `https://github.com/sandrohanea/whisper.net.git` at `6fb7ba7706ccfdbe1f54b6b6ff96302593e52505`, target `examples/BlazorApp/BlazorApp/BlazorApp.csproj`: baseline build required `dotnet workload restore` to install `wasm-tools`, then restore/build passed with one upstream Razor warning for `ReconnectModal`; scaffold preview/approve patched only the server host project startup, shell assets, imports, references, and starter workflow while leaving client layout/chat work as manual review; rebuild passed with the same upstream warning; `doctor` readiness `7/9` with MudBlazor provider and chat surface warnings; `validate` readiness `7/9`, validation `3/5` with the same manual-review warnings. Workdir: `/tmp/agentblazor-hostedwasm-validation-alt-20260413172633/whisper.net`.
- 2026-04-13 hosted WebAssembly candidate `https://github.com/davidfowl/TodoApp.git` at `307a1eadbbd77a3004c318f2377e4818bc400af6` was not used for scaffold validation because its `global.json` pins SDK `9.0.100`, while this environment only has .NET SDK `10.0.104` installed.
- 2026-04-13 real OpenAI-backed workflow validation used `demo/AgentBlazor.Demo/appsettings.Development.json` provider config without printing the API key. `dotnet test tests/AgentBlazor.IntegrationTests/AgentBlazor.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ProviderAdapterIntegrationTests` passed `30/30`. Coverage included OpenAI chat response, semantic workflow capability invocation, approval-required workflow execution, blocked/recovery/retry workflow execution, streaming/reconnect replay, cancellation, concurrent workflow runs, and deterministic session-state continuity.
- 2026-04-13 local package validation packed `AgentBlazor.0.1.0-preview.1.nupkg` and `AgentBlazor.Cli.0.1.0-preview.1.nupkg` to `/tmp/agentblazor-package-validation-20260413/packages`. The `AgentBlazor` package contained the bundled `AgentBlazor`, `AgentBlazor.Core`, `AgentBlazor.Hosting`, `AgentBlazor.Licensing`, and `AgentBlazor.ProviderAdapters` assemblies plus static web assets. A clean Blazor Web App in `/tmp/agentblazor-package-validation-20260413/work/PackageSmoke` installed the local tool and package with no repo-local project references, ran `init`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, and `dotnet build`; build passed with `0` warnings and `0` errors, `doctor` readiness passed `9/9`, and `validate` readiness passed `9/9` with validation `3/3`. A separate packaged runtime smoke runner in `/tmp/agentblazor-package-validation-20260413/work/PackageRuntimeSmoke` referenced `AgentBlazor` `0.1.0-preview.1` only, used the real OpenAI provider config without printing the API key, and completed normal and streaming semantic workflow calls with `PACKAGE_SMOKE_OK`; streaming produced `24` events. This validation exposed and fixed the CLI's stale `1.0.0` scaffold/display version.
- 2026-04-13 local package validation for `0.1.0-preview.2` packed `AgentBlazor`, `AgentBlazor.Cli`, and internal dependency packages to `/tmp/agentblazor-package-validation-preview2-20260413/packages`. A clean Blazor Web App in `/tmp/agentblazor-package-validation-preview2-20260413/work/PackageSmoke` installed `AgentBlazor` and the local CLI tool from the isolated feed, confirmed `agentblazor --version` reported `0.1.0-preview.2`, ran `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, `dotnet build`, `doctor`, and `validate`. Build passed with `0` warnings and `0` errors; `doctor` readiness passed `9/9`; `validate` readiness passed `9/9` with validation `3/3`. This validation exposed and fixed a package-first onboarding gap where an app with `AgentBlazor` already installed still needed the scaffold planner to add a direct `MudBlazor` package reference.
- 2026-04-13 GitHub Packages published-feed preflight confirmed the target feed is `https://nuget.pkg.github.com/ashpeterson/index.json`, but the local environment had no feed credentials at that time. The publish workflow was hardened to publish both `AgentBlazor` and `AgentBlazor.Cli`.
- 2026-04-14 GitHub Packages workflow `publish-github-packages-preview` run `24420548131` passed on commit `193ccdfc92f2b6e618b7dafa6e6228cfe2597171` for `0.1.0-preview.3` and uploaded artifact `agentblazor-packages-0.1.0-preview.3`. A clean Blazor Web App in `/tmp/agentblazor-github-consumer-validation-preview3-20260414201600/PackageConsumer` installed `AgentBlazor` and `AgentBlazor.Cli` from the GitHub Packages feed with isolated NuGet cache/tool paths, confirmed `agentblazor --version` reported `0.1.0-preview.3`, ran `init --non-interactive`, `scaffold --diff`, `scaffold --approve`, `dotnet restore`, and `dotnet build` with `0` warnings and `0` errors, then passed `doctor` readiness `9/9` and `validate` readiness `9/9`, validation `3/3`. Runtime HTTP smoke passed with a placeholder `OpenAI__ApiKey`, rendering the home page, `AgentChatWidget`, and AgentBlazor/MudBlazor static assets. Later real-app validation exposed a dependency-float issue in `0.1.0-preview.3`; use `0.1.0-preview.7` or later for private-preview testing.
- 2026-04-15 real-app package validation against `neozhu/CleanArchitectureWithBlazorServer` exposed that `0.1.0-preview.3` used open lower-bound Microsoft Agents dependency ranges. The app restored `Microsoft.Agents.AI` `1.1.0`, causing startup to fail with `TypeLoadException` for `SerializeSessionCoreAsync`. The first attempted fix pinned the Microsoft Agents package family exactly, which proved too strict for apps already using Agents 1.1.
- 2026-04-15 GitHub Packages workflow run `24440492708` succeeded for `0.1.0-preview.4`, but published-feed validation proved the feed version was an older immutable package from commit `45da291b84441c75e50af4c16cbed33ccb31cc5d`. The stale package lacked current `AgentBlazor.App` semantic workflow APIs and failed real-app scaffold build. The publish workflow now fails on duplicate package versions instead of using `--skip-duplicate`.
- 2026-04-15 published-feed validation for `0.1.0-preview.5` confirmed the package was current, but `neozhu/CleanArchitectureWithBlazorServer` already depends on `Microsoft.Agents.AI.OpenAI` `1.1.0`; the exact `1.0.0-preview.260209.1` pin caused `NU1107`. Current source moved to the Microsoft Agents 1.1 API family and implements the new async session serializer.
- 2026-04-15 GitHub Packages workflow run `24441459326` passed for `0.1.0-preview.6` on commit `13336a8779c761a340472ad2f1530dd3bdb68c12`. Published-feed validation inspected the downloaded package and confirmed current commit metadata, Microsoft Agents 1.1 dependencies, `SerializeSessionCoreAsync`, and current semantic workflow APIs. `neozhu/CleanArchitectureWithBlazorServer` at `4ef0b7c599be97d93049028e7b9a641f237cc5c7` installed `AgentBlazor` and `AgentBlazor.Cli` `0.1.0-preview.6` from GitHub Packages with isolated cache/tool paths; `agentblazor --version`, `init`, `scaffold --diff`, `scaffold --approve`, post-scaffold restore/build, `doctor` readiness `9/9`, and `validate` readiness `9/9`, validation `3/3` passed. Runtime HTTP smoke passed with `dotnet run --no-launch-profile --urls http://127.0.0.1:5197` and confirmed AgentBlazor static assets in the rendered home page. Build retained upstream app warnings but had `0` errors. Workdir: `/tmp/agentblazor-published-realapp-cleanarch-preview6-20260415071751/CleanArchitectureWithBlazorServer`.
- 2026-04-15 external GitHub project validation against `https://github.com/damienbod/BlazorSecurityNet10.git` at `682213b40201f3ce8000b10287492de4b6242d66` exposed a CSP nonce gap in `0.1.0-preview.6`: the app's shell used nonce-aware assets and returned a nonce-only `script-src` policy, but scaffolded MudBlazor/AgentBlazor assets did not copy the existing nonce. The CLI now preserves existing `nonce="..."` attributes on inserted link/script assets.
- 2026-04-15 GitHub Packages workflow run `24443709690` passed for `0.1.0-preview.7` on commit `5809ddcfda282e5a70bd89649a901e9599d89ac4`. Published-feed validation against `damienbod/BlazorSecurityNet10` installed `AgentBlazor` and `AgentBlazor.Cli` `0.1.0-preview.7`, confirmed `agentblazor --version`, ran `init`, `scaffold --diff`, `scaffold --approve`, post-scaffold restore/build, `doctor` readiness `9/9`, and `validate` readiness `9/9`, validation `3/3`. Runtime HTTP smoke passed with `dotnet run --no-launch-profile --urls http://127.0.0.1:5292`; rendered MudBlazor and AgentBlazor static assets carried the generated CSP nonce and the response included the nonce-based `Content-Security-Policy` header. Workdir: `/tmp/agentblazor-published-preview7-damienbod/BlazorApp`.
- 2026-04-15 source commit `c288c21666beba430ebf21980c3e96488721316c` bumped the next preview to `0.1.0-preview.8`, changed SDK roll-forward to `latestMinor`, and updated `Microsoft.AspNetCore.App.Internal.Assets` lockfiles to `10.0.5`. This is the next package candidate; run the GitHub Packages workflow and published-feed real-app validation before marking it complete.

Exit criteria:

- Test the CLI on at least three external Blazor apps:
  - one standard Blazor Web App - started; official Microsoft sample passed
  - one hosted WebAssembly server+client app - passed with `sandrohanea/whisper.net` while preserving WebAssembly client UI manual-review warnings
  - one legacy/custom host that should remain review-first - passed with Oqtane framework
- Also test at least two independent real-world OSS Blazor applications, not only templates or official documentation samples:
  - one larger app with auth, custom layouts, and multiple projects - passed with `CleanArchitectureWithBlazorServer`
  - one materially different host shape - passed with `oqtane.framework`
- For each app, capture:
  - repository URL and commit SHA
  - baseline `dotnet restore` / `dotnet build`
  - `scaffold --diff`
  - `scaffold --approve`
  - `dotnet build`
  - `doctor`
  - `validate`
  - the exact manual edits required, if any
- Run at least one real OpenAI-backed workflow end to end - passed:
  - capability invocation
  - approval-gated action
  - blocked/recovery result
  - streaming chat turn
- Record gaps as issues or roadmap items before moving to package release.

## Phase 1: Preview Package Release

### 1.1 NuGet Package Prep
- [x] Create package metadata (description, tags, icon)
- [x] Set up package versioning (0.1.0-preview.8 in Directory.Build.props)
- [x] Configure GitHub Actions for publish (publish-github-packages-preview.yml)
- [x] Configure GitHub Actions to publish both `AgentBlazor` and `AgentBlazor.Cli`
- [x] Test install from locally packed preview package in a clean project
- [x] Publish the preview package
- [x] Test install from the published preview package in a clean project
- [x] Test install of the published `AgentBlazor.Cli` tool from the target feed
- [x] Run `dotnet restore`, `dotnet build`, and a minimal runtime HTTP smoke from the published package

### 1.2 Documentation Polish
- [x] Review quickstart.md for accuracy
- [x] Add troubleshooting section
- [x] Add Pro tier features section
- [x] Add component reference table
- [x] Ensure demo runs out of box
- [x] Add a private-preview limitations section
- [x] Add a real-app CLI validation checklist
- [x] Add a provider configuration checklist for OpenAI, Azure OpenAI, and Ollama

### 1.3 Preview Ship
- [x] Run publish workflow with version 0.1.0-preview.7
- [ ] Run publish workflow with version 0.1.0-preview.8
- [ ] Share with a small validation group (external coordination gate; not a local code task)
- [ ] Do not announce as production-ready until Phase 2 exits cleanly (release governance gate)

**Deliverable:** Free tier on NuGet as a private-preview package for real-app validation.

---

## Phase 2: Production Pilot

Target: use AgentBlazor in a controlled production pilot with a known app owner watching logs and behavior.

Exit criteria:

- One real application runs AgentBlazor with a real provider and a real workflow.
- The host app owner signs off on:
  - install steps
  - runtime behavior
  - approval UX
  - rollback plan
  - logging/observability
- Known unsupported host shapes are documented.
- The CLI either patches safely or clearly reports review-first/manual work.
- No untriaged test failures remain in the non-demo suite.
- Demo/e2e is green if the public site is part of the release.

**Deliverable:** AgentBlazor is acceptable for production pilots with explicit support.

---

## Phase 3: Broad Production Release

Target: public claim that AgentBlazor is production-ready for the supported host paths.

Exit criteria:

- Published package install works from a clean machine.
- Quickstart works without repo-local source paths.
- CLI fresh-app and real-app validation are documented and current.
- OpenAI-backed workflow smoke test is repeatable.
- Provider endpoint/config validation has coverage.
- Middleware, streaming, scope, and approval-gated workflow coverage remains green.
- Release notes clearly state supported host shapes:
  - standard Blazor Web App
  - WebAssembly companion-client hosts as server-host scaffold plus client UI review-first
  - review-first legacy/custom hosts
  - unsupported/unclassified hosts

**Deliverable:** Public production release with a clear support boundary.

---

## Phase 4: Build Real Paid Value

The paid tier needs features that justify $29/seat/mo. Focus on things competitors don't have.

### 2.1 Usage Analytics Service ✅ COMPLETE

**Why:** Teams pay for visibility into how their agents are used.

**Status:** Implemented in `AgentBlazor.Core.Paid.Analytics`
- `IUsageAnalyticsService` interface
- `SqliteUsageAnalyticsService` - queries action_history for metrics
- `NullUsageAnalyticsService` - free tier no-op

Interface:

```csharp
public interface IUsageAnalyticsService
{
    // Core metrics
    Task<UsageSummary> GetSummaryAsync(DateRange range);
    Task<IReadOnlyList<ActionMetric>> GetTopActionsAsync(int limit = 20);
    Task<IReadOnlyList<AgentMetric>> GetAgentPerformanceAsync();

    // Trends
    Task<IReadOnlyList<DailyUsage>> GetDailyTrendsAsync(int days = 30);
    Task<double> GetSuccessRateAsync(DateRange range);
    Task<TimeSpan> GetAverageResponseTimeAsync(DateRange range);

    // Insights
    Task<IReadOnlyList<UsageAnomaly>> DetectAnomaliesAsync();
}
```

**Data models:**
```csharp
public record UsageSummary(
    int TotalActions,
    int UniqueUsers,
    int UniqueSessions,
    double SuccessRate,
    TimeSpan AvgResponseTime,
    IReadOnlyList<string> TopActions);

public record ActionMetric(
    string ActionId,
    int ExecutionCount,
    double SuccessRate,
    TimeSpan AvgDuration);

public record DailyUsage(
    DateOnly Date,
    int ActionCount,
    int UserCount,
    double SuccessRate);
```

**Implementation:**
- Query existing `SqliteActionHistoryStore`
- Add execution duration + success/failure tracking to `ActionHistoryEntry`
- Build aggregation queries
- No new infrastructure needed

**UI Component:**
- `<AgentAnalyticsDashboard />` - Embeddable Blazor component
- Shows charts, top actions, trends
- Uses MudBlazor charts

### 2.2 Audit Log ✅ COMPLETE

**Why:** Enterprise teams need compliance/accountability.

**Status:** Implemented in `AgentBlazor.Core.Paid.Audit`
- `IAuditLogService` interface with full query/export support
- `SqliteAuditLogService` - durable audit trail
- `NullAuditLogService` - free tier no-op

```csharp
public interface IAuditLogService
{
    Task LogAsync(AuditEvent evt);
    Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query);
    Task<Stream> ExportAsync(AuditQuery query, ExportFormat format);
}

public record AuditEvent(
    DateTimeOffset Timestamp,
    string UserId,
    string UserEmail,
    string ActionType,      // "action_executed", "approval_granted", "config_changed"
    string TargetId,
    string Details,
    string IpAddress);
```

**Features:**
- Queryable by user, date range, action type
- Export to CSV/JSON
- Retention policy (configurable days)

### 2.3 Smart Suggestions v2 ✅ COMPLETE

**Why:** Current implementation is trivial. Make it actually learn.

**Status:** Implemented in `AgentBlazor.Core.Paid.Suggestions`
- `ISmartSuggestionService` interface
- `SqliteSmartSuggestionService` - pattern-based + popularity + LLM hybrid
- `NullSmartSuggestionService` - free tier no-op

Implements pattern-based + LLM hybrid approach:

```csharp
public interface ISmartSuggestionService
{
    Task<IReadOnlyList<Suggestion>> GetSuggestionsAsync(SuggestionContext ctx);
}

public record SuggestionContext(
    string SessionId,
    string? UserId,
    string? CurrentRoute,
    IReadOnlyList<string> RecentActions,
    IReadOnlyDictionary<string, object>? PageState);

public record Suggestion(
    string Text,
    string ActionId,
    float Confidence,
    string Source);  // "pattern", "llm", "popular"
```

**Algorithm:**
1. **Pattern matching** (no LLM cost):
   - "Users who did A then B usually do C next"
   - Query historical sequences from action history
   - Weight by recency and frequency

2. **Popularity fallback**:
   - If no pattern match, suggest most common actions for current route

3. **LLM enhancement** (only when needed):
   - If confidence < threshold, ask LLM to rank/refine
   - Include user context in prompt

**Benefits:**
- Faster (pattern matching is instant)
- Cheaper (less LLM calls)
- Actually learns from usage

### 2.4 Team Configuration (2-3 days)

**Why:** Teams want shared agent configs, not per-developer setup.

```csharp
public interface ITeamConfigurationService
{
    Task<TeamConfig> GetConfigAsync(string teamId);
    Task SaveConfigAsync(string teamId, TeamConfig config);
    Task<IReadOnlyList<ConfigChange>> GetChangeHistoryAsync(string teamId);
}

public record TeamConfig(
    string TeamId,
    AgentSettings DefaultAgentSettings,
    ApprovalPolicySettings ApprovalDefaults,
    IReadOnlyList<string> AllowedModels,
    IReadOnlyDictionary<string, string> CustomPromptOverrides);
```

**Features:**
- Centralized agent configuration
- Change history (who changed what)
- Override defaults per-agent

---

## Phase 3: Polish & Ship Paid (3-5 days)

### 3.1 Paid Dashboard Component ✅ COMPLETE
- `<AgentProDashboard />` - Single component showing:
  - Usage analytics (summary, trends, anomalies)
  - Recent audit events (with filtering and export)
  - Discovered action patterns
  - Agent performance metrics

Location: `src/AgentBlazor.Components/Dashboard/AgentProDashboard.razor`

### 3.2 License Enforcement
- Server-side license validation (optional)
- Graceful degradation if license expires
- Clear upgrade prompts in free tier

### 3.3 Documentation
- Paid features guide
- Analytics interpretation
- Audit log compliance guide

### 3.4 Pricing Page
- Update landing with clear tier comparison
- Add "Upgrade" CTA in free tier UI

---

## Implementation Order

| Week | Focus | Deliverables |
|------|-------|--------------|
| 1 | Free tier ship + Analytics foundation | NuGet publish, `IUsageAnalyticsService` |
| 2 | Analytics UI + Audit log | `<AgentAnalyticsDashboard />`, `IAuditLogService` |
| 3 | Smart suggestions + Team config | Pattern-based suggestions, `ITeamConfigurationService` |
| 4 | Polish + Ship paid | Dashboard, docs, pricing page |

---

## What This Gives You

### Free Tier (ships Week 1)
- Full agent runtime
- All components
- Dev tools
- CLI

### Paid Tier @ $29/seat/mo (ships Week 4)
- **Usage Analytics** - See how agents are used, success rates, trends
- **Audit Log** - Compliance-ready activity tracking with export
- **Smart Suggestions** - Pattern-based learning (not just LLM prompts)
- **Team Configuration** - Shared settings, change history
- **Pro Dashboard** - Single view of everything

This is real value that teams will pay for:
- Analytics = visibility leadership wants
- Audit = compliance teams need
- Smart suggestions = genuine productivity improvement
- Team config = operational efficiency

---

## What's NOT in This Plan

Explicitly deferred to future:
- SSO/SAML (Enterprise tier)
- Role-based access control (Enterprise tier)
- Custom model fine-tuning (too complex)
- Multi-tenant architecture (not needed yet)

---

## Success Criteria

### Free Tier
- [ ] 100+ NuGet downloads in first month
- [ ] 5+ GitHub stars
- [ ] 2+ external blog posts/tutorials

### Paid Tier
- [ ] 3+ paying customers in first quarter
- [ ] $500+ MRR within 90 days
- [ ] <5% churn rate

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Analytics too slow | Pre-aggregate daily, query aggregates |
| LLM costs spike | Pattern-first, LLM-fallback approach |
| No one pays | Validate with 3 beta customers before full launch |
| Scope creep | Strict 4-week timeline, defer everything else |
