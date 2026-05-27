using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

internal static class AnalysisModelFilters
{
    private static readonly string[] InfrastructureServiceNames =
    [
        "AgentBlazorBuilder",
        "AgentRegistrationBuilder",
        "ComponentCapabilityCatalogBuilder",
        "AgentBlazorEntitlementService",
        "AgentChatActiveRunStore"
    ];

    private static readonly string[] InfrastructurePathFragments =
    [
        "/src/AgentBlazor.Core/",
        "/src/AgentBlazor.Components/",
        "/src/AgentBlazor.Hosting/",
        "/src/AgentBlazor.ProviderAdapters/",
        "/src/AgentBlazor.Licensing/",
        "\\src\\AgentBlazor.Core\\",
        "\\src\\AgentBlazor.Components\\",
        "\\src\\AgentBlazor.Hosting\\",
        "\\src\\AgentBlazor.ProviderAdapters\\",
        "\\src\\AgentBlazor.Licensing\\"
    ];

    private static readonly string[] UiStateMethodPrefixes =
    [
        "Set",
        "GetSelected",
        "GetCurrent",
        "GetNext",
        "GetRoute",
        "GetJourney",
        "GetSuggestions",
        "GetSnapshot",
        "GetStatistics",
        "GetTrace",
        "GetInsight"
    ];

    private static readonly string[] UiStateMethodNames =
    [
        "Load",
        "LoadAsync",
        "Reset",
        "ResetAsync",
        "IsHighlighted",
        "DescribeSignals"
    ];

    public static bool IsDeveloperFacingService(ServiceModel service, ProjectModel? model = null)
    {
        if (InfrastructureServiceNames.Contains(service.TypeName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (model?.Actions.Any(action =>
                action.ExposureMode == ActionExposureMode.Confirmed &&
                string.Equals(action.SourceService, service.TypeName, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return false;
        }

        if (InfrastructurePathFragments.Any(fragment => service.FilePath.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return service.Methods.Any(method => IsDeveloperFacingMethod(method.Name));
    }

    public static bool IsDeveloperFacingAction(ActionModel action)
    {
        if (!IsDeveloperFacingMethod(action.MethodName))
        {
            return false;
        }

        if (InfrastructurePathFragments.Any(fragment => action.FilePath.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return action.Classification != ActionClassification.Infrastructure;
    }

    public static bool IsDeveloperFacingMethod(string methodName)
    {
        var normalized = methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName[..^"Async".Length]
            : methodName;

        if (UiStateMethodNames.Contains(methodName, StringComparer.OrdinalIgnoreCase) ||
            UiStateMethodNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !UiStateMethodPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
