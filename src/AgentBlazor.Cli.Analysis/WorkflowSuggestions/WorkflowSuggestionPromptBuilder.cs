using System.Text;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.WorkflowSuggestions;

public sealed class WorkflowSuggestionPromptBuilder
{
    public string Build(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sb = new StringBuilder();
        sb.AppendLine("You are analyzing a Blazor application for AgentBlazor workflow onboarding.");
        sb.AppendLine("Suggest workflows that can be built from the existing routes, services, and methods.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Return JSON only. No markdown.");
        sb.AppendLine("- Only reference methods listed in the static analysis summary.");
        sb.AppendLine("- In JSON method fields, use method names only, without parameter lists or return types.");
        sb.AppendLine("- Do not invent services, methods, routes, entities, or files.");
        sb.AppendLine("- Prefer 3 to 5 high-value workflows.");
        sb.AppendLine("- Use confidence from 0.0 to 1.0.");
        sb.AppendLine("- Suggested C# code is illustrative only and must be short.");
        sb.AppendLine("- Suggested C# code must use AgentBlazor CapabilityResult and only call listed methods.");
        sb.AppendLine("- Do not invent DTO or entity types in code. If a type is unclear, use comments rather than fake types.");
        sb.AppendLine("- If a listed method has approvalRecommended=true, the suggested [AgentAction] example must include RequiresApproval = true.");
        sb.AppendLine();
        sb.AppendLine("JSON shape:");
        sb.AppendLine("""
{
  "workflows": [
    {
      "name": "Workflow name",
      "description": "What the workflow helps the user do.",
      "methods": [
        { "service": "ExistingService", "method": "ExistingMethodAsync" }
      ],
      "capabilityClass": "SuggestedCapabilityClass",
      "code": "public sealed class SuggestedCapabilityClass { public async Task<CapabilityResult> RunAsync() { /* call ExistingService.ExistingMethodAsync */ return CapabilityResult.Success(\"Done.\"); } }",
      "reasoning": "Why this workflow fits the app.",
      "confidence": 0.8
    }
  ]
}
""");
        sb.AppendLine();
        sb.AppendLine("Static analysis summary:");
        sb.AppendLine($"Application: {model.AppName}");
        sb.AppendLine($"Host project: {model.BlazorHostProject}");
        sb.AppendLine();
        AppendRoutes(sb, model);
        AppendConfirmedActions(sb, model);
        AppendServices(sb, model);
        return sb.ToString();
    }

    private static void AppendRoutes(StringBuilder sb, ProjectModel model)
    {
        sb.AppendLine("Routes:");
        foreach (var route in model.Routes.OrderBy(route => route.Template).Take(40))
        {
            sb.AppendLine($"- {route.Template}: {route.ComponentName} ({route.ComponentFile})");
        }

        if (model.Routes.Count > 40)
        {
            sb.AppendLine($"- ... {model.Routes.Count - 40} more routes omitted");
        }

        sb.AppendLine();
    }

    private static void AppendConfirmedActions(StringBuilder sb, ProjectModel model)
    {
        var confirmed = model.Actions
            .Where(action => action.ExposureMode == ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .OrderBy(action => action.SourceService)
            .ThenBy(action => action.MethodName)
            .Take(80)
            .ToList();

        sb.AppendLine("Confirmed AgentBlazor actions:");
        if (confirmed.Count == 0)
        {
            sb.AppendLine("- none");
            sb.AppendLine();
            return;
        }

        foreach (var action in confirmed)
        {
            sb.AppendLine($"- {action.SourceService}.{action.MethodName}: approval={action.RequiresApproval}, summary={action.Summary}");
        }

        sb.AppendLine();
    }

    private static void AppendServices(StringBuilder sb, ProjectModel model)
    {
        sb.AppendLine("Discovered services and public methods:");
        var candidateActions = model.Actions
            .Where(action => action.ExposureMode is ActionExposureMode.Suggested or ActionExposureMode.Confirmed)
            .Where(AnalysisModelFilters.IsDeveloperFacingAction)
            .GroupBy(
                action => (action.SourceService, action.MethodName),
                new MethodKeyComparer())
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(action => action.ExposureMode == ActionExposureMode.Confirmed)
                    .ThenByDescending(action => action.Score)
                    .First(),
                new MethodKeyComparer());

        foreach (var service in model.Services
            .Where(service => AnalysisModelFilters.IsDeveloperFacingService(service, model))
            .OrderBy(service => service.TypeName)
            .Take(60))
        {
            var methods = service.Methods
                .Where(method => method.IsPublic)
                .Where(method => AnalysisModelFilters.IsDeveloperFacingMethod(method.Name))
                .Where(method => candidateActions.ContainsKey((service.TypeName, method.Name)))
                .OrderBy(method => method.Name)
                .Take(20)
                .Select(method =>
                {
                    var action = candidateActions[(service.TypeName, method.Name)];
                    var approvalRecommended = action.IsMutationLikely ||
                        action.RequiresApproval ||
                        action.Classification is ActionClassification.Command or ActionClassification.Workflow;
                    return $"{method.Name}({string.Join(", ", method.Parameters.Select(parameter => $"{parameter.TypeName} {parameter.Name}"))}) [mutation={approvalRecommended.ToString().ToLowerInvariant()}, approvalRecommended={approvalRecommended.ToString().ToLowerInvariant()}]";
                })
                .ToList();

            if (methods.Count == 0)
            {
                continue;
            }

            sb.AppendLine($"- {service.TypeName} [{service.Lifetime}]");
            foreach (var method in methods)
            {
                sb.AppendLine($"  - {method}");
            }
        }
    }

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
