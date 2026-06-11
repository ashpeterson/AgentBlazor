using System.Globalization;
using System.Text;
using AgentBlazor.Cli.Analysis.Models;
using AgentBlazor.Cli.Analysis.WorkflowSuggestions;

namespace AgentBlazor.Cli.Analysis.Generation;

public sealed class AnalysisReportGenerator
{
    public async Task GenerateAsync(
        ProjectModel model,
        string outputPath,
        InstallReadinessReport? readiness = null,
        WorkflowSuggestionSet? workflowSuggestions = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = GenerateMarkdown(model, readiness, workflowSuggestions);
        await File.WriteAllTextAsync(outputPath, content, ct).ConfigureAwait(false);
    }

    public string GenerateMarkdown(
        ProjectModel model,
        InstallReadinessReport? readiness = null,
        WorkflowSuggestionSet? workflowSuggestions = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sb = new StringBuilder();
        sb.AppendLine("# AgentBlazor Analysis");
        sb.AppendLine();
        sb.AppendLine($"Generated: {model.GeneratedAtUtc:O}");
        sb.AppendLine($"Application: {ValueOrUnknown(model.AppName)}");
        sb.AppendLine($"Host project: {ValueOrUnknown(model.BlazorHostProject)}");
        sb.AppendLine($"Schema version: {model.SchemaVersion}");
        sb.AppendLine();

        WriteSummary(sb, model, readiness);
        WriteTopRecommendations(sb, model, workflowSuggestions);
        WriteInstallBlockers(sb, readiness);
        WriteRoutes(sb, model);
        WriteCapabilities(sb, model);
        WriteWorkflowClusters(sb, model);
        WriteWorkflowSuggestions(sb, model, workflowSuggestions);
        if (readiness is not null)
        {
            WriteReadiness(sb, readiness);
        }

        WriteServices(sb, model);
        WriteNextSteps(sb, model, readiness, workflowSuggestions);
        return sb.ToString();
    }

    private static void WriteSummary(StringBuilder sb, ProjectModel model, InstallReadinessReport? readiness)
    {
        var reportableActions = model.Actions.Where(AnalysisModelFilters.IsDeveloperFacingAction).ToList();
        var confirmedCount = reportableActions.Count(action => action.ExposureMode == ActionExposureMode.Confirmed);
        var discoveredCount = reportableActions.Count(action => action.ExposureMode == ActionExposureMode.Suggested);
        var reportableServiceCount = model.Services.Count(service => AnalysisModelFilters.IsDeveloperFacingService(service, model));

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Projects scanned: {model.Projects.Count}");
        sb.AppendLine($"- Routes discovered: {model.Routes.Count}");
        sb.AppendLine($"- Developer-facing services discovered: {reportableServiceCount}");
        sb.AppendLine($"- Confirmed actions: {confirmedCount}");
        sb.AppendLine($"- Discovered candidate actions: {discoveredCount}");
        sb.AppendLine($"- Workflow clusters discovered: {model.WorkflowClusters.Count}");
        if (readiness is not null)
        {
            sb.AppendLine($"- Install readiness: {readiness.PassCount} passed, {readiness.WarningCount} warnings, {readiness.MissingCount} missing");
            sb.AppendLine($"- Host shape: {readiness.HostShape.Title}");
        }

        var adoptionTotal = confirmedCount + discoveredCount;
        if (adoptionTotal > 0)
        {
            sb.AppendLine($"- AgentBlazor action adoption: {confirmedCount} confirmed, {discoveredCount} candidate actions not yet exposed");
        }

        sb.AppendLine();
    }

    private static void WriteTopRecommendations(
        StringBuilder sb,
        ProjectModel model,
        WorkflowSuggestionSet? workflowSuggestions)
    {
        sb.AppendLine("## Top Recommendations");
        sb.AppendLine();

        if (workflowSuggestions?.Suggestions.Count > 0)
        {
            var promotedSuggestions = workflowSuggestions.Suggestions
                .Where(suggestion => IsProcessWorkflowSuggestion(suggestion, model))
                .OrderBy(suggestion => GetSuggestionWorkflowRank(suggestion, model))
                .ThenBy(suggestion => GetSuggestionRiskRank(suggestion, model))
                .ThenByDescending(suggestion => suggestion.Confidence)
                .Take(5)
                .ToList();

            if (promotedSuggestions.Count == 0)
            {
                var preferredClusters = GetPreferredWorkflowClusters(model, take: 5);
                if (preferredClusters.Count > 0)
                {
                    sb.AppendLine("The validated LLM suggestions were data, integration, admin, or infrastructure surfaces. Use these static workflow clusters as the first workflow candidates instead.");
                    var fallbackDemotedSuggestionCount = workflowSuggestions.Suggestions.Count;
                    if (fallbackDemotedSuggestionCount > 0)
                    {
                        sb.AppendLine($"{fallbackDemotedSuggestionCount} supporting data, integration, admin, or infrastructure suggestion(s) were not promoted to top recommendations.");
                    }

                    WriteClusterRecommendationTable(sb, preferredClusters);
                    return;
                }

                sb.AppendLine("No process-oriented workflow candidates were found. The validated LLM suggestions are data, integration, admin, or infrastructure surfaces; review them below, but treat them as supporting context rather than first workflow work.");
                sb.AppendLine();
                return;
            }

            var demotedSuggestionCount = workflowSuggestions.Suggestions.Count -
                workflowSuggestions.Suggestions.Count(suggestion => IsProcessWorkflowSuggestion(suggestion, model));

            sb.AppendLine("These are the highest-signal process-oriented workflow candidates. Start here before reading the full service inventory.");
            if (demotedSuggestionCount > 0)
            {
                sb.AppendLine($"{demotedSuggestionCount} supporting data, integration, admin, or infrastructure suggestion(s) were not promoted to top recommendations.");
            }

            sb.AppendLine();
            sb.AppendLine("| Workflow | Risk | Confidence | Existing methods | Why it matters |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var suggestion in promotedSuggestions)
            {
                var methods = suggestion.Methods.Count == 0
                    ? "-"
                    : string.Join("<br>", suggestion.Methods.Select(method => $"`{EscapeTableCell(method.Service)}.{EscapeTableCell(method.Method)}`"));
                var reasoning = string.IsNullOrWhiteSpace(suggestion.Reasoning)
                    ? suggestion.Description
                    : suggestion.Reasoning;
                sb.AppendLine($"| {EscapeTableCell(suggestion.Name)} | {ActionRisk.Describe(GetSuggestionRiskBand(suggestion, model))} | {suggestion.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {methods} | {EscapeTableCell(TrimForTable(reasoning, 180))} |");
            }

            sb.AppendLine();
            return;
        }

        var clusterCandidates = GetPreferredWorkflowClusters(model, take: 5);
        if (clusterCandidates.Count > 0)
        {
            sb.AppendLine("LLM workflow suggestions were not requested. These are the highest-confidence static workflow clusters.");
            WriteClusterRecommendationTable(sb, clusterCandidates);
            return;
        }

        var candidates = GetStaticWorkflowCandidates(model, take: 5);
        if (candidates.Count == 0)
        {
            sb.AppendLine("No high-confidence workflow candidates were discovered yet.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("LLM workflow suggestions were not requested. These are the highest-confidence static candidates.");
        sb.AppendLine();
        sb.AppendLine("| Candidate | Risk | Confidence | Existing method | Why it matters |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var action in candidates)
        {
            var reasoning = string.IsNullOrWhiteSpace(action.Summary)
                ? action.Classification.ToString()
                : action.Summary;
            sb.AppendLine($"| {EscapeTableCell(action.Name)} | {ActionRisk.Describe(ActionRisk.GetRiskBand(action))} | {action.Score.ToString("0.00", CultureInfo.InvariantCulture)} | `{EscapeTableCell(action.SourceService)}.{EscapeTableCell(action.MethodName)}` | {EscapeTableCell(TrimForTable(reasoning, 180))} |");
        }

        sb.AppendLine();
    }

    private static void WriteClusterRecommendationTable(StringBuilder sb, IReadOnlyList<WorkflowClusterModel> clusters)
    {
        sb.AppendLine();
        sb.AppendLine("| Workflow | Risk | Confidence | Existing methods | Why it matters |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var cluster in clusters)
        {
            var methods = cluster.Methods.Count == 0
                ? "-"
                : string.Join("<br>", cluster.Methods.Select(method => $"`{EscapeTableCell(method.Service)}.{EscapeTableCell(method.Method)}`"));
            sb.AppendLine($"| {EscapeTableCell(cluster.Name)} | {EscapeTableCell(cluster.Risk)} | {cluster.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {methods} | {EscapeTableCell(TrimForTable(cluster.Summary, 180))} |");
        }

        sb.AppendLine();
    }

    private static void WriteWorkflowClusters(StringBuilder sb, ProjectModel model)
    {
        sb.AppendLine("## Workflow Clusters");
        sb.AppendLine();

        if (model.WorkflowClusters.Count == 0)
        {
            sb.AppendLine("No multi-method workflow clusters were inferred from the static analysis model.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Static pre-pass clusters that are sent to the workflow-suggestion model as multi-step process context.");
        sb.AppendLine();
        sb.AppendLine("| Cluster | Origin | Risk | Confidence | Routes | Methods | Evidence |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var cluster in model.WorkflowClusters
            .OrderByDescending(cluster => cluster.Confidence)
            .ThenBy(cluster => cluster.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12))
        {
            var routes = cluster.RouteHints.Count == 0
                ? "-"
                : string.Join("<br>", cluster.RouteHints.Select(route => $"`{EscapeTableCell(route)}`"));
            var methods = cluster.Methods.Count == 0
                ? "-"
                : string.Join("<br>", cluster.Methods.Select(method => $"`{EscapeTableCell(method.Service)}.{EscapeTableCell(method.Method)}` ({EscapeTableCell(method.Role)})"));
            var evidence = cluster.Evidence.Count == 0
                ? "-"
                : string.Join("<br>", cluster.Evidence.Take(3).Select(item => EscapeTableCell(TrimForTable(item, 120))));

            sb.AppendLine($"| {EscapeTableCell(cluster.Name)} | {EscapeTableCell(cluster.Origin)} | {EscapeTableCell(cluster.Risk)} | {cluster.Confidence.ToString("0.00", CultureInfo.InvariantCulture)} | {routes} | {methods} | {evidence} |");
        }

        sb.AppendLine();
    }

    private static void WriteInstallBlockers(StringBuilder sb, InstallReadinessReport? readiness)
    {
        if (readiness is null)
        {
            sb.AppendLine("## Install Blockers");
            sb.AppendLine();
            sb.AppendLine("Install readiness was not evaluated for this analysis run.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("## Install Blockers");
        sb.AppendLine();

        var blockers = readiness.Checks
            .Where(check => check.Status != InstallReadinessStatus.Pass)
            .ToList();
        if (blockers.Count == 0)
        {
            sb.AppendLine("No install blockers were found.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"Fix these {blockers.Count} setup item(s) before wiring generated workflows into the app.");
        sb.AppendLine();
        sb.AppendLine("| Status | Check | Fix |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var check in blockers)
        {
            sb.AppendLine($"| {check.Status} | {EscapeTableCell(check.Title)} | {EscapeTableCell(check.SuggestedFix ?? check.Message)} |");
        }

        sb.AppendLine();
    }

    private static void WriteRoutes(StringBuilder sb, ProjectModel model)
    {
        sb.AppendLine("## Routes And Pages");
        sb.AppendLine();
        if (model.Routes.Count == 0)
        {
            if (model.Components.Count > 0)
            {
                sb.AppendLine($"No Razor `@page` routes were discovered. {model.Components.Count} Razor components were discovered, so this app likely uses component-driven or framework-managed routing; route-to-action linking is unavailable for those components.");
            }
            else
            {
                sb.AppendLine("No Razor routes were discovered.");
            }

            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Route | Component | File | Suggested actions |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var route in model.Routes.OrderBy(route => route.Template))
        {
            var page = model.Pages.FirstOrDefault(page => page.Route == route.Template);
            var actions = page?.SuggestedActions.Count > 0
                ? string.Join(", ", page.SuggestedActions
                    .Where(actionId => model.Actions.Any(action =>
                        action.Id == actionId &&
                        AnalysisModelFilters.IsDeveloperFacingAction(action)))
                    .Take(5)
                    .Select(EscapeTableCell))
                : "-";
            if (string.IsNullOrWhiteSpace(actions))
            {
                actions = "-";
            }
            sb.AppendLine($"| `{EscapeTableCell(route.Template)}` | `{EscapeTableCell(route.ComponentName)}` | `{EscapeTableCell(route.ComponentFile)}` | {actions} |");
        }

        var linkedRouteCount = model.Pages
            .Where(page => page.SuggestedActions.Any(actionId => model.Actions.Any(action =>
                action.Id == actionId &&
                AnalysisModelFilters.IsDeveloperFacingAction(action))))
            .Select(page => page.Route)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (model.Routes.Count > 5 && linkedRouteCount * 2 < model.Routes.Count)
        {
            sb.AppendLine();
            sb.AppendLine($"> Route quality note: only {linkedRouteCount} of {model.Routes.Count} route(s) mapped to candidate actions. This can be normal for dynamic, multi-tenant, or component-composed apps; use the workflow recommendations above as the primary signal.");
        }

        sb.AppendLine();
    }

    private static void WriteCapabilities(StringBuilder sb, ProjectModel model)
    {
        var confirmedActions = model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .OrderBy(action => action.SourceService)
            .ThenBy(action => action.MethodName)
            .ToList();

        sb.AppendLine("## Existing Capabilities");
        sb.AppendLine();
        if (confirmedActions.Count == 0)
        {
            sb.AppendLine("No confirmed `[AgentCapability]` / `[AgentAction]` actions were discovered.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Capability class | Action method | Approval | File |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var action in confirmedActions)
        {
            var approval = action.RequiresApproval ? "required" : "not required";
            sb.AppendLine($"| `{EscapeTableCell(action.SourceService)}` | `{EscapeTableCell(action.MethodName)}` | {approval} | `{EscapeTableCell(action.FilePath)}` |");
        }

        sb.AppendLine();
    }

    private static void WriteServices(StringBuilder sb, ProjectModel model)
    {
        sb.AppendLine("## Service Inventory");
        sb.AppendLine();
        var developerFacingServices = model.Services
            .Where(service => AnalysisModelFilters.IsDeveloperFacingService(service, model))
            .OrderBy(service => service.TypeName)
            .ToList();

        if (developerFacingServices.Count == 0)
        {
            sb.AppendLine("No developer-facing service-like classes were discovered.");
            sb.AppendLine();
            return;
        }

        var classifications = developerFacingServices
            .Select(service => ClassifyService(service, model))
            .ToList();
        sb.AppendLine($"Detailed inventory for {developerFacingServices.Count} service-like classes. Use this section to audit the model; use Top Recommendations for what to build first.");
        sb.AppendLine();
        sb.AppendLine("| Classification | Count | Guidance |");
        sb.AppendLine("| --- | ---: | --- |");
        foreach (var group in classifications
            .GroupBy(classification => classification.Category)
            .OrderBy(group => GetServiceCategoryRank(group.Key)))
        {
            sb.AppendLine($"| {ServiceCategoryLabel(group.Key)} | {group.Count()} | {EscapeTableCell(ServiceCategoryGuidance(group.Key))} |");
        }

        sb.AppendLine();

        foreach (var service in developerFacingServices)
        {
            var classification = ClassifyService(service, model);
            sb.AppendLine($"### `{service.TypeName}`");
            sb.AppendLine();
            sb.AppendLine($"- Lifetime: {service.Lifetime}");
            sb.AppendLine($"- File: `{service.FilePath}`");
            sb.AppendLine($"- Classification: {ServiceCategoryLabel(classification.Category)}");
            sb.AppendLine($"- Agent fit: {classification.AgentFit}");
            if (service.Methods.Count == 0)
            {
                sb.AppendLine("- Public methods: none discovered");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("- Public methods:");
            foreach (var method in service.Methods
                .Where(method => AnalysisModelFilters.IsDeveloperFacingMethod(method.Name))
                .OrderBy(method => method.Name))
            {
                var parameters = method.Parameters.Count == 0
                    ? ""
                    : string.Join(", ", method.Parameters.Select(parameter => $"{parameter.TypeName} {parameter.Name}"));
                sb.AppendLine($"  - `{method.Name}({parameters})` -> `{method.ReturnType}`");
            }

            sb.AppendLine();
        }
    }

    private static void WriteWorkflowSuggestions(
        StringBuilder sb,
        ProjectModel model,
        WorkflowSuggestionSet? workflowSuggestions)
    {
        sb.AppendLine("## Workflow Suggestions");
        sb.AppendLine();

        if (workflowSuggestions is not null)
        {
            if (!string.IsNullOrWhiteSpace(workflowSuggestions.Model))
            {
                sb.AppendLine($"Model: `{workflowSuggestions.Model}`");
                sb.AppendLine();
            }

            if (workflowSuggestions.Suggestions.Count > 0)
            {
                foreach (var suggestion in workflowSuggestions.Suggestions
                    .OrderBy(suggestion => GetSuggestionWorkflowRank(suggestion, model))
                    .ThenBy(suggestion => GetSuggestionRiskRank(suggestion, model))
                    .ThenByDescending(suggestion => suggestion.Confidence))
                {
                    sb.AppendLine($"### {suggestion.Name}");
                    sb.AppendLine();
                    sb.AppendLine(suggestion.Description);
                    sb.AppendLine();
                    sb.AppendLine($"- Confidence: {suggestion.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}");
                    sb.AppendLine($"- Risk: {ActionRisk.Describe(GetSuggestionRiskBand(suggestion, model))}");
                    if (!string.IsNullOrWhiteSpace(suggestion.CapabilityClass))
                    {
                        sb.AppendLine($"- Suggested capability class: `{suggestion.CapabilityClass}`");
                    }

                    var referencesMutationLikelyMethod = ReferencesMutationLikelyMethod(suggestion, model);
                    if (referencesMutationLikelyMethod)
                    {
                        sb.AppendLine("- Approval guidance: this workflow references mutating methods. Mark generated `[AgentAction]` methods with `RequiresApproval = true` unless a human-reviewed policy says the action is safe to run automatically.");
                        sb.AppendLine($"- Suggested attribute: `[AgentAction(\"{EscapeAttributeValue(suggestion.Name)}\", RequiresApproval = true)]`");
                    }

                    if (suggestion.Methods.Count > 0)
                    {
                        sb.AppendLine("- Existing methods used:");
                        foreach (var method in suggestion.Methods)
                        {
                            sb.AppendLine($"  - `{method.Service}.{method.Method}`");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(suggestion.Reasoning))
                    {
                        sb.AppendLine($"- Reasoning: {suggestion.Reasoning}");
                    }

                    if (!string.IsNullOrWhiteSpace(suggestion.Code))
                    {
                        sb.AppendLine();
                        sb.AppendLine("```csharp");
                        sb.AppendLine(suggestion.Code.Trim());
                        sb.AppendLine("```");
                    }

                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("No validated LLM workflow suggestions were returned.");
                sb.AppendLine();
            }

            if (workflowSuggestions.Rejected.Count > 0)
            {
                sb.AppendLine("### Rejected Suggestions");
                sb.AppendLine();
                sb.AppendLine("These suggestions were filtered because they did not validate against the static analysis model.");
                sb.AppendLine();
                foreach (var rejected in workflowSuggestions.Rejected)
                {
                    sb.AppendLine($"- `{rejected.Name}`: {rejected.Reason}");
                }

                sb.AppendLine();
            }

            return;
        }

        var candidates = GetStaticWorkflowCandidates(model, take: 10);
        var duplicateCandidateNames = candidates
            .GroupBy(action => NormalizeName(action.Name))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        sb.AppendLine("> LLM workflow suggestions were not requested. Showing high-confidence static candidates instead.");
        sb.AppendLine();

        if (candidates.Count == 0)
        {
            sb.AppendLine("No high-confidence workflow candidates were discovered.");
            sb.AppendLine();
            return;
        }

        foreach (var action in candidates)
        {
            var heading = duplicateCandidateNames.Contains(NormalizeName(action.Name))
                ? $"{action.Name} ({action.SourceService})"
                : action.Name;
            sb.AppendLine($"### {heading}");
            sb.AppendLine();
            sb.AppendLine($"- Existing method: `{action.SourceService}.{action.MethodName}`");
            sb.AppendLine($"- Classification: {action.Classification}");
            sb.AppendLine($"- Risk: {ActionRisk.Describe(ActionRisk.GetRiskBand(action))}");
            sb.AppendLine($"- Mutation likely: {FormatBool(action.IsMutationLikely)}");
            sb.AppendLine($"- Approval recommended: {FormatBool(action.RequiresApproval)}");
            sb.AppendLine($"- Confidence: {action.Score.ToString("0.00", CultureInfo.InvariantCulture)}");
            if (action.RelevantRoutes.Count > 0)
            {
                sb.AppendLine($"- Relevant routes: {string.Join(", ", action.RelevantRoutes.Select(route => $"`{route}`"))}");
            }
            if (!string.IsNullOrWhiteSpace(action.Summary))
            {
                sb.AppendLine($"- Reasoning: {action.Summary}");
            }

            sb.AppendLine();
        }
    }

    private static void WriteReadiness(StringBuilder sb, InstallReadinessReport readiness)
    {
        sb.AppendLine("## Install Readiness");
        sb.AppendLine();
        sb.AppendLine("| Status | Check | Details |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var check in readiness.Checks)
        {
            var details = check.Message;
            if (!string.IsNullOrWhiteSpace(check.SuggestedFix))
            {
                details += $" Fix: {check.SuggestedFix}";
            }

            sb.AppendLine($"| {check.Status} | {EscapeTableCell(check.Title)} | {EscapeTableCell(details)} |");
        }

        sb.AppendLine();
    }

    private static void WriteNextSteps(
        StringBuilder sb,
        ProjectModel model,
        InstallReadinessReport? readiness,
        WorkflowSuggestionSet? workflowSuggestions)
    {
        sb.AppendLine("## Recommended Next Steps");
        sb.AppendLine();

        var hasSteps = false;
        if (readiness is not null)
        {
            foreach (var check in readiness.Checks.Where(check => check.Status != InstallReadinessStatus.Pass))
            {
                hasSteps = true;
                sb.AppendLine($"- Fix `{check.Id}`: {check.SuggestedFix ?? check.Message}");
            }
        }

        var includeStaticActionRecommendations = workflowSuggestions is null ||
            workflowSuggestions.Suggestions.Count == 0;

        if (!includeStaticActionRecommendations && workflowSuggestions?.Suggestions.Count > 0)
        {
            hasSteps = true;
            sb.AppendLine("- Review the validated workflow suggestions above and choose which ones should become explicit `[AgentAction]` capabilities.");
        }

        if (includeStaticActionRecommendations)
        {
            foreach (var recommendation in model.Recommendations
                .Where(recommendation => IsDeveloperFacingRecommendation(recommendation, model))
                .Where(recommendation => !DuplicatesConfirmedAction(recommendation, model))
                .OrderBy(item => GetRecommendationRiskRank(item, model))
                .ThenByDescending(item => item.Priority)
                .GroupBy(item => item.Suggestion, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(5))
            {
                hasSteps = true;
                sb.AppendLine($"- {recommendation.Suggestion}");
            }
        }

        if (!hasSteps)
        {
            sb.AppendLine("- No immediate setup gaps were found. Review workflow suggestions and decide which actions should become explicit `[AgentAction]` capabilities.");
        }

        sb.AppendLine();
    }

    private static string ValueOrUnknown(string value)
        => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static string FormatBool(bool value)
        => value ? "yes" : "no";

    private static string EscapeTableCell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string EscapeAttributeValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string TrimForTable(string value, int maxLength)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maxLength - 3), "...");
    }

    private static IReadOnlyList<ActionModel> GetStaticWorkflowCandidates(ProjectModel model, int take)
    {
        return model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Suggested)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Where(action => !DuplicatesConfirmedAction(action, model))
            .Where(action => IsTopRecommendationAction(action, model))
            .Where(action => action.Score >= 0.7)
            .OrderBy(GetActionWorkflowRank)
            .ThenBy(action => (int)ActionRisk.GetRiskBand(action))
            .ThenByDescending(action => action.Score)
            .ThenBy(action => action.SourceService)
            .GroupBy(action => $"{action.SourceService}.{action.MethodName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(take)
            .ToList();
    }

    private static IReadOnlyList<WorkflowClusterModel> GetPreferredWorkflowClusters(ProjectModel model, int take)
    {
        return model.WorkflowClusters
            .Where(cluster => !cluster.Risk.Equals("high-risk/admin", StringComparison.OrdinalIgnoreCase))
            .Where(cluster => cluster.Methods.Any(method =>
                method.Classification is ActionClassification.Workflow or ActionClassification.Command or ActionClassification.Export))
            .OrderBy(cluster => cluster.Risk.Equals("approval required", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(cluster => cluster.Confidence)
            .ThenByDescending(cluster => cluster.Methods.Count)
            .ThenBy(cluster => cluster.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static ServiceClassification ClassifyService(ServiceModel service, ProjectModel model)
    {
        var name = service.TypeName;
        var path = service.FilePath;
        var methods = service.Methods.Select(method => method.Name).ToList();
        var actions = model.Actions
            .Where(action => string.Equals(action.SourceService, service.TypeName, StringComparison.OrdinalIgnoreCase))
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .ToList();

        if (ContainsAny(name, "Permission", "Password", "User", "Setup", "Cookie", "Auth", "Tenant") ||
            ContainsAny(path, "\\Permissions\\", "/Permissions/", "\\Users\\", "/Users/", "\\Setup\\", "/Setup/", "\\Cookies\\", "/Cookies/") ||
            methods.Any(method => ContainsAny(method, "ResetPassword", "SendPasswordReset", "AddPermission", "CheckPermission", "AddTenant", "CreateSystemUser", "DeleteCookie")) ||
            IsGenericWorkflowEngineService(name, methods))
        {
            return new ServiceClassification(ServiceCategory.AdminOrSensitive, "Use cautiously; likely needs explicit approval policy and tenant/user context before exposing.");
        }

        if (ContainsAny(name, "Mongo", "Database", "Sql", "Db", "Repository", "Store", "Transaction") ||
            ContainsAny(path, "\\MongoDb\\", "/MongoDb/", "\\Data\\", "/Data/"))
        {
            return new ServiceClassification(ServiceCategory.DataAccess, "Supporting data access surface; usually wrap it in a narrower business capability.");
        }

        if (ContainsAny(name, "Email", "Message", "Notification", "Chat", "Client", "Upload", "File", "Link"))
        {
            return new ServiceClassification(ServiceCategory.IntegrationOrMessaging, "Useful integration surface, but review side effects and external delivery before exposing.");
        }

        if (actions.Any(action => ActionRisk.GetRiskBand(action) is ActionRiskBand.HighRisk or ActionRiskBand.ApprovalRequired) ||
            methods.Any(method => ContainsAny(method, "Submit", "Promote", "Upload", "Save", "Add", "Remove", "Delete", "Update", "Create", "Run", "Execute")))
        {
            return new ServiceClassification(ServiceCategory.OperationalWorkflow, "Potential workflow candidate; prefer explicit approval for mutating operations.");
        }

        if (actions.Any(action => action.Classification is ActionClassification.Query or ActionClassification.Validation or ActionClassification.Export) ||
            methods.Any(method => ContainsAny(method, "Get", "Find", "List", "Check", "Validate", "Generate", "Translate")))
        {
            return new ServiceClassification(ServiceCategory.BusinessReadOnly, "Good candidate for a first read-only agent workflow if the data is safe to show.");
        }

        return new ServiceClassification(ServiceCategory.Unknown, "Review manually; the analyzer found public methods but could not infer a strong workflow role.");
    }

    private static bool ContainsAny(string value, params string[] fragments)
        => fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool IsGenericWorkflowEngineService(string serviceName, IReadOnlyCollection<string> methodNames)
    {
        return ContainsAny(serviceName, "Workflow") &&
            methodNames.Any(method => ContainsAny(method, "ExecuteWorkflow", "RunWorkflow", "LoadWorkflow", "RunWorkflowFromJson"));
    }

    private static string ServiceCategoryLabel(ServiceCategory category)
        => category switch
        {
            ServiceCategory.BusinessReadOnly => "Business/read-only",
            ServiceCategory.OperationalWorkflow => "Operational workflow",
            ServiceCategory.IntegrationOrMessaging => "Integration/messaging",
            ServiceCategory.DataAccess => "Data access",
            ServiceCategory.AdminOrSensitive => "Admin/sensitive",
            _ => "Needs review"
        };

    private static string ServiceCategoryGuidance(ServiceCategory category)
        => category switch
        {
            ServiceCategory.BusinessReadOnly => "Best starting point for first agent actions.",
            ServiceCategory.OperationalWorkflow => "Candidate workflow surface; check mutation and approval requirements.",
            ServiceCategory.IntegrationOrMessaging => "Useful, but external effects or delivery need policy review.",
            ServiceCategory.DataAccess => "Prefer wrapping in business capabilities rather than exposing directly.",
            ServiceCategory.AdminOrSensitive => "Do not expose directly without explicit approval, auth, and tenant policy.",
            _ => "Inspect manually before exposing."
        };

    private static int GetServiceCategoryRank(ServiceCategory category)
        => category switch
        {
            ServiceCategory.BusinessReadOnly => 0,
            ServiceCategory.OperationalWorkflow => 1,
            ServiceCategory.IntegrationOrMessaging => 2,
            ServiceCategory.DataAccess => 3,
            ServiceCategory.AdminOrSensitive => 4,
            _ => 5
        };

    private static bool DuplicatesConfirmedAction(RecommendationModel recommendation, ProjectModel model)
    {
        if (recommendation.Type is not RecommendationType.AddAgentAction)
        {
            return false;
        }

        var targetMethod = recommendation.TargetName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? recommendation.TargetName;
        var normalizedTarget = NormalizeName(targetMethod);
        return model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Any(action =>
                NormalizeName(action.MethodName) == normalizedTarget ||
                NormalizeName(action.Name) == normalizedTarget ||
                (!string.IsNullOrWhiteSpace(action.Name) && NormalizeName(recommendation.Suggestion).Contains(NormalizeName(action.Name), StringComparison.Ordinal)));
    }

    private static bool IsDeveloperFacingRecommendation(RecommendationModel recommendation, ProjectModel model)
    {
        return recommendation.Type switch
        {
            RecommendationType.AddAgentAction or RecommendationType.AddApprovalTag or RecommendationType.AddDescription or RecommendationType.AddParameterInfo
                => model.Actions
                    .Where(action => string.Equals($"{action.SourceService}.{action.MethodName}", recommendation.TargetName, StringComparison.OrdinalIgnoreCase))
                    .Any(AnalysisModelFilters.IsDeveloperFacingAction),
            RecommendationType.AddAgentCapability
                => model.Services
                    .Where(service => string.Equals(service.TypeName, recommendation.TargetName, StringComparison.OrdinalIgnoreCase))
                    .Any(service => AnalysisModelFilters.IsDeveloperFacingService(service, model)),
            _ => true
        };
    }

    private static bool DuplicatesConfirmedAction(ActionModel candidate, ProjectModel model)
    {
        var normalizedMethod = NormalizeName(candidate.MethodName);
        var normalizedName = NormalizeName(candidate.Name);
        return model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Any(action =>
                NormalizeName(action.MethodName) == normalizedMethod ||
                (!string.IsNullOrWhiteSpace(action.Name) && NormalizeName(action.Name) == normalizedName));
    }

    private static bool ReferencesMutationLikelyMethod(WorkflowSuggestion suggestion, ProjectModel model)
    {
        return suggestion.Methods.Any(method => model.Actions
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Any(action =>
                string.Equals(action.SourceService, method.Service, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action.MethodName, method.Method, StringComparison.OrdinalIgnoreCase) &&
                (action.IsMutationLikely ||
                 action.RequiresApproval ||
                 action.Classification is ActionClassification.Command or ActionClassification.Workflow)));
    }

    private static ActionRiskBand GetSuggestionRiskBand(WorkflowSuggestion suggestion, ProjectModel model)
    {
        var actions = suggestion.Methods
            .SelectMany(method => model.Actions
                .Where(AnalysisModelFilters.IsDeveloperFacingAction)
                .Where(action =>
                    string.Equals(action.SourceService, method.Service, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(action.MethodName, method.Method, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return actions.Count == 0
            ? ActionRiskBand.ApprovalRequired
            : actions.Max(ActionRisk.GetRiskBand);
    }

    private static int GetSuggestionRiskRank(WorkflowSuggestion suggestion, ProjectModel model)
        => (int)GetSuggestionRiskBand(suggestion, model);

    private static bool IsProcessWorkflowSuggestion(WorkflowSuggestion suggestion, ProjectModel model)
    {
        return GetReferencedActions(suggestion, model)
            .Any(action => IsTopRecommendationAction(action, model));
    }

    private static bool IsTopRecommendationAction(ActionModel action, ProjectModel model)
    {
        if (action.Classification is not (
            ActionClassification.Workflow or
            ActionClassification.Command or
            ActionClassification.Export))
        {
            return false;
        }

        var service = model.Services.FirstOrDefault(service =>
            string.Equals(service.TypeName, action.SourceService, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            return action.Classification == ActionClassification.Workflow;
        }

        var serviceCategory = ClassifyService(service, model).Category;
        if (serviceCategory is ServiceCategory.IntegrationOrMessaging or ServiceCategory.DataAccess or ServiceCategory.AdminOrSensitive)
        {
            return false;
        }

        return action.Classification == ActionClassification.Workflow ||
            serviceCategory is ServiceCategory.OperationalWorkflow or ServiceCategory.Unknown ||
            (action.Classification == ActionClassification.Export && serviceCategory == ServiceCategory.BusinessReadOnly);
    }

    private static int GetSuggestionWorkflowRank(WorkflowSuggestion suggestion, ProjectModel model)
    {
        var actions = GetReferencedActions(suggestion, model).ToList();
        if (actions.Any(action => action.Classification == ActionClassification.Workflow))
        {
            return 0;
        }

        if (actions.Any(action => action.Classification == ActionClassification.Command))
        {
            return 1;
        }

        if (actions.Any(action => action.Classification == ActionClassification.Export))
        {
            return 2;
        }

        if (actions.Any(action => action.Classification == ActionClassification.Validation))
        {
            return 3;
        }

        return 4;
    }

    private static int GetActionWorkflowRank(ActionModel action)
        => action.Classification switch
        {
            ActionClassification.Workflow => 0,
            ActionClassification.Command => 1,
            ActionClassification.Export => 2,
            ActionClassification.Validation => 3,
            ActionClassification.Query => 4,
            _ => 5
        };

    private static IEnumerable<ActionModel> GetReferencedActions(WorkflowSuggestion suggestion, ProjectModel model)
    {
        return suggestion.Methods.SelectMany(method => model.Actions
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Where(action =>
                string.Equals(action.SourceService, method.Service, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(action.MethodName, method.Method, StringComparison.OrdinalIgnoreCase)));
    }

    private static int GetRecommendationRiskRank(RecommendationModel recommendation, ProjectModel model)
    {
        var action = model.Actions.FirstOrDefault(action =>
            string.Equals($"{action.SourceService}.{action.MethodName}", recommendation.TargetName, StringComparison.OrdinalIgnoreCase));
        return action is null ? (int)ActionRiskBand.ApprovalRequired : (int)ActionRisk.GetRiskBand(action);
    }

    private static string NormalizeName(string value)
    {
        var normalized = value.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? value[..^"Async".Length]
            : value;

        var sb = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                sb.Append(char.ToLowerInvariant(character));
            }
        }

        return sb.ToString();
    }
}

internal sealed record ServiceClassification(ServiceCategory Category, string AgentFit);

internal enum ServiceCategory
{
    BusinessReadOnly,
    OperationalWorkflow,
    IntegrationOrMessaging,
    DataAccess,
    AdminOrSensitive,
    Unknown
}
