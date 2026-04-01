# Production Plan

Last updated: 2026-04-01

Honest assessment and actionable plan to ship both tiers.

## Current State

### Free Tier - 90% Ready
- Core runtime works
- 14 components work
- Demo proves value
- CLI tool ships
- Missing: NuGet publish, minor polish

### Paid Tier - 90% Ready ✅ UPDATED
- ✅ SQLite persistence (action history, inspector runs)
- ✅ Usage Analytics Service (`IUsageAnalyticsService`)
- ✅ Audit Log Service (`IAuditLogService`)
- ✅ Smart Suggestions with pattern matching (`ISmartSuggestionService`)
- ✅ Execution metrics (duration, success/failure tracking)
- ✅ Dashboard UI component (`AgentProDashboard`)
- Missing: Documentation polish

---

## Phase 1: Ship Free Tier (1-2 days)

### 1.1 NuGet Package Prep
- [ ] Create package metadata (description, tags, icon)
- [ ] Set up package versioning (0.1.0-preview)
- [ ] Configure GitHub Actions for publish
- [ ] Test install from NuGet in clean project

### 1.2 Documentation Polish
- [ ] Review quickstart.md for accuracy
- [ ] Add troubleshooting section
- [ ] Ensure demo runs out of box

### 1.3 Ship It
- [ ] Publish to NuGet as preview
- [ ] Announce on social/blog

**Deliverable:** Free tier on NuGet, usable by anyone.

---

## Phase 2: Build Real Paid Value (2-3 weeks)

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
