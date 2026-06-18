using System.Text;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class AnalysisCorpusBuilder
{
    private static readonly HashSet<string> IgnoredTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "agent",
        "async",
        "blazor",
        "class",
        "component",
        "data",
        "model",
        "page",
        "request",
        "response",
        "result",
        "service",
        "services",
        "task",
        "user",
        "workflow"
    };

    public AnalysisCorpus Build(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var chunks = new List<AnalysisCorpusChunk>();
        var fileReferences = new Dictionary<string, FileReferenceAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var route in model.Routes)
        {
            var terms = ExtractTerms(route.Template, route.ComponentName);
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"route:{route.Id}",
                Kind = AnalysisCorpusChunkKind.Route,
                Title = $"Route {route.Template}",
                Text = $"Route {route.Template} renders {route.ComponentName}. Parameters: {string.Join(", ", route.Parameters.Select(parameter => parameter.Name))}.",
                FilePath = route.ComponentFile,
                RelatedRoutes = [route.Template],
                DomainTerms = terms
            });
            AddFileReference(fileReferences, route.ComponentFile, route.ComponentName, route.Template);
        }

        foreach (var page in model.Pages)
        {
            var terms = ExtractTerms(page.Route, page.ComponentName, page.Summary);
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"page:{page.Id}",
                Kind = AnalysisCorpusChunkKind.Page,
                Title = $"Page {page.ComponentName}",
                Text = BuildPageText(page),
                FilePath = page.FilePath,
                RelatedRoutes = string.IsNullOrWhiteSpace(page.Route) ? [] : [page.Route],
                RelatedServices = page.InjectedServices,
                RelatedMethods = page.SuggestedActions,
                DomainTerms = terms
            });
            AddFileReference(fileReferences, page.FilePath, page.ComponentName, page.Route);
        }

        foreach (var service in model.Services.Where(service => AnalysisModelFilters.IsDeveloperFacingService(service, model)))
        {
            var serviceTerms = ExtractTerms(service.TypeName, service.ImplementationType ?? string.Empty);
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"service:{service.TypeName}",
                Kind = AnalysisCorpusChunkKind.Service,
                Title = service.TypeName,
                Text = BuildServiceText(service),
                FilePath = service.FilePath,
                RelatedServices = [service.TypeName],
                RelatedMethods = service.Methods.Select(method => $"{service.TypeName}.{method.Name}").ToList(),
                DomainTerms = serviceTerms
            });
            AddFileReference(fileReferences, service.FilePath, service.TypeName, null);

            foreach (var method in service.Methods.Where(method => method.IsPublic && AnalysisModelFilters.IsDeveloperFacingMethod(method.Name)))
            {
                var action = model.Actions.FirstOrDefault(action =>
                    action.SourceService.Equals(service.TypeName, StringComparison.OrdinalIgnoreCase) &&
                    action.MethodName.Equals(method.Name, StringComparison.OrdinalIgnoreCase));
                if (action is null || !AnalysisModelFilters.IsDeveloperFacingAction(action))
                {
                    continue;
                }

                var methodTerms = ExtractTerms(service.TypeName, method.Name, method.XmlDocSummary ?? string.Empty, action.Summary);
                chunks.Add(new AnalysisCorpusChunk
                {
                    Id = $"method:{service.TypeName}.{method.Name}",
                    Kind = AnalysisCorpusChunkKind.Method,
                    Title = $"{service.TypeName}.{method.Name}",
                    Text = BuildMethodText(service, method, action),
                    FilePath = service.FilePath,
                    RelatedRoutes = action.RelevantRoutes,
                    RelatedServices = [service.TypeName],
                    RelatedMethods = [$"{service.TypeName}.{method.Name}"],
                    DomainTerms = methodTerms
                });
            }
        }

        foreach (var registration in model.DiRegistrations)
        {
            var terms = ExtractTerms(registration.ServiceType, registration.ImplementationType);
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"di:{registration.ServiceType}:{registration.ImplementationType}",
                Kind = AnalysisCorpusChunkKind.DiRegistration,
                Title = $"DI {registration.ServiceType}",
                Text = $"{registration.Lifetime} registration maps {registration.ServiceType} to {registration.ImplementationType}.",
                FilePath = registration.FilePath,
                LineNumber = registration.LineNumber,
                RelatedServices = [registration.ServiceType, registration.ImplementationType],
                DomainTerms = terms
            });
            AddFileReference(fileReferences, registration.FilePath, registration.ImplementationType, null);
        }

        foreach (var action in model.Actions.Where(action => action.ExposureMode == ActionExposureMode.Confirmed))
        {
            var terms = ExtractTerms(action.SourceService, action.MethodName, action.Summary);
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"capability:{action.SourceService}.{action.MethodName}",
                Kind = AnalysisCorpusChunkKind.Capability,
                Title = $"Confirmed action {action.SourceService}.{action.MethodName}",
                Text = $"{action.SourceService}.{action.MethodName} is already exposed to AgentBlazor. Requires approval: {action.RequiresApproval}. Summary: {action.Summary}.",
                FilePath = action.FilePath,
                RelatedRoutes = action.RelevantRoutes,
                RelatedServices = [action.SourceService],
                RelatedMethods = [$"{action.SourceService}.{action.MethodName}"],
                DomainTerms = terms
            });
        }

        foreach (var cluster in model.WorkflowClusters)
        {
            var terms = ExtractTerms(cluster.Name, cluster.SourceService, cluster.Summary)
                .Concat(cluster.DomainTerms)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"workflow:{cluster.Id}",
                Kind = AnalysisCorpusChunkKind.WorkflowCluster,
                Title = cluster.Name,
                Text = BuildWorkflowClusterText(cluster),
                FilePath = cluster.FilePath,
                RelatedRoutes = cluster.RouteHints,
                RelatedServices = cluster.RelatedServices,
                RelatedMethods = cluster.Methods.Select(method => $"{method.Service}.{method.Method}").ToList(),
                DomainTerms = terms
            });
        }

        var routeCorrelations = BuildRouteCorrelations(model);
        foreach (var correlation in routeCorrelations)
        {
            var terms = ExtractTerms(correlation.Route, correlation.ComponentName)
                .Concat(correlation.Services.SelectMany(SplitIdentifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            chunks.Add(new AnalysisCorpusChunk
            {
                Id = $"route-correlation:{NormalizeId(correlation.Route)}",
                Kind = AnalysisCorpusChunkKind.RouteCorrelation,
                Title = $"Route correlation {correlation.Route}",
                Text = $"Route {correlation.Route} correlates {correlation.ComponentName} with services {string.Join(", ", correlation.Services)} and methods {string.Join(", ", correlation.Methods)}.",
                FilePath = correlation.FilePath,
                RelatedRoutes = [correlation.Route],
                RelatedServices = correlation.Services,
                RelatedMethods = correlation.Methods,
                DomainTerms = terms
            });
        }

        var domainTerms = chunks
            .SelectMany(chunk => chunk.DomainTerms)
            .Where(term => !IgnoredTerms.Contains(term))
            .GroupBy(term => term, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .Take(40)
            .ToList();

        return new AnalysisCorpus
        {
            Chunks = chunks,
            RouteCorrelations = routeCorrelations,
            DomainTerms = domainTerms,
            FileReferences = fileReferences.Values
                .Select(reference => new FileReferenceModel
                {
                    Path = reference.Path,
                    Symbols = reference.Symbols.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    Routes = reference.Routes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
                })
                .OrderBy(reference => reference.Path, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static IReadOnlyList<RouteCorrelationModel> BuildRouteCorrelations(ProjectModel model)
    {
        var actionById = model.Actions
            .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(action => action.ExposureMode == ActionExposureMode.Confirmed)
                    .ThenByDescending(action => action.Score)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        var correlations = new List<RouteCorrelationModel>();
        foreach (var page in model.Pages.Where(page => !string.IsNullOrWhiteSpace(page.Route)))
        {
            var actions = page.SuggestedActions
                .Select(actionId => actionById.TryGetValue(actionId, out var action) ? action : null)
                .OfType<ActionModel>()
                .ToList();
            if (actions.Count == 0 && page.InjectedServices.Count == 0)
            {
                continue;
            }

            correlations.Add(new RouteCorrelationModel
            {
                Route = page.Route,
                ComponentName = page.ComponentName,
                FilePath = page.FilePath,
                Services = actions
                    .Select(action => action.SourceService)
                    .Concat(page.InjectedServices)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Methods = actions
                    .Select(action => $"{action.SourceService}.{action.MethodName}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        return correlations;
    }

    private static string BuildPageText(PageModel page)
        => $"Page {page.ComponentName} at route {page.Route}. Injected services: {string.Join(", ", page.InjectedServices)}. Suggested actions: {string.Join(", ", page.SuggestedActions)}. UI elements: {string.Join(", ", page.DetectedUiElements)}.";

    private static string BuildServiceText(ServiceModel service)
        => $"Service {service.TypeName} ({service.Lifetime}) in {service.FilePath}. Service types: {string.Join(", ", service.ServiceTypes)}. Public methods: {string.Join(", ", service.Methods.Where(method => method.IsPublic).Select(method => method.Name))}.";

    private static string BuildMethodText(ServiceModel service, ServiceMethodModel method, ActionModel action)
    {
        var parameters = string.Join(", ", method.Parameters.Select(parameter => $"{parameter.TypeName} {parameter.Name}"));
        return $"{service.TypeName}.{method.Name}({parameters}) returns {method.ReturnType}. Classification: {action.Classification}. Risk: {ActionRisk.Describe(ActionRisk.GetRiskBand(action))}. Requires approval: {action.RequiresApproval}. Routes: {string.Join(", ", action.RelevantRoutes)}. Summary: {FirstNonEmpty(action.Summary, method.XmlDocSummary, action.Name)}.";
    }

    private static string BuildWorkflowClusterText(WorkflowClusterModel cluster)
    {
        var sb = new StringBuilder();
        sb.Append(cluster.Summary);
        sb.Append(" Origin: ").Append(cluster.Origin).Append('.');
        sb.Append(" Risk: ").Append(cluster.Risk).Append('.');
        sb.Append(" Requires approval: ").Append(cluster.RequiresApproval).Append('.');
        sb.Append(" Methods: ").Append(string.Join(" -> ", cluster.Methods.Select(method => $"{method.Service}.{method.Method}"))).Append('.');
        sb.Append(" Evidence: ").Append(string.Join("; ", cluster.Evidence)).Append('.');
        if (cluster.RouteHints.Count > 0)
        {
            sb.Append(" Routes: ").Append(string.Join(", ", cluster.RouteHints)).Append('.');
        }

        return sb.ToString();
    }

    public static IReadOnlyList<string> ExtractTerms(params string[] values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(SplitIdentifier)
            .Where(term => term.Length >= 4)
            .Where(term => !IgnoredTerms.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> SplitIdentifier(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(character) && !char.IsUpper(current[^1]))
            {
                Flush();
            }

            current.Append(char.ToLowerInvariant(character));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            words.Add(current.ToString());
            current.Clear();
        }
    }

    private static string NormalizeId(string value)
        => string.Join('-', SplitIdentifier(value)).ToLowerInvariant();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static void AddFileReference(
        Dictionary<string, FileReferenceAccumulator> references,
        string path,
        string? symbol,
        string? route)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!references.TryGetValue(path, out var reference))
        {
            reference = new FileReferenceAccumulator(path);
            references[path] = reference;
        }

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            reference.Symbols.Add(symbol);
        }

        if (!string.IsNullOrWhiteSpace(route))
        {
            reference.Routes.Add(route);
        }
    }

    private sealed class FileReferenceAccumulator(string path)
    {
        public string Path { get; } = path;

        public HashSet<string> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Routes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
