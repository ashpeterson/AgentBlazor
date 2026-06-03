using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Analyzes a ProjectModel and generates recommendations for improving
/// agent comprehension through attributes.
/// </summary>
public sealed class RecommendationEngine
{
    // Method patterns that strongly suggest needing [AgentAction]
    private static readonly string[] HighValuePatterns =
    [
        "Create", "Update", "Delete", "Remove", "Add", "Submit", "Approve",
        "Reject", "Cancel", "Complete", "Start", "Stop", "Reset", "Process",
        "Execute", "Run", "Generate", "Export", "Import", "Send", "Publish",
        "Save", "Sync", "Apply", "Prepare"
    ];

    // Services that are infrastructure/implementation details (not agent-facing)
    private static readonly string[] ExcludedServicePrefixes =
    [
        "Null", "InMemory", "Static", "PatternBased", "JsonFile", "Mock", "Fake", "Stub", "Test"
    ];

    private static readonly string[] InfrastructureServiceSuffixes =
    [
        "Store", "Repository", "DataAccess", "DbContext", "Cache", "Logger", "Factory"
    ];

    // Method patterns that indicate UI/state methods (not useful as agent actions)
    private static readonly string[] ExcludedMethodPatterns =
    [
        "Visible", "Dialog", "SetActive", "SetCurrent", "SetSelected", "SetExpanded",
        "LoadAsync", "GetRoute", "GetJourney", "GetNext", "GetTools", "GetSnapshot",
        "GetSuggestions", "GetStatistics", "GetTrace", "GetInsight"
    ];

    // Method patterns that suggest mutation (should have approval)
    private static readonly string[] MutationPatterns =
    [
        "Create", "Update", "Delete", "Remove", "Add", "Submit", "Approve",
        "Reject", "Cancel", "Complete", "Reset", "Process", "Execute", "Send"
    ];

    /// <summary>
    /// Generates recommendations and coverage stats for the project.
    /// </summary>
    public (IReadOnlyList<RecommendationModel> Recommendations, CoverageStats Coverage) Analyze(
        ProjectModel model)
    {
        var recommendations = new List<RecommendationModel>();

        // Analyze actions for missing attributes
        foreach (var action in model.Actions)
        {
            if (action.ExposureMode == ActionExposureMode.Confirmed)
            {
                // Check confirmed actions for missing descriptions
                if (string.IsNullOrEmpty(action.Summary) || IsTrivialDescription(action.Summary))
                {
                    recommendations.Add(new RecommendationModel
                    {
                        Type = RecommendationType.AddDescription,
                        TargetName = $"{action.SourceService}.{action.MethodName}",
                        FilePath = action.FilePath,
                        Suggestion = $"Add description to [AgentAction]: `[AgentAction(\"{GenerateSuggestedDescription(action)}\")]`",
                        Priority = 0.5
                    });
                }

                // Check if mutation needs approval flag
                if (action.IsMutationLikely && !action.RequiresApproval)
                {
                    recommendations.Add(new RecommendationModel
                    {
                        Type = RecommendationType.AddApprovalTag,
                        TargetName = $"{action.SourceService}.{action.MethodName}",
                        FilePath = action.FilePath,
                        Suggestion = "Add `RequiresApproval = true` - this appears to be a mutation",
                        Priority = 0.7
                    });
                }
            }
            else if (action.ExposureMode == ActionExposureMode.Suggested)
            {
                if (!AnalysisModelFilters.IsDeveloperFacingAction(action))
                {
                    continue;
                }

                // High-scoring discovered actions should become confirmed
                if (action.Score >= 0.7 && IsHighValueMethod(action.MethodName))
                {
                    var isMutation = IsMutationMethod(action.MethodName);
                    var approvalHint = isMutation ? ", RequiresApproval = true" : "";

                    recommendations.Add(new RecommendationModel
                    {
                        Type = RecommendationType.AddAgentAction,
                        TargetName = $"{action.SourceService}.{action.MethodName}",
                        FilePath = action.FilePath,
                        Suggestion = $"Add `[AgentAction(\"{GenerateSuggestedDescription(action)}\"{approvalHint})]`",
                        Priority = action.Score
                    });
                }
            }
        }

        // Find services that have high-value methods but no [AgentCapability]
        var servicesWithConfirmedActions = model.Actions
            .Where(a => a.ExposureMode == ActionExposureMode.Confirmed)
            .Select(a => a.SourceService)
            .ToHashSet();

        var servicesWithHighValueMethods = model.Actions
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Where(a => a.ExposureMode == ActionExposureMode.Suggested && a.Score >= 0.7)
            .GroupBy(a => a.SourceService)
            .Where(g => g.Count() >= 2) // At least 2 high-value methods
            .Select(g => g.Key)
            .Except(servicesWithConfirmedActions);

        foreach (var serviceName in servicesWithHighValueMethods)
        {
            var service = model.Services.FirstOrDefault(s => s.TypeName == serviceName);
            if (service != null)
            {
                recommendations.Add(new RecommendationModel
                {
                    Type = RecommendationType.AddAgentCapability,
                    TargetName = serviceName,
                    FilePath = service.FilePath,
                    Suggestion = $"Add `[AgentCapability]` - this service has multiple high-value methods",
                    Priority = 0.8
                });
            }
        }

        // Calculate coverage stats
        var coverage = CalculateCoverage(model);

        // Sort recommendations by priority (highest first)
        var sortedRecommendations = recommendations
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Type)
            .Take(10) // Limit to top 10 recommendations
            .ToList();

        return (sortedRecommendations, coverage);
    }

    private static CoverageStats CalculateCoverage(ProjectModel model)
    {
        // Exclude infrastructure services from coverage calculations
        var relevantActions = model.Actions
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Where(a => !IsExcludedService(a.SourceService))
            .Where(a => !IsInfrastructureService(a.SourceService))
            .ToList();

        var confirmedActions = relevantActions.Count(a => a.ExposureMode == ActionExposureMode.Confirmed);

        // Build set of confirmed action names for duplicate detection
        var confirmedNames = relevantActions
            .Where(a => a.ExposureMode == ActionExposureMode.Confirmed)
            .Select(a => a.Name)
            .ToHashSet();

        // Only count discovered actions that would actually appear in AGENT.md
        // Must match MarkdownGenerator filtering criteria exactly:
        // - score >= 0.7
        // - not excluded method pattern
        // - not duplicate of confirmed action name
        var discoveredActions = relevantActions.Count(a =>
            a.ExposureMode == ActionExposureMode.Suggested &&
            a.Score >= 0.7 &&
            !IsExcludedMethod(a.MethodName) &&
            !confirmedNames.Contains(a.Name));

        var totalActions = confirmedActions + discoveredActions;

        // For mutations, only count confirmed ones (discovered mutations are uncertain)
        var confirmedMutations = relevantActions
            .Where(a => a.ExposureMode == ActionExposureMode.Confirmed && a.IsMutationLikely)
            .ToList();
        var mutationsWithApproval = confirmedMutations.Count(a => a.RequiresApproval);

        var actionsWithDescription = relevantActions.Count(a =>
            a.ExposureMode == ActionExposureMode.Confirmed &&
            !string.IsNullOrEmpty(a.Summary) &&
            !IsTrivialDescription(a.Summary));

        // Count services with at least one confirmed action
        var servicesWithCapability = relevantActions
            .Where(a => a.ExposureMode == ActionExposureMode.Confirmed)
            .Select(a => a.SourceService)
            .Distinct()
            .Count();

        return new CoverageStats
        {
            TotalServices = model.Services.Count,
            ServicesWithCapability = servicesWithCapability,
            TotalActions = totalActions,
            ConfirmedActions = confirmedActions,
            DiscoveredActions = discoveredActions,
            ActionsWithDescription = actionsWithDescription,
            MutationsWithApproval = mutationsWithApproval,
            TotalMutations = confirmedMutations.Count
        };
    }

    private static bool IsHighValueMethod(string methodName)
    {
        return HighValuePatterns.Any(p =>
            methodName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMutationMethod(string methodName)
    {
        return MutationPatterns.Any(p =>
            methodName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcludedService(string serviceName)
    {
        return ExcludedServicePrefixes.Any(p =>
            serviceName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInfrastructureService(string serviceName)
    {
        return InfrastructureServiceSuffixes.Any(p =>
            serviceName.EndsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcludedMethod(string methodName)
    {
        return ExcludedMethodPatterns.Any(p =>
            methodName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTrivialDescription(string desc)
    {
        if (string.IsNullOrEmpty(desc)) return true;
        if (desc.StartsWith("Action from", StringComparison.OrdinalIgnoreCase)) return true;

        // Auto-generated descriptions are just method name split
        var words = desc.TrimEnd('.').Split(' ');
        return words.Length <= 3 && char.IsUpper(desc[0]);
    }

    private static string GenerateSuggestedDescription(ActionModel action)
    {
        var methodName = action.MethodName;
        if (methodName.EndsWith("Async", StringComparison.Ordinal))
            methodName = methodName[..^5];

        // Split PascalCase into words
        var words = string.Concat(methodName.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

        return words.Trim();
    }
}
