using System.Text.RegularExpressions;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed partial class WorkflowClusterAnalyzer
{
    private static readonly IReadOnlyDictionary<string, int> RoleOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["prepare"] = 10,
        ["generate"] = 20,
        ["validate"] = 25,
        ["upload"] = 30,
        ["submit"] = 40,
        ["status"] = 50,
        ["promote"] = 60,
        ["notify"] = 70
    };

    private static readonly string[] GenericWorkflowMethodTerms =
    [
        "ExecuteWorkflow",
        "RunWorkflow",
        "LoadWorkflow",
        "RunWorkflowFromJson"
    ];

    private static readonly HashSet<string> IgnoredDomainTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Async",
        "Service",
        "Services",
        "Task",
        "Tasks",
        "Model",
        "Models",
        "Data",
        "Client",
        "Clients",
        "Request",
        "Response",
        "Result",
        "Results",
        "Status",
        "Workflow",
        "Workflows",
        "Check",
        "User",
        "Users",
        "Admin",
        "System",
        "Item",
        "Items",
        "Entity",
        "Entities",
        "Object",
        "Objects"
    };

    public IReadOnlyList<WorkflowClusterModel> Analyze(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var candidates = BuildCandidateActions(model);
        var clusters = new List<WorkflowClusterModel>();

        clusters.AddRange(BuildSameServiceLifecycleClusters(model, candidates));
        clusters.AddRange(BuildRouteCorrelatedClusters(model, candidates));
        clusters.AddRange(BuildDomainCorrelatedClusters(model, candidates));

        return clusters
            .GroupBy(cluster => BuildClusterKey(cluster), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(cluster => cluster.Confidence).First())
            .OrderByDescending(cluster => cluster.Confidence)
            .ThenByDescending(cluster => cluster.Methods.Count)
            .ThenBy(cluster => cluster.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<ActionModel> BuildCandidateActions(ProjectModel model)
    {
        var servicesByName = model.Services.ToDictionary(service => service.TypeName, StringComparer.OrdinalIgnoreCase);

        return model.Actions
            .Where(action => action.ExposureMode is ActionExposureMode.Suggested or ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .Where(action => servicesByName.TryGetValue(action.SourceService, out var service) &&
                AnalysisModelFilters.IsDeveloperFacingService(service, model) &&
                !IsGenericWorkflowEngineService(service, [action]))
            .GroupBy(action => (action.SourceService, action.MethodName), new MethodKeyComparer())
            .Select(group => group
                .OrderByDescending(action => action.ExposureMode == ActionExposureMode.Confirmed)
                .ThenByDescending(action => action.Score)
                .First())
            .ToList();
    }

    private static IEnumerable<WorkflowClusterModel> BuildSameServiceLifecycleClusters(
        ProjectModel model,
        IReadOnlyList<ActionModel> candidates)
    {
        var servicesByName = model.Services.ToDictionary(service => service.TypeName, StringComparer.OrdinalIgnoreCase);

        foreach (var actionGroup in candidates.GroupBy(action => action.SourceService, StringComparer.OrdinalIgnoreCase))
        {
            if (!servicesByName.TryGetValue(actionGroup.Key, out var service) ||
                IsGenericWorkflowEngineService(service, actionGroup))
            {
                continue;
            }

            var methods = BuildOrderedMethodSteps(actionGroup);
            if (!HasEnoughProcessShape(methods, minimumMethods: 3, minimumRoles: 3))
            {
                continue;
            }

            var domainTerms = ExtractDominantDomainTerms(methods.Select(method => method.Action), [service.TypeName]);
            yield return BuildCluster(
                origin: "same-service lifecycle",
                serviceName: service.TypeName,
                servicePath: service.FilePath,
                methods: methods,
                routeHints: BuildRouteHints(model, methods.Select(method => method.Action)),
                domainTerms: domainTerms,
                evidence:
                [
                    $"same service contains {methods.Count} lifecycle methods",
                    "method names form an ordered process sequence"
                ]);
        }
    }

    private static IEnumerable<WorkflowClusterModel> BuildRouteCorrelatedClusters(
        ProjectModel model,
        IReadOnlyList<ActionModel> candidates)
    {
        var actionsById = candidates.ToDictionary(action => action.Id, StringComparer.OrdinalIgnoreCase);
        var actionsByRoute = new Dictionary<string, List<ActionModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in candidates)
        {
            foreach (var route in action.RelevantRoutes.Where(route => !string.IsNullOrWhiteSpace(route)))
            {
                AddAction(actionsByRoute, route, action);
            }
        }

        foreach (var page in model.Pages)
        {
            foreach (var actionId in page.SuggestedActions)
            {
                if (!string.IsNullOrWhiteSpace(page.Route) &&
                    actionsById.TryGetValue(actionId, out var action))
                {
                    AddAction(actionsByRoute, page.Route, action);
                }
            }
        }

        foreach (var routeGroup in actionsByRoute)
        {
            var route = routeGroup.Key;
            var routeActions = routeGroup.Value
                .GroupBy(action => (action.SourceService, action.MethodName), new MethodKeyComparer())
                .Select(group => group.OrderByDescending(action => action.Score).First())
                .ToList();

            var methods = BuildOrderedMethodSteps(routeActions)
                .Where(method => IsProcessAction(method.Action))
                .ToList();

            if (!HasEnoughProcessShape(methods, minimumMethods: 2, minimumRoles: 2))
            {
                continue;
            }

            var routeTerms = ExtractDomainTerms(route);
            var domainTerms = ExtractDominantDomainTerms(methods.Select(method => method.Action), routeTerms);
            if (domainTerms.Count == 0 && methods.Select(method => method.Action.SourceService).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                continue;
            }

            var component = model.Pages.FirstOrDefault(page => string.Equals(page.Route, route, StringComparison.OrdinalIgnoreCase))?.ComponentName;
            yield return BuildCluster(
                origin: "route-correlated workflow",
                serviceName: BuildRouteClusterName(component, route, domainTerms),
                servicePath: "",
                methods: methods,
                routeHints: [route],
                domainTerms: domainTerms,
                evidence:
                [
                    $"methods are used or linked from route {route}",
                    methods.Select(method => method.Action.SourceService).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                        ? "route correlates multiple services"
                        : "route correlates multiple lifecycle methods"
                ]);
        }
    }

    private static IEnumerable<WorkflowClusterModel> BuildDomainCorrelatedClusters(
        ProjectModel model,
        IReadOnlyList<ActionModel> candidates)
    {
        var termGroups = candidates
            .SelectMany(action => ExtractDomainTerms(action.SourceService, action.MethodName)
                .Select(term => (Term: term, Action: action)))
            .Where(item => !IgnoredDomainTerms.Contains(item.Term))
            .GroupBy(item => item.Term, StringComparer.OrdinalIgnoreCase);

        foreach (var termGroup in termGroups)
        {
            var actions = termGroup
                .Select(item => item.Action)
                .DistinctBy(action => $"{action.SourceService}.{action.MethodName}", StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (actions.Select(action => action.SourceService).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            {
                continue;
            }

            var methods = BuildOrderedMethodSteps(actions)
                .Where(method => IsProcessAction(method.Action))
                .ToList();

            if (!HasEnoughProcessShape(methods, minimumMethods: 3, minimumRoles: 3))
            {
                continue;
            }

            yield return BuildCluster(
                origin: "domain-correlated workflow",
                serviceName: $"{termGroup.Key} Pipeline",
                servicePath: "",
                methods: methods,
                routeHints: BuildRouteHints(model, methods.Select(method => method.Action)),
                domainTerms: [termGroup.Key],
                evidence:
                [
                    $"multiple services share the domain term '{termGroup.Key}'",
                    "shared domain methods form a lifecycle sequence"
                ]);
        }
    }

    private static WorkflowClusterModel BuildCluster(
        string origin,
        string serviceName,
        string servicePath,
        IReadOnlyList<(ActionModel Action, string Role)> methods,
        IReadOnlyList<string> routeHints,
        IReadOnlyList<string> domainTerms,
        IReadOnlyList<string> evidence)
    {
        var highestRisk = methods
            .Select(method => ActionRisk.GetRiskBand(method.Action))
            .DefaultIfEmpty(ActionRiskBand.SafeReadOnly)
            .OrderByDescending(risk => (int)risk)
            .First();
        var displayName = BuildDisplayName(serviceName, methods.Select(method => method.Action.MethodName), domainTerms);
        var relatedServices = methods
            .Select(method => method.Action.SourceService)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(service => service, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WorkflowClusterModel
        {
            Id = NormalizeId($"{origin}-{displayName}"),
            Name = displayName,
            SourceService = relatedServices.Count == 1 ? relatedServices[0] : serviceName,
            FilePath = servicePath,
            Summary = BuildSummary(displayName, methods),
            Confidence = CalculateConfidence(methods, routeHints, domainTerms, origin),
            RequiresApproval = methods.Any(method =>
                method.Action.RequiresApproval ||
                method.Action.IsMutationLikely ||
                ActionRisk.GetRiskBand(method.Action) is ActionRiskBand.ApprovalRequired or ActionRiskBand.HighRisk),
            Risk = ActionRisk.Describe(highestRisk),
            Origin = origin,
            DomainTerms = domainTerms,
            RelatedServices = relatedServices,
            Evidence = evidence,
            RouteHints = routeHints,
            Methods = methods
                .Select(method => new WorkflowClusterMethodModel
                {
                    Service = method.Action.SourceService,
                    Method = method.Action.MethodName,
                    Role = method.Role,
                    Classification = method.Action.Classification,
                    Risk = ActionRisk.Describe(ActionRisk.GetRiskBand(method.Action)),
                    Summary = method.Action.Summary
                })
                .ToList()
        };
    }

    private static IReadOnlyList<(ActionModel Action, string Role)> BuildOrderedMethodSteps(IEnumerable<ActionModel> actions)
    {
        return actions
            .Select(action => (Action: action, Role: GetLifecycleRole(action.MethodName)))
            .Where(item => item.Role is not null)
            .Select(item => (item.Action, Role: item.Role!))
            .GroupBy(item => (item.Action.SourceService, item.Action.MethodName), new MethodKeyComparer())
            .Select(group => group
                .OrderByDescending(item => item.Action.ExposureMode == ActionExposureMode.Confirmed)
                .ThenByDescending(item => item.Action.Score)
                .First())
            .OrderBy(item => RoleOrder[item.Role])
            .ThenByDescending(item => IsProcessAction(item.Action))
            .ThenBy(item => item.Action.SourceService, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Action.MethodName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> BuildRouteHints(ProjectModel model, IEnumerable<ActionModel> actions)
    {
        var actionKeys = actions
            .Select(action => (action.SourceService, action.MethodName))
            .ToHashSet(new MethodKeyComparer());
        var routes = actions
            .SelectMany(action => action.RelevantRoutes)
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .ToList();

        foreach (var page in model.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Route))
            {
                continue;
            }

            var pageMatches = model.Actions
                .Where(action => page.SuggestedActions.Contains(action.Id, StringComparer.OrdinalIgnoreCase))
                .Any(action => actionKeys.Contains((action.SourceService, action.MethodName)));
            if (pageMatches)
            {
                routes.Add(page.Route);
            }
        }

        return routes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(route => route, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static bool HasEnoughProcessShape(
        IReadOnlyList<(ActionModel Action, string Role)> methods,
        int minimumMethods,
        int minimumRoles)
    {
        return methods.Count >= minimumMethods &&
            methods.Select(method => method.Role).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= minimumRoles &&
            methods.Any(method => IsProcessAction(method.Action));
    }

    private static bool IsProcessAction(ActionModel action)
    {
        return action.Classification is (ActionClassification.Workflow or ActionClassification.Command or ActionClassification.Export or ActionClassification.Validation) &&
            !ActionRisk.IsSafeReadOnly(action);
    }

    private static string BuildSummary(string displayName, IReadOnlyList<(ActionModel Action, string Role)> methods)
    {
        var sequence = string.Join(" -> ", methods.Select(method => $"{method.Action.SourceService}.{method.Action.MethodName}"));
        return $"{displayName} appears to be a multi-step process: {sequence}.";
    }

    private static double CalculateConfidence(
        IReadOnlyList<(ActionModel Action, string Role)> methods,
        IReadOnlyList<string> routeHints,
        IReadOnlyList<string> domainTerms,
        string origin)
    {
        var distinctRoles = methods
            .Select(method => method.Role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var averageActionScore = methods.Count == 0 ? 0.0 : methods.Average(method => method.Action.Score);
        var roleScore = Math.Min(1.0, distinctRoles / 5.0);
        var methodScore = Math.Min(1.0, methods.Count / 5.0);
        var routeScore = routeHints.Count > 0 ? 0.1 : 0.0;
        var domainScore = domainTerms.Count > 0 ? 0.1 : 0.0;
        var originScore = origin.Equals("same-service lifecycle", StringComparison.OrdinalIgnoreCase) ? 0.05 : 0.0;

        return Math.Round(Math.Min(1.0, (averageActionScore * 0.35) + (roleScore * 0.25) + (methodScore * 0.15) + routeScore + domainScore + originScore), 2);
    }

    private static IReadOnlyList<string> ExtractDominantDomainTerms(IEnumerable<ActionModel> actions, IEnumerable<string> extraText)
    {
        return actions
            .SelectMany(action => ExtractDomainTerms(action.SourceService, action.MethodName, action.Name, action.Summary))
            .Concat(extraText.SelectMany(text => ExtractDomainTerms(text)))
            .Where(term => !IgnoredDomainTerms.Contains(term))
            .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .Take(5)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractDomainTerms(params string[] values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(SplitIdentifier)
            .Where(word => word.Length >= 4)
            .Where(word => !RoleOrder.ContainsKey(word))
            .Where(word => !IgnoredDomainTerms.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildDisplayName(
        string sourceName,
        IEnumerable<string> methodNames,
        IReadOnlyList<string> domainTerms)
    {
        var sourceWords = SplitIdentifier(sourceName)
            .Where(word => !IgnoredDomainTerms.Contains(word))
            .ToList();
        var methodWords = methodNames
            .SelectMany(SplitIdentifier)
            .Where(word => !IgnoredDomainTerms.Contains(word))
            .Where(word => !RoleOrder.ContainsKey(word))
            .GroupBy(word => word, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        var leadingWords = sourceWords.Count > 0 ? sourceWords : domainTerms;
        var words = leadingWords
            .Concat(methodWords)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (words.Count == 0)
        {
            words.Add("Business");
        }

        words.Add("Pipeline");
        return string.Join(' ', words);
    }

    private static string BuildRouteClusterName(string? componentName, string route, IReadOnlyList<string> domainTerms)
    {
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            return componentName;
        }

        var routeWords = ExtractDomainTerms(route);
        var words = domainTerms.Concat(routeWords).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        return words.Count == 0 ? "Route Workflow" : string.Join(' ', words);
    }

    private static bool IsGenericWorkflowEngineService(
        ServiceModel service,
        IEnumerable<ActionModel> actions)
    {
        return service.TypeName.Contains("Workflow", StringComparison.OrdinalIgnoreCase) &&
            actions.Any(action => ContainsAny(action.MethodName, GenericWorkflowMethodTerms));
    }

    private static string? GetLifecycleRole(string methodName)
    {
        var normalized = methodName.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? methodName[..^"Async".Length]
            : methodName;

        if (ContainsAny(normalized, "Prepare", "Initialize", "Initiate", "CreateDraft"))
        {
            return "prepare";
        }

        if (ContainsAny(normalized, "CheckStatus", "GetStatus", "Status", "Poll"))
        {
            return "status";
        }

        if (ContainsAny(normalized, "Validate", "Verify", "Check"))
        {
            return "validate";
        }

        if (ContainsAny(normalized, "Upload", "Import"))
        {
            return "upload";
        }

        if (ContainsAny(normalized, "Submit", "Send", "Publish"))
        {
            return "submit";
        }

        if (ContainsAny(normalized, "Promote", "Approve", "Complete", "Finalize"))
        {
            return "promote";
        }

        if (ContainsAny(normalized, "Notify", "Email"))
        {
            return "notify";
        }

        if (ContainsAny(normalized, "Generate", "Build", "Package"))
        {
            return "generate";
        }

        return null;
    }

    private static void AddAction(IDictionary<string, List<ActionModel>> actionMap, string key, ActionModel action)
    {
        if (!actionMap.TryGetValue(key, out var actions))
        {
            actions = [];
            actionMap[key] = actions;
        }

        actions.Add(action);
    }

    private static string BuildClusterKey(WorkflowClusterModel cluster)
        => string.Join(
            "|",
            cluster.Methods
                .Select(method => $"{method.Service}.{method.Method}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, params string[] fragments)
        => fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> SplitIdentifier(string value)
    {
        var normalized = value.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? value[..^"Async".Length]
            : value;

        return IdentifierWordRegex()
            .Matches(normalized)
            .Select(match => match.Value)
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToList();
    }

    private static string NormalizeId(string value)
        => string.Join(
            '-',
            SplitIdentifier(value)
                .Select(word => word.ToLowerInvariant()));

    [GeneratedRegex("[A-Z]?[a-z]+|[A-Z]+(?=[A-Z]|$)|\\d+")]
    private static partial Regex IdentifierWordRegex();

    private sealed class MethodKeyComparer : IEqualityComparer<(string SourceService, string MethodName)>
    {
        public bool Equals((string SourceService, string MethodName) x, (string SourceService, string MethodName) y)
            => string.Equals(x.SourceService, y.SourceService, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.MethodName, y.MethodName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string SourceService, string MethodName) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceService),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.MethodName));
    }

}
