using System.ComponentModel;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentBlazor.Cli.Commands;

public sealed class DoctorCommand : AsyncCommand<DoctorCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to solution (.sln/.slnx) or project (.csproj) file")]
        public string? Path { get; init; }

        [CommandOption("--host <PROJECT>")]
        [Description("Name of the Blazor host project (auto-detected if not specified)")]
        public string? HostProject { get; init; }

        [CommandOption("-y|--non-interactive")]
        [Description("Skip interactive prompts and use defaults")]
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

            var analyzer = new InstallReadinessAnalyzer();
            var report = await analyzer.AnalyzeAsync(path, settings.HostProject);

            RenderReport(report);

            return report.MissingCount > 0 ? 2 : 0;
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

    private static void RenderReport(InstallReadinessReport report)
    {
        AnsiConsole.Write(new Rule("[blue]AgentBlazor Readiness[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(report.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(report.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(report.HostProjectPath)}");
        AnsiConsole.WriteLine();

        var table = new Table().RoundedBorder();
        table.AddColumn("Status");
        table.AddColumn("Check");
        table.AddColumn("Details");

        foreach (var check in report.Checks)
        {
            table.AddRow(
                GetStatusMarkup(check.Status),
                Markup.Escape(check.Title),
                BuildDetailMarkup(check));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var summaryColor = report.MissingCount > 0 ? "yellow" : "green";
        AnsiConsole.MarkupLine(
            $"[{summaryColor}]Summary:[/] {report.PassCount} passed, {report.WarningCount} warnings, {report.MissingCount} missing");

        if (report.MissingCount > 0)
        {
            AnsiConsole.MarkupLine("[grey]Next step:[/] implement the baseline wiring with a future scaffold command or patch the missing items manually.");
        }
    }

    private static string GetStatusMarkup(InstallReadinessStatus status) =>
        status switch
        {
            InstallReadinessStatus.Pass => "[green]PASS[/]",
            InstallReadinessStatus.Warning => "[yellow]WARN[/]",
            InstallReadinessStatus.Missing => "[red]MISS[/]",
            _ => "[grey]?[/]"
        };

    private static string BuildDetailMarkup(InstallReadinessCheck check)
    {
        var lines = new List<string> { Markup.Escape(check.Message) };

        if (!string.IsNullOrWhiteSpace(check.FilePath))
        {
            lines.Add($"[grey]{Markup.Escape(check.FilePath!)}[/]");
        }

        if (!string.IsNullOrWhiteSpace(check.SuggestedFix))
        {
            lines.Add($"[grey]Fix: {Markup.Escape(check.SuggestedFix!)}[/]");
        }

        return string.Join('\n', lines);
    }
}
