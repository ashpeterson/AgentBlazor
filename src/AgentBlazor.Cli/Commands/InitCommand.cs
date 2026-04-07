using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Commands;

public sealed class InitCommand : AsyncCommand<InitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to solution (.sln/.slnx) or project (.csproj) file")]
        public string? Path { get; init; }

        [CommandOption("-h|--host <PROJECT>")]
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
            var path = await ResolvePath(settings.Path, settings.NonInteractive);
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
            AnsiConsole.MarkupLine("[grey]Run 'agentblazor update' to refresh when code changes.[/]");

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

    private static Task<string?> ResolvePath(string? path, bool nonInteractive)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return Task.FromResult<string?>(System.IO.Path.GetFullPath(path));
        }

        // Search current directory
        var currentDir = Directory.GetCurrentDirectory();

        // Look for solution files first
        var slnFiles = Directory.GetFiles(currentDir, "*.sln")
            .Concat(Directory.GetFiles(currentDir, "*.slnx"))
            .ToList();

        if (slnFiles.Count == 1)
        {
            return Task.FromResult<string?>(slnFiles[0]);
        }

        if (slnFiles.Count > 1)
        {
            if (nonInteractive)
            {
                return Task.FromResult<string?>(slnFiles[0]);
            }

            var solutionChoices = slnFiles
                .Select(file => System.IO.Path.GetFileName(file) ?? file)
                .ToList();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Multiple solution files found. Select one:")
                    .AddChoices(solutionChoices));

            return Task.FromResult<string?>(slnFiles.First(f => System.IO.Path.GetFileName(f) == selected));
        }

        // Look for project files
        var csprojFiles = Directory.GetFiles(currentDir, "*.csproj");
        if (csprojFiles.Length == 1)
        {
            return Task.FromResult<string?>(csprojFiles[0]);
        }

        if (csprojFiles.Length > 1 && !nonInteractive)
        {
            var projectChoices = csprojFiles
                .Select(file => System.IO.Path.GetFileName(file) ?? file)
                .ToList();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Multiple project files found. Select one:")
                    .AddChoices(projectChoices));

            return Task.FromResult<string?>(csprojFiles.First(f => System.IO.Path.GetFileName(f) == selected));
        }

        return Task.FromResult<string?>(null);
    }
}
