using System.ComponentModel;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Blazor;
using AgentBlazor.Cli.Analysis.Frameworks;
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
        [Description("Framework override. v1 currently supports 'auto' and 'blazor'.")]
        public string Framework { get; init; } = "auto";

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
            var clientOptions = WorkflowSuggestionClientFactory.FromEnvironment(config);
            clientOptions = settings.StaticOnly
                ? clientOptions
                : PromptForMissingOpenAIApiKey(clientOptions, settings.NonInteractive);
            var suggestionClient = settings.StaticOnly
                ? null
                : WorkflowSuggestionClientFactory.Create(clientOptions);
            var analyzerRegistry = new ProjectAnalyzerRegistry();
            var analyzer = analyzerRegistry.ResolveBlazor(settings.Framework, path);

            var warnings = new List<string>();
            ProjectModel model = null!;
            InstallReadinessReport? readiness = null;
            BlazorFrameworkContext? blazorContext = null;
            WorkflowSuggestionSet? workflowSuggestions = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Analyzing project...", async status =>
                {
                    var analysis = await analyzer.AnalyzeAsync(
                        path,
                        hostProject,
                        description,
                        config,
                        includeReadiness: !settings.NoReadiness);
                    model = analysis.Model;
                    readiness = analysis.Readiness;
                    blazorContext = analysis.Context;

                    if (!settings.StaticOnly)
                    {
                        status.Status("Generating LLM workflow suggestions...");
                        workflowSuggestions = await suggestionClient!.GenerateAsync(model);
                    }

                    status.Status("Writing analysis report...");
                    var reportGenerator = new AnalysisReportGenerator();
                    await reportGenerator.GenerateAsync(model, outputPath, readiness, workflowSuggestions);
                });

            RenderSummary(model, readiness, blazorContext, workflowSuggestions, outputPath, warnings);
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
            if (IsSdkResolutionFailure(ex))
            {
                AnsiConsole.MarkupLine("[red]SDK resolution failed while loading the project.[/]");
                AnsiConsole.MarkupLine(Markup.Escape(GetRelevantSdkResolutionMessage(ex.Message)));
                AnsiConsole.MarkupLine("[grey]Install the requested SDK, or update the target repo's global.json rollForward setting before running analyze.[/]");
                return 1;
            }

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

    private static bool IsSdkResolutionFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("A compatible .NET SDK was not found", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("hostfxr_resolve_sdk", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetRelevantSdkResolutionMessage(string message)
    {
        var learnMoreIndex = message.IndexOf("Learn about SDK resolution:", StringComparison.OrdinalIgnoreCase);
        return learnMoreIndex > 0
            ? message[..learnMoreIndex].Trim()
            : message;
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

    private static WorkflowSuggestionClientOptions PromptForMissingOpenAIApiKey(
        WorkflowSuggestionClientOptions options,
        bool nonInteractive)
    {
        if (!options.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(options.OpenAIApiKey) ||
            nonInteractive ||
            Console.IsInputRedirected ||
            Console.IsOutputRedirected)
        {
            return options;
        }

        AnsiConsole.MarkupLine("[yellow]No OpenAI API key found.[/]");
        AnsiConsole.MarkupLine("[grey]Enter a key for this analysis run. It will not be written to disk.[/]");

        var apiKey = AnsiConsole.Prompt(
            new TextPrompt<string>("OpenAI API key:")
                .PromptStyle("grey")
                .Secret());

        return string.IsNullOrWhiteSpace(apiKey)
            ? options
            : options with { OpenAIApiKey = apiKey };
    }

    private static void RenderSummary(
        ProjectModel model,
        InstallReadinessReport? readiness,
        BlazorFrameworkContext? blazorContext,
        WorkflowSuggestionSet? workflowSuggestions,
        string outputPath,
        IReadOnlyList<string> warnings)
    {
        AnsiConsole.Write(new Rule("[green]Analysis Complete[/]").RuleStyle("green"));
        var reportableActions = model.Actions.Where(AnalysisModelFilters.IsDeveloperFacingAction).ToList();
        var confirmedActionCount = reportableActions.Count(action => action.ExposureMode == ActionExposureMode.Confirmed);
        var discoveredActionCount = reportableActions.Count(action => action.ExposureMode == ActionExposureMode.Suggested);
        var reportableServiceCount = model.Services.Count(service => AnalysisModelFilters.IsDeveloperFacingService(service, model));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(model.BlazorHostProject)}");
        AnsiConsole.MarkupLine($"[blue]Routes:[/] {model.Routes.Count}");
        AnsiConsole.MarkupLine($"[blue]Services:[/] {reportableServiceCount}");
        AnsiConsole.MarkupLine($"[blue]Actions:[/] {confirmedActionCount} confirmed, {discoveredActionCount} discovered");
        if (readiness is not null)
        {
            AnsiConsole.MarkupLine($"[blue]Readiness:[/] {readiness.PassCount} passed, {readiness.WarningCount} warnings, {readiness.MissingCount} missing");
        }
        if (blazorContext?.HostShape is not null)
        {
            AnsiConsole.MarkupLine($"[blue]Host shape:[/] {Markup.Escape(blazorContext.HostShape.Title)}");
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
