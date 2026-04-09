using System.ComponentModel;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentBlazor.Cli.Commands;

public sealed class ValidateCommand : AsyncCommand<ValidateCommand.Settings>
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

            var analyzer = new InstallValidationAnalyzer();
            var report = await analyzer.AnalyzeAsync(path, settings.HostProject);
            RenderReport(report);
            return report.HasBlockingIssues ? 2 : 0;
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

    private static void RenderReport(InstallValidationReport report)
    {
        var readiness = report.Readiness;
        AnsiConsole.Write(new Rule("[blue]AgentBlazor Validate[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(readiness.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(readiness.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(readiness.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(readiness.UiProjectPath)}");
        }
        AnsiConsole.WriteLine();

        var readinessTable = new Table().RoundedBorder();
        readinessTable.AddColumn("Status");
        readinessTable.AddColumn("Readiness Check");
        readinessTable.AddColumn("Details");

        foreach (var check in readiness.Checks)
        {
            readinessTable.AddRow(
                GetStatusMarkup(check.Status),
                Markup.Escape(check.Title),
                BuildDetailMarkup(check));
        }

        AnsiConsole.Write(new Rule("[blue]Readiness[/]").RuleStyle("blue"));
        AnsiConsole.Write(readinessTable);
        AnsiConsole.WriteLine();

        var validationTable = new Table().RoundedBorder();
        validationTable.AddColumn("Status");
        validationTable.AddColumn("Validation Check");
        validationTable.AddColumn("Details");

        foreach (var check in report.Checks)
        {
            validationTable.AddRow(
                GetStatusMarkup(check.Status),
                Markup.Escape(check.Title),
                BuildDetailMarkup(check));
        }

        AnsiConsole.Write(new Rule("[blue]Validation[/]").RuleStyle("blue"));
        AnsiConsole.Write(validationTable);
        AnsiConsole.WriteLine();

        var summaryColor = report.HasBlockingIssues ? "yellow" : "green";
        AnsiConsole.MarkupLine(
            $"[{summaryColor}]Summary:[/] readiness {readiness.PassCount}/{readiness.Checks.Count} pass with {readiness.WarningCount} warnings and {readiness.MissingCount} missing; validation {report.PassCount}/{report.Checks.Count} pass with {report.WarningCount} warnings and {report.MissingCount} missing");

        if (report.HasBlockingIssues)
        {
            AnsiConsole.MarkupLine("[grey]Next step:[/] fix the missing readiness or validation items, then rerun `agentblazor validate`.");
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
