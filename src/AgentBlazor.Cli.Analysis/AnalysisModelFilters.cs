using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public static class AnalysisModelFilters
{
    private static readonly string[] InfrastructureServiceNames =
    [
        "AgentBlazorBuilder",
        "AgentRegistrationBuilder",
        "ComponentCapabilityCatalogBuilder",
        "AgentBlazorEntitlementService",
        "AgentChatActiveRunStore"
    ];

    private static readonly string[] InfrastructureServiceNameFragments =
    [
        "Controller",
        "DbContext",
        "DbContextFactory",
        "Factory",
        "Helper",
        "Utility",
        "Provider",
        "Notifier",
        "SignInManager",
        "DownloadFileService",
        "DialogService",
        "HubClient",
        "HubConnection",
        "ExceptionHandler",
        "LayoutService",
        "Cache",
        "Runner",
        "Scheduler",
        "StateStore",
        "TenantStore",
        "TicketStore",
        "DataSourceService",
        "MessageAssetService",
        "UserProfileState",
        "ValidationService"
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
        "\\src\\AgentBlazor.Licensing\\",
        "/Hubs/",
        "\\Hubs\\",
        "/Middlewares/",
        "\\Middlewares\\",
        "/Services/JsInterop/",
        "\\Services\\JsInterop\\",
        "/Services/Layout/",
        "\\Services\\Layout\\",
        "/Services/Caching/",
        "\\Services\\Caching\\",
        "/Services/Identity/",
        "\\Services\\Identity\\",
        "/Services/AI/Chat/",
        "\\Services\\AI\\Chat\\",
        "/Services/AI/State/",
        "\\Services\\AI\\State\\",
        "/Services/AI/Runner/",
        "\\Services\\AI\\Runner\\",
        "/Services/AI/Scheduler/",
        "\\Services\\AI\\Scheduler\\",
        "/Tenants/",
        "\\Tenants\\"
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
        "Dispose",
        "DisposeAsync",
        "Equals",
        "GetHashCode",
        "GetType",
        "Load",
        "LoadAsync",
        "OnPropertyChanged",
        "Reset",
        "ResetAsync",
        "ToString",
        "IsHighlighted",
        "DescribeSignals"
    ];

    public static bool IsDeveloperFacingService(ServiceModel service, ProjectModel? model = null)
    {
        if (InfrastructureServiceNames.Contains(service.TypeName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (InfrastructureServiceNameFragments.Any(fragment => service.TypeName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
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

        if (InfrastructureServiceNameFragments.Any(fragment => action.SourceService.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
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
