using System.ComponentModel;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentBlazor.Cli.Commands;

public sealed class ScaffoldCommand : AsyncCommand<ScaffoldCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Path to solution (.sln/.slnx) or project (.csproj) file")]
        public string? Path { get; init; }

        [CommandOption("--host <PROJECT>")]
        [Description("Name of the Blazor host project (auto-detected if not specified)")]
        public string? HostProject { get; init; }

        [CommandOption("--dry-run")]
        [Description("Preview the proposed edits without mutating any files")]
        public bool DryRun { get; init; }

        [CommandOption("--approve")]
        [Description("Apply the standard-host scaffold edits")]
        public bool Approve { get; init; }

        [CommandOption("--diff")]
        [Description("Show exact file-level scaffold diffs")]
        public bool Diff { get; init; }

        [CommandOption("--use-local-source <PATH>")]
        [Description("Use local AgentBlazor source projects instead of the AgentBlazor package reference")]
        public string? LocalSourcePath { get; init; }

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

            var localSourcePath = ResolveLocalSourcePath(settings.LocalSourcePath);

            var planner = new ExistingAppScaffoldPlanner();
            var plan = await planner.PlanAsync(path, settings.HostProject);
            var applier = new ExistingAppScaffoldApplier();
            var preview = await applier.PreviewAsync(plan, localSourcePath);

            if (!settings.Approve)
            {
                RenderPreview(plan, preview, settings.DryRun, settings.Diff, localSourcePath);
                return 0;
            }

            if (settings.Diff && preview.HasChanges)
            {
                RenderDiffs(preview);
            }

            var result = await applier.ApplyAsync(plan, preview, localSourcePath);
            RenderApplyResult(plan, result);
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

    private static void RenderPreview(
        ScaffoldPlan plan,
        ScaffoldPreviewResult preview,
        bool dryRun,
        bool showDiff,
        string? localSourcePath)
    {
        AnsiConsole.Write(new Rule("[blue]AgentBlazor Scaffold Preview[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(plan.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(localSourcePath))
        {
            AnsiConsole.MarkupLine($"[blue]Local source:[/] {Markup.Escape(localSourcePath)}");
        }
        if (dryRun)
        {
            AnsiConsole.MarkupLine("[grey]--dry-run supplied.[/]");
        }
        if (showDiff)
        {
            AnsiConsole.MarkupLine("[grey]--diff supplied.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Preview mode is the default. Add --approve to apply these edits.[/]");
        }

        AnsiConsole.WriteLine();

        if (!preview.HasChanges)
        {
            AnsiConsole.MarkupLine("[green]No scaffold changes proposed.[/] The app already has the baseline AgentBlazor wiring.");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("File");
        table.AddColumn("Change");

        foreach (var change in preview.Changes)
        {
            table.AddRow(
                Markup.Escape(change.Path),
                $"{GetChangeKindMarkup(change.ChangeKind)} {Markup.Escape(change.Summary)}");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]Proposed file changes:[/] {preview.ChangedFileCount}");
        AnsiConsole.MarkupLine("[yellow]Needs your decision:[/] choose a model provider after the baseline scaffold is applied.");
        AnsiConsole.MarkupLine($"[grey]Preview command:[/] {BuildCommand(plan.InputPath, plan.HostProjectName, "--diff")}");
        AnsiConsole.MarkupLine($"[grey]Approve command:[/] {BuildCommand(plan.InputPath, plan.HostProjectName, "--approve")}");
        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");

        if (showDiff)
        {
            AnsiConsole.WriteLine();
            RenderDiffs(preview);
        }
    }

    private static string GetChangeKindMarkup(ScaffoldPreviewChangeKind changeKind) =>
        changeKind switch
        {
            ScaffoldPreviewChangeKind.Create => "[green]CREATE[/]",
            ScaffoldPreviewChangeKind.Update => "[yellow]UPDATE[/]",
            _ => "[grey]?[/]"
        };

    private static void RenderApplyResult(ScaffoldPlan plan, ScaffoldApplyResult result)
    {
        AnsiConsole.Write(new Rule("[green]AgentBlazor Scaffold Applied[/]").RuleStyle("green"));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        AnsiConsole.WriteLine();

        if (result.Changes.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No file changes were needed.[/]");
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("File");
        table.AddColumn("Change");

        foreach (var change in result.Changes)
        {
            table.AddRow(
                Markup.Escape(change.Path),
                $"{GetChangeKindMarkup(change.ChangeKind)} {Markup.Escape(change.Summary)}");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]Updated files:[/] {result.ChangedFileCount}");
        if (!string.IsNullOrWhiteSpace(result.ManifestPath))
        {
            AnsiConsole.MarkupLine($"[green]Manifest:[/] {Markup.Escape(result.ManifestPath)}");
        }
        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");
        AnsiConsole.MarkupLine("[grey]Human step:[/] connect a model provider in Program.cs.");
    }

    private static void RenderDiffs(ScaffoldPreviewResult preview)
    {
        AnsiConsole.Write(new Rule("[blue]Scaffold Diff[/]").RuleStyle("blue"));

        foreach (var change in preview.Changes)
        {
            AnsiConsole.MarkupLine($"[blue]{Markup.Escape(change.Path)}[/] [grey]({change.ChangeKind})[/]");
            foreach (var line in BuildDiffLines(change.OriginalContent, change.UpdatedContent))
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Removed:
                        AnsiConsole.MarkupLine($"[red]- {Markup.Escape(line.Text)}[/]");
                        break;
                    case DiffLineKind.Added:
                        AnsiConsole.MarkupLine($"[green]+ {Markup.Escape(line.Text)}[/]");
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(line.Text)}[/]");
                        break;
                }
            }

            AnsiConsole.WriteLine();
        }
    }

    private static IReadOnlyList<DiffLine> BuildDiffLines(string original, string updated)
    {
        var oldLines = NormalizeLines(original);
        var newLines = NormalizeLines(updated);
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];

        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(oldLines[i], newLines[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var diff = new List<DiffLine>();
        var oldIndex = 0;
        var newIndex = 0;

        while (oldIndex < oldLines.Length && newIndex < newLines.Length)
        {
            if (string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal))
            {
                diff.Add(new DiffLine(DiffLineKind.Context, oldLines[oldIndex]));
                oldIndex++;
                newIndex++;
                continue;
            }

            if (lcs[oldIndex + 1, newIndex] >= lcs[oldIndex, newIndex + 1])
            {
                diff.Add(new DiffLine(DiffLineKind.Removed, oldLines[oldIndex]));
                oldIndex++;
            }
            else
            {
                diff.Add(new DiffLine(DiffLineKind.Added, newLines[newIndex]));
                newIndex++;
            }
        }

        while (oldIndex < oldLines.Length)
        {
            diff.Add(new DiffLine(DiffLineKind.Removed, oldLines[oldIndex]));
            oldIndex++;
        }

        while (newIndex < newLines.Length)
        {
            diff.Add(new DiffLine(DiffLineKind.Added, newLines[newIndex]));
            newIndex++;
        }

        return diff;
    }

    private static string[] NormalizeLines(string content)
        => string.IsNullOrEmpty(content)
            ? []
            : content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private enum DiffLineKind
    {
        Context,
        Removed,
        Added
    }

    private sealed record DiffLine(DiffLineKind Kind, string Text);

    private static string? ResolveLocalSourcePath(string? localSourcePath)
    {
        if (!string.IsNullOrWhiteSpace(localSourcePath))
        {
            return Path.GetFullPath(localSourcePath);
        }

        return TryFindAgentBlazorSourceRoot(AppContext.BaseDirectory);
    }

    private static string? TryFindAgentBlazorSourceRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AgentBlazor.Components", "AgentBlazor.Components.csproj");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string BuildCommand(string inputPath, string hostProjectName, string trailingFlag)
        => $"`agentblazor scaffold {QuoteIfNeeded(inputPath)} --host {QuoteIfNeeded(hostProjectName)} {trailingFlag}`";

    private static string BuildDoctorCommand(string inputPath, string hostProjectName)
        => $"`agentblazor doctor {QuoteIfNeeded(inputPath)} --host {QuoteIfNeeded(hostProjectName)}`";

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
