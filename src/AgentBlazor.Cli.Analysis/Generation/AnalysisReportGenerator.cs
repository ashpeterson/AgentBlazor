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
        WriteRoutes(sb, model);
        WriteCapabilities(sb, model);
        WriteServices(sb, model);
        WriteWorkflowSuggestions(sb, model, workflowSuggestions);
        if (readiness is not null)
        {
            WriteReadiness(sb, readiness);
        }

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
        sb.AppendLine("## Services");
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

        foreach (var service in developerFacingServices)
        {
            sb.AppendLine($"### `{service.TypeName}`");
            sb.AppendLine();
            sb.AppendLine($"- Lifetime: {service.Lifetime}");
            sb.AppendLine($"- File: `{service.FilePath}`");
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
                    .OrderBy(suggestion => GetSuggestionRiskRank(suggestion, model))
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

        var candidates = model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Suggested)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Where(action => !DuplicatesConfirmedAction(action, model))
            .Where(action => action.Score >= 0.7)
            .OrderBy(action => (int)ActionRisk.GetRiskBand(action))
            .ThenByDescending(action => action.Score)
            .ThenBy(action => action.SourceService)
            .GroupBy(action => $"{action.SourceService}.{action.MethodName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(10)
            .ToList();
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
