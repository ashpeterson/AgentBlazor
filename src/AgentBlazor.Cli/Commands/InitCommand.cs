using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;
using System.Text;

namespace AgentBlazor.Cli.Commands;

public sealed class InitCommand : AsyncCommand<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to solution (.sln/.slnx) or project (.csproj) file")]
        public string? Path { get; init; }

        [CommandOption("-p|--host <PROJECT>")]
        [Description("Name of the Blazor host project (auto-detected if not specified)")]
        public string? HostProject { get; init; }

        [CommandOption("-d|--description <DESCRIPTION>")]
        [Description("Short description of the application")]
        public string? Description { get; init; }

        [CommandOption("-y|--non-interactive")]
        [Description("Skip interactive prompts and use defaults")]
        public bool NonInteractive { get; init; }

        [CommandOption("--save-config")]
        [Description("Save settings to .agentblazorc config file")]
        public bool SaveConfig { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            // Find solution/project path
            var path = await CommandPathResolver.ResolvePathAsync(settings.Path, settings.NonInteractive);
            if (path == null)
            {
                AnsiConsole.MarkupLine("[red]No solution or project file found.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[blue]Project:[/] {path}");

            // Check for existing config file
            var solutionDir = System.IO.Path.GetDirectoryName(path)!;
            var config = await AgentBlazorConfig.ReadAsync(solutionDir);
            if (config != null)
            {
                AnsiConsole.MarkupLine("[grey]Using settings from .agentblazorc[/]");
            }

            // Get host project (priority: CLI flag > config > auto-detect)
            var hostProject = settings.HostProject ?? config?.HostProject;

            // Get description (priority: CLI flag > config > interactive > default)
            var description = settings.Description ?? config?.Description;
            if (string.IsNullOrEmpty(description) && !settings.NonInteractive)
            {
                description = AnsiConsole.Ask(
                    "[yellow]Provide a short description of your application:[/]",
                    "A Blazor application");
            }
            description ??= "A Blazor application";

            // Build the model
            var warnings = new List<string>();
            var modelBuilder = new ModelBuilder();
            modelBuilder.OnProgress += msg => AnsiConsole.MarkupLine($"[grey]{msg}[/]");
            modelBuilder.OnWarning += msg => warnings.Add(msg);

            ProjectModel model = null!;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Analyzing project...", async ctx =>
                {
                    model = await modelBuilder.BuildModelAsync(path, hostProject, description, config);

                    // Write outputs
                    var outputDir = System.IO.Path.GetDirectoryName(path)!;
                    var modelWriter = new ModelWriter();
                    var markdownGenerator = new MarkdownGenerator();

                    ctx.Status("Writing AGENT.md...");
                    await markdownGenerator.GenerateAsync(model, outputDir);
                    await modelWriter.WriteStateAsync(model, outputDir);
                });

            // Display warnings if any
            if (warnings.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[yellow]Warnings ({warnings.Count}):[/]");
                foreach (var warning in warnings.Take(10))
                {
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(warning)}[/]");
                }
                if (warnings.Count > 10)
                {
                    AnsiConsole.MarkupLine($"  [grey]... and {warnings.Count - 10} more[/]");
                }
            }

            // Display summary
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[green]Analysis Complete[/]").RuleStyle("green"));
            AnsiConsole.WriteLine();

            // Stats
            var confirmedCount = model.Actions.Count(a => a.ExposureMode == ActionExposureMode.Confirmed);
            var suggestedCount = model.Actions.Count(a => a.ExposureMode == ActionExposureMode.Suggested && a.Score >= 0.5);

            AnsiConsole.MarkupLine($"[blue]Routes:[/] {model.Routes.Count}");
            AnsiConsole.MarkupLine($"[blue]Services:[/] {model.Services.Count}");
            AnsiConsole.MarkupLine($"[blue]Actions:[/] {confirmedCount} confirmed, {suggestedCount} discovered");
            AnsiConsole.WriteLine();

            // Output path
            var agentMdPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".agentblazor", "AGENT.md");
            AnsiConsole.MarkupLine($"[green]Generated:[/] {agentMdPath}");

            var readinessAnalyzer = new InstallReadinessAnalyzer();
            var readiness = await readinessAnalyzer.AnalyzeAsync(path, model.BlazorHostProject);
            var planner = new ExistingAppScaffoldPlanner();
            var plan = await planner.PlanAsync(path, model.BlazorHostProject);

            RenderSetupSummary(path, model.BlazorHostProject, readiness, plan);

            // Save config if requested
            if (settings.SaveConfig || (!settings.NonInteractive && config == null &&
                AnsiConsole.Confirm("[yellow]Save settings to .agentblazorc?[/]", defaultValue: false)))
            {
                var newConfig = new AgentBlazorConfig
                {
                    HostProject = model.BlazorHostProject,
                    Description = description,
                    WatchDebounceMs = 500,
                    AutoUpdateOnBuild = true
                };
                await newConfig.WriteAsync(solutionDir);
                AnsiConsole.MarkupLine("[grey]Saved .agentblazorc[/]");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            // User-friendly errors (e.g., host project not found)
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

    private static void RenderSetupSummary(
        string inputPath,
        string hostProject,
        InstallReadinessReport readiness,
        ScaffoldPlan plan)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[blue]Setup Summary[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(hostProject)}");
        AnsiConsole.MarkupLine($"[blue]Readiness:[/] {readiness.PassCount} passed, {readiness.WarningCount} warnings, {readiness.MissingCount} missing");

        if (plan.IsBlocked)
        {
            AnsiConsole.MarkupLine("[yellow]Scaffold status:[/] automatic scaffold is blocked for this host shape.");
            if (!string.IsNullOrWhiteSpace(plan.BlockReason))
            {
                AnsiConsole.MarkupLine($"[grey]Reason:[/] {Markup.Escape(plan.BlockReason)}");
            }

            if (!string.IsNullOrWhiteSpace(plan.BlockSuggestedFix))
            {
                AnsiConsole.MarkupLine($"[grey]Next step:[/] {Markup.Escape(plan.BlockSuggestedFix)}");
            }

            AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildCommand("doctor", inputPath, hostProject)}");
            AnsiConsole.MarkupLine("[grey]Refresh command:[/] run `agentblazor update` when code changes.");
            return;
        }

        if (readiness.HostShape.Kind == HostShapeKind.AdvancedReview)
        {
            AnsiConsole.MarkupLine("[yellow]Scaffold status:[/] advanced-host review mode.");
            if (!string.IsNullOrWhiteSpace(plan.BlockReason))
            {
                AnsiConsole.MarkupLine($"[grey]Review mode:[/] {Markup.Escape(plan.BlockReason)}");
            }
        }

        if (!plan.HasChanges)
        {
            AnsiConsole.MarkupLine("[green]Install status:[/] The baseline AgentBlazor wiring is already present.");
            AnsiConsole.MarkupLine("[grey]Next step:[/] run `agentblazor doctor` any time you want to verify the current setup.");
            AnsiConsole.MarkupLine("[grey]Refresh command:[/] run `agentblazor update` when code changes.");
            return;
        }

        var updateCount = plan.Items.Count(item => item.Action == ScaffoldPlanAction.Update);
        var createCount = plan.Items.Count(item => item.Action == ScaffoldPlanAction.Create);
        var reviewCount = plan.Items.Count(item => item.Action == ScaffoldPlanAction.ManualReview);

        if (updateCount > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Will update:[/] {updateCount} file(s)");
        }

        if (createCount > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Will create:[/] {createCount} file(s)");
        }

        if (reviewCount > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Needs review:[/] {reviewCount} item(s) may need host-specific attention");
        }

        AnsiConsole.MarkupLine("[yellow]Recommended provider:[/] use `--provider openai` for the validated path. `azure-openai` and `ollama` are also supported.");
        AnsiConsole.MarkupLine($"[grey]Preview next:[/] {BuildCommand("scaffold", inputPath, hostProject, "--provider openai --diff")}");
        AnsiConsole.MarkupLine($"[grey]Approve next:[/] {BuildCommand("scaffold", inputPath, hostProject, "--provider openai --approve")}");
        AnsiConsole.MarkupLine($"[grey]Verify after apply:[/] {BuildCommand("doctor", inputPath, hostProject)}");
        AnsiConsole.MarkupLine("[grey]Refresh command:[/] run `agentblazor update` when code changes.");
    }

    private static string BuildCommand(string command, string inputPath, string hostProject, string? trailingFlag = null)
    {
        var builder = new StringBuilder("agentblazor ");
        builder.Append(command);
        builder.Append(' ');
        builder.Append(QuoteIfNeeded(inputPath));
        builder.Append(" --host ");
        builder.Append(QuoteIfNeeded(hostProject));

        if (!string.IsNullOrWhiteSpace(trailingFlag))
        {
            builder.Append(' ');
            builder.Append(trailingFlag);
        }

        return $"`{builder}`";
    }

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
