using System.ComponentModel;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;
using AgentBlazor.Cli.Analysis.WorkflowSuggestions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentBlazor.Cli.Commands;

public sealed class AnalyzeCommand : AsyncCommand<AnalyzeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to solution, project, or directory. Defaults to the current directory.")]
        public string? Path { get; init; }

        [CommandOption("--host <PROJECT>")]
        [Description("Name of the Blazor host project (auto-detected if not specified).")]
        public string? HostProject { get; init; }

        [CommandOption("--output <PATH>")]
        [Description("Path to write the markdown report. Defaults to .agentblazor/analysis.md next to the solution or project.")]
        public string? Output { get; init; }

        [CommandOption("--framework <FRAMEWORK>")]
        [Description("Framework override. v1 currently supports only 'blazor'.")]
        public string Framework { get; init; } = "blazor";

        [CommandOption("--no-readiness")]
        [Description("Skip AgentBlazor install-readiness checks in the report.")]
        public bool NoReadiness { get; init; }

        [CommandOption("--static-only")]
        [Description("Skip the LLM workflow suggestion call and write a static-analysis-only report.")]
        public bool StaticOnly { get; init; }

        [CommandOption("-y|--non-interactive")]
        [Description("Skip interactive prompts and use defaults.")]
        public bool NonInteractive { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            if (!settings.Framework.Equals("blazor", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[red]Unsupported framework:[/] {Markup.Escape(settings.Framework)}");
                AnsiConsole.MarkupLine("[grey]v1 currently supports only `--framework blazor`.[/]");
                return 1;
            }

            var path = await CommandPathResolver.ResolvePathAsync(settings.Path, settings.NonInteractive);
            if (path is null)
            {
                AnsiConsole.MarkupLine("[red]No solution or project file found.[/]");
                return 1;
            }

            var outputDirectory = Path.GetDirectoryName(path)!;
            var config = await AgentBlazorConfig.ReadAsync(outputDirectory);
            var hostProject = settings.HostProject ?? config?.HostProject;
            var description = config?.Description ?? "A Blazor application";
            var outputPath = ResolveOutputPath(settings.Output, outputDirectory);
            var suggestionClient = settings.StaticOnly
                ? null
                : WorkflowSuggestionClientFactory.Create(WorkflowSuggestionClientFactory.FromEnvironment(config));

            var warnings = new List<string>();
            ProjectModel model = null!;
            InstallReadinessReport? readiness = null;
            WorkflowSuggestionSet? workflowSuggestions = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Analyzing project...", async status =>
                {
                    var modelBuilder = new ModelBuilder();
                    modelBuilder.OnProgress += message => status.Status(message);
                    modelBuilder.OnWarning += warning => warnings.Add(warning);

                    model = await modelBuilder.BuildModelAsync(path, hostProject, description, config);

                    if (!settings.NoReadiness)
                    {
                        status.Status("Checking AgentBlazor install readiness...");
                        var readinessAnalyzer = new InstallReadinessAnalyzer();
                        readiness = await readinessAnalyzer.AnalyzeAsync(path, model.BlazorHostProject);
                    }

                    if (!settings.StaticOnly)
                    {
                        status.Status("Generating LLM workflow suggestions...");
                        workflowSuggestions = await suggestionClient!.GenerateAsync(model);
                    }

                    status.Status("Writing analysis report...");
                    var reportGenerator = new AnalysisReportGenerator();
                    await reportGenerator.GenerateAsync(model, outputPath, readiness, workflowSuggestions);
                });

            RenderSummary(model, readiness, workflowSuggestions, outputPath, warnings);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(ex.FileName ?? ex.Message)}");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Unexpected error:[/] {Markup.Escape(ex.Message)}");
            if (Environment.GetEnvironmentVariable("AGENTBLAZOR_DEBUG") == "1")
            {
                AnsiConsole.WriteException(ex);
            }
            else
            {
                AnsiConsole.MarkupLine("[grey]Set AGENTBLAZOR_DEBUG=1 for full stack trace.[/]");
            }

            return 1;
        }
    }

    private static string ResolveOutputPath(string? output, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Path.Combine(outputDirectory, ".agentblazor", "analysis.md");
        }

        return Path.IsPathRooted(output)
            ? output
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), output));
    }

    private static void RenderSummary(
        ProjectModel model,
        InstallReadinessReport? readiness,
        WorkflowSuggestionSet? workflowSuggestions,
        string outputPath,
        IReadOnlyList<string> warnings)
    {
        AnsiConsole.Write(new Rule("[green]Analysis Complete[/]").RuleStyle("green"));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(model.BlazorHostProject)}");
        AnsiConsole.MarkupLine($"[blue]Routes:[/] {model.Routes.Count}");
        AnsiConsole.MarkupLine($"[blue]Services:[/] {model.Services.Count}");
        AnsiConsole.MarkupLine($"[blue]Actions:[/] {model.Actions.Count(action => action.ExposureMode == ActionExposureMode.Confirmed)} confirmed, {model.Actions.Count(action => action.ExposureMode == ActionExposureMode.Suggested)} discovered");
        if (readiness is not null)
        {
            AnsiConsole.MarkupLine($"[blue]Readiness:[/] {readiness.PassCount} passed, {readiness.WarningCount} warnings, {readiness.MissingCount} missing");
        }
        if (workflowSuggestions is not null)
        {
            AnsiConsole.MarkupLine($"[blue]Workflow suggestions:[/] {workflowSuggestions.Suggestions.Count} accepted, {workflowSuggestions.Rejected.Count} rejected");
        }

        if (warnings.Count > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Warnings:[/] {warnings.Count}");
        }

        AnsiConsole.MarkupLine($"[green]Report:[/] {Markup.Escape(outputPath)}");
    }
}
