using System.Text.RegularExpressions;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Links pages to relevant actions based on service injection and method usage analysis.
/// </summary>
public sealed partial class PageActionLinker
{
    /// <summary>
    /// Analyzes a page's razor file to find method calls to injected services.
    /// </summary>
    public async Task<PageActionLinks> AnalyzePageAsync(
        RazorFileAnalysis page,
        IReadOnlyList<ServiceModel> services,
        IReadOnlyList<ActionModel> actions,
        CancellationToken ct = default)
    {
        var linkedActions = new List<string>();
        var linkedServices = new List<string>();
        var methodCallsFound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get the injected service types for this page
        var injectedTypes = page.InjectedServices
            .Select(s => NormalizeTypeName(s.TypeName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Read the page content to find method calls
        var content = await File.ReadAllTextAsync(page.FilePath, ct).ConfigureAwait(false);

        // Also check for code-behind file
        var codeBehindPath = page.FilePath + ".cs";
        if (File.Exists(codeBehindPath))
        {
            content += "\n" + await File.ReadAllTextAsync(codeBehindPath, ct).ConfigureAwait(false);
        }

        // Find services that are injected into this page
        foreach (var service in services)
        {
            if (!IsInjectedServiceMatch(service, injectedTypes)) continue;

            linkedServices.Add(service.TypeName);

            // Look for method calls from this service
            foreach (var method in service.Methods)
            {
                if (IsMethodCalledInContent(content, page.InjectedServices, service, method.Name))
                {
                    methodCallsFound.Add($"{service.TypeName}.{method.Name}");

                    // Find the corresponding action
                    var matchingAction = actions.FirstOrDefault(a =>
                        a.SourceService == service.TypeName &&
                        a.MethodName == method.Name);

                    if (matchingAction != null)
                    {
                        linkedActions.Add(matchingAction.Id);
                    }
                }
            }
        }

        // If we found direct injections but no method calls, link high-score workflow actions
        // from those services (they're likely called indirectly or via binding)
        if (linkedServices.Count > 0 && linkedActions.Count == 0)
        {
            foreach (var serviceName in linkedServices)
            {
                var serviceActions = actions
                    .Where(a => a.SourceService == serviceName &&
                               a.Score >= 0.6 &&
                               a.Classification is ActionClassification.Workflow or ActionClassification.Command)
                    .Take(5); // Limit to top 5 per service

                foreach (var action in serviceActions)
                {
                    linkedActions.Add(action.Id);
                }
            }
        }

        return new PageActionLinks
        {
            PageRoute = page.Routes.FirstOrDefault()?.Template ?? "",
            PageFile = page.FilePath,
            LinkedServices = linkedServices.Distinct().ToList(),
            LinkedActions = linkedActions.Distinct().ToList(),
            MethodCallsDetected = methodCallsFound.ToList()
        };
    }

    /// <summary>
    /// Links all pages to their relevant actions.
    /// </summary>
    public async Task<IReadOnlyList<PageActionLinks>> LinkAllPagesAsync(
        IReadOnlyList<RazorFileAnalysis> pages,
        IReadOnlyList<ServiceModel> services,
        IReadOnlyList<ActionModel> actions,
        CancellationToken ct = default)
    {
        var links = new List<PageActionLinks>();

        foreach (var page in pages.Where(p => p.IsPage))
        {
            ct.ThrowIfCancellationRequested();
            var link = await AnalyzePageAsync(page, services, actions, ct);
            links.Add(link);
        }

        return links;
    }

    /// <summary>
    /// Updates action models with their relevant routes based on linking analysis.
    /// </summary>
    public IReadOnlyList<ActionModel> UpdateActionsWithRoutes(
        IReadOnlyList<ActionModel> actions,
        IReadOnlyList<PageActionLinks> links)
    {
        var actionToRoutes = new Dictionary<string, List<string>>();

        foreach (var link in links)
        {
            if (string.IsNullOrEmpty(link.PageRoute)) continue;

            foreach (var actionId in link.LinkedActions)
            {
                if (!actionToRoutes.TryGetValue(actionId, out var routes))
                {
                    routes = [];
                    actionToRoutes[actionId] = routes;
                }
                routes.Add(link.PageRoute);
            }
        }

        return actions.Select(a =>
        {
            if (actionToRoutes.TryGetValue(a.Id, out var routes))
            {
                return a with { RelevantRoutes = routes.Distinct().ToList() };
            }
            return a;
        }).ToList();
    }

    private bool IsMethodCalledInContent(
        string content,
        IReadOnlyList<InjectedServiceModel> injections,
        ServiceModel service,
        string methodName)
    {
        // Find the field name for this service type
        var injection = injections.FirstOrDefault(i =>
            GetServiceAliases(service).Any(alias =>
                NormalizeTypeName(i.TypeName).Equals(NormalizeTypeName(alias), StringComparison.OrdinalIgnoreCase)));

        if (injection == null) return false;

        // Look for field.MethodName patterns (with or without Async suffix)
        var methodWithoutAsync = methodName.EndsWith("Async", StringComparison.Ordinal)
            ? methodName[..^5]
            : methodName;

        var patterns = new[]
        {
            // Direct call: service.Method(
            $@"\b{Regex.Escape(injection.FieldName)}\.{Regex.Escape(methodName)}\s*\(",
            // Await call: await service.Method(
            $@"await\s+{Regex.Escape(injection.FieldName)}\.{Regex.Escape(methodName)}\s*\(",
            // Lambda/delegate: () => service.Method(
            $@"=>\s*{Regex.Escape(injection.FieldName)}\.{Regex.Escape(methodName)}\s*\(",
            // Without Async suffix
            $@"\b{Regex.Escape(injection.FieldName)}\.{Regex.Escape(methodWithoutAsync)}\s*\(",
        };

        return patterns.Any(p => Regex.IsMatch(content, p, RegexOptions.IgnoreCase | RegexOptions.Singleline));
    }

    private static string NormalizeTypeName(string typeName)
    {
        var name = typeName.Split('.').Last();

        // Remove interface prefix 'I' if present
        if (name.StartsWith('I') && name.Length > 1 && char.IsUpper(name[1]))
        {
            name = name[1..];
        }

        return name
            .Replace("ApiService", "Service", StringComparison.OrdinalIgnoreCase)
            .Replace("ClientService", "Service", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInjectedServiceMatch(ServiceModel service, IReadOnlySet<string> injectedTypes)
    {
        return GetServiceAliases(service)
            .Select(NormalizeTypeName)
            .Any(injectedTypes.Contains);
    }

    private static IEnumerable<string> GetServiceAliases(ServiceModel service)
    {
        yield return service.TypeName;

        if (!string.IsNullOrWhiteSpace(service.ImplementationType))
        {
            yield return service.ImplementationType;
        }

        foreach (var serviceType in service.ServiceTypes)
        {
            yield return serviceType;
        }
    }
}

public sealed class PageActionLinks
{
    public string PageRoute { get; init; } = "";
    public string PageFile { get; init; } = "";
    public IReadOnlyList<string> LinkedServices { get; init; } = [];
    public IReadOnlyList<string> LinkedActions { get; init; } = [];
    public IReadOnlyList<string> MethodCallsDetected { get; init; } = [];
}
