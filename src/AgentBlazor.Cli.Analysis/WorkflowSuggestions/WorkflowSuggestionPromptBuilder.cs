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
        sb.AppendLine("- Do not invent services, methods, routes, entities, or files.");
        sb.AppendLine("- Prefer 3 to 5 high-value workflows.");
        sb.AppendLine("- Use confidence from 0.0 to 1.0.");
        sb.AppendLine("- Suggested C# code is illustrative only and must be short.");
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
      "code": "public sealed class SuggestedCapabilityClass { ... }",
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
        foreach (var service in model.Services
            .Where(AnalysisModelFilters.IsDeveloperFacingService)
            .OrderBy(service => service.TypeName)
            .Take(60))
        {
            var methods = service.Methods
                .Where(method => method.IsPublic)
                .Where(method => AnalysisModelFilters.IsDeveloperFacingMethod(method.Name))
                .OrderBy(method => method.Name)
                .Take(20)
                .Select(method => $"{method.Name}({string.Join(", ", method.Parameters.Select(parameter => $"{parameter.TypeName} {parameter.Name}"))})")
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
}
