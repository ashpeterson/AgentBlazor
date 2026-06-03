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
        "Builder",
        "Manager",
        "Helper",
        "Utility",
        "Provider",
        "Repository",
        "StorageManager",
        "EventHandler",
        "Handler",
        "QueryHandler",
        "CommandHandler",
        "IntegrationHandler",
        "Notifier",
        "SignInManager",
        "TokenManager",
        "RoleStore",
        "UserStore",
        "PasswordValidator",
        "DownloadFileService",
        "DialogService",
        "HubClient",
        "HubService",
        "HubConnection",
        "HttpClientService",
        "HttpService",
        "ExceptionHandler",
        "ExecutionContext",
        "LayoutService",
        "State",
        "Cache",
        "Runner",
        "Scheduler",
        "StateStore",
        "TenantStore",
        "TicketStore",
        "Dispatcher",
        "LockManager",
        "CircuitBreakerManager",
        "DataSourceService",
        "FakeData",
        "FakeStorage",
        "Jwt",
        "JwtService",
        "MessageAssetService",
        "EmbeddingService",
        "ImageAnalysisService",
        "MarkdownService",
        "TextChunkingService",
        "UserProfileState",
        "ConfigurationValidator",
        "Validator",
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
        "/SignalR/",
        "\\SignalR\\",
        "/Middlewares/",
        "\\Middlewares\\",
        "/BuildingBlocks/Http/",
        "\\BuildingBlocks\\Http\\",
        "/ExecutionContext/",
        "\\ExecutionContext\\",
        "/Services/JsInterop/",
        "\\Services\\JsInterop\\",
        "/Services/Layout/",
        "\\Services\\Layout\\",
        "/Services/Caching/",
        "\\Services\\Caching\\",
        "/Services/Identity/",
        "\\Services\\Identity\\",
        "/Identity/",
        "\\Identity\\",
        "/Security/",
        "\\Security\\",
        "/Services/AI/Chat/",
        "\\Services\\AI\\Chat\\",
        "/Services/AI/State/",
        "\\Services\\AI\\State\\",
        "/Services/AI/Runner/",
        "\\Services\\AI\\Runner\\",
        "/Services/AI/Scheduler/",
        "\\Services\\AI\\Scheduler\\",
        "/Background/Services/",
        "\\Background\\Services\\",
        "/Infrastructure/",
        "\\Infrastructure\\",
        "/Domain/",
        "\\Domain\\",
        "/Auth/",
        "\\Auth\\",
        "/Storages/",
        "\\Storages\\",
        "/Storage/",
        "\\Storage\\",
        "/HostedServices/",
        "\\HostedServices\\",
        "/FileStorage/",
        "\\FileStorage\\",
        "/Notification/",
        "\\Notification\\",
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
        "Clone",
        "Equals",
        "GetHashCode",
        "GetType",
        "Hydrate",
        "Load",
        "LoadAsync",
        "OnPropertyChanged",
        "Reset",
        "ResetAsync",
        "ToString",
        "IsHighlighted",
        "DescribeSignals"
    ];

    private static readonly string[] SensitiveMethodNameFragments =
    [
        "AccessToken",
        "GenerateToken",
        "GetToken",
        "LoginUser",
        "LogoutUser",
        "Passkey",
        "PersonalAccessToken",
        "RefreshToken",
        "ValidatePassword",
        "ValidateUser",
        "VerifyTwoFactor"
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
        if (action.ExposureMode == ActionExposureMode.Confirmed)
        {
            return true;
        }

        if (!IsDeveloperFacingMethod(action.MethodName))
        {
            return false;
        }

        if (SensitiveMethodNameFragments.Any(fragment => action.MethodName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
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

        if (SensitiveMethodNameFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !UiStateMethodPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
