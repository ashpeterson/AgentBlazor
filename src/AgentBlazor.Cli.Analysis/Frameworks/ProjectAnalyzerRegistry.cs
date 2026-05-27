using AgentBlazor.Cli.Analysis.Blazor;

namespace AgentBlazor.Cli.Analysis.Frameworks;

public sealed class ProjectAnalyzerRegistry
{
    private readonly BlazorProjectAnalyzer _blazorProjectAnalyzer = new();

    public IProjectAnalyzer<BlazorFrameworkContext> ResolveBlazor(string framework, string solutionOrProjectPath)
    {
        if (!framework.Equals("blazor", StringComparison.OrdinalIgnoreCase) &&
            !framework.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported framework '{framework}'. v1 currently supports only 'blazor'.");
        }

        if (!_blazorProjectAnalyzer.CanAnalyze(solutionOrProjectPath))
        {
            throw new InvalidOperationException(
                $"Could not detect a Blazor project at '{solutionOrProjectPath}'. Pass a .sln, .slnx, .csproj, or project directory containing Razor components.");
        }

        return _blazorProjectAnalyzer;
    }
}
