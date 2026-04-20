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

        [CommandOption("--provider <PROVIDER>")]
        [Description("Scaffold provider registration: openai, azure-openai, or ollama")]
        public string? Provider { get; init; }

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
            var provider = ScaffoldProviders.ParseOrThrow(settings.Provider);

            var planner = new ExistingAppScaffoldPlanner();
            var plan = await planner.PlanAsync(path, settings.HostProject);

            if (plan.IsBlocked)
            {
                RenderBlockedPlan(plan);
                return 2;
            }

            var applier = new ExistingAppScaffoldApplier();
            var preview = await applier.PreviewAsync(plan, localSourcePath, provider);

            if (!settings.Approve)
            {
                RenderPreview(plan, preview, settings.DryRun, settings.Diff, localSourcePath, provider);
                return 0;
            }

            if (settings.Diff && preview.HasChanges)
            {
                RenderDiffs(preview);
            }

            var result = await applier.ApplyAsync(plan, preview, localSourcePath, provider);
            RenderApplyResult(plan, result, provider);
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
        string? localSourcePath,
        ScaffoldProvider? provider)
    {
        AnsiConsole.Write(new Rule("[blue]AgentBlazor Scaffold Preview[/]").RuleStyle("blue"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(plan.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(plan.Readiness.UiProjectPath!)}");
        }
        if (!string.IsNullOrWhiteSpace(localSourcePath))
        {
            AnsiConsole.MarkupLine($"[blue]Local source:[/] {Markup.Escape(localSourcePath)}");
        }
        if (provider is { } providerChoice)
        {
            AnsiConsole.MarkupLine($"[blue]Provider:[/] {Markup.Escape(providerChoice.ToDisplayName())}");
        }
        if (plan.Readiness.HostShape.Kind == HostShapeKind.AdvancedReview && !string.IsNullOrWhiteSpace(plan.BlockReason))
        {
            AnsiConsole.MarkupLine($"[yellow]Advanced host:[/] {Markup.Escape(plan.BlockReason)}");
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

        if (!preview.HasChanges && !plan.Items.Any(item => item.Action == ScaffoldPlanAction.ManualReview))
        {
            AnsiConsole.MarkupLine("[green]No scaffold changes proposed.[/] The app already has the baseline AgentBlazor wiring.");
            return;
        }

        if (preview.HasChanges)
        {
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
        }

        RenderManualReviewItems(plan);
        if (provider is { } selectedProvider && WillScaffoldProvider(plan))
        {
            AnsiConsole.MarkupLine($"[green]Provider template:[/] {Markup.Escape(selectedProvider.ToDisplayName())} registration will be scaffolded into Program.cs.");
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProvider.GetConfigurationHint())}");
        }
        else if (provider is { } selectedProviderForManualReview)
        {
            AnsiConsole.MarkupLine($"[yellow]Provider selected:[/] {Markup.Escape(selectedProviderForManualReview.ToDisplayName())} was captured, but startup wiring is still manual review for this host.");
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProviderForManualReview.GetConfigurationHint())}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Needs your decision:[/] add `--provider openai` (recommended), `--provider azure-openai`, or `--provider ollama` for concrete runtime registration.");
        }
        AnsiConsole.MarkupLine($"[grey]Preview command:[/] {BuildCommand(plan.InputPath, plan.HostProjectName, "--diff", provider)}");
        AnsiConsole.MarkupLine($"[grey]Approve command:[/] {BuildCommand(plan.InputPath, plan.HostProjectName, "--approve", provider)}");
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

    private static bool WillScaffoldProvider(ScaffoldPlan plan)
        => plan.Items.Any(item =>
            item.Id == "agentblazor-services" &&
            item.Action != ScaffoldPlanAction.ManualReview);

    private static void RenderBlockedPlan(ScaffoldPlan plan)
    {
        AnsiConsole.Write(new Rule("[yellow]AgentBlazor Scaffold Blocked[/]").RuleStyle("yellow"));
        AnsiConsole.MarkupLine($"[blue]Input:[/] {Markup.Escape(plan.InputPath)}");
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(plan.Readiness.UiProjectPath!)}");
        }
        AnsiConsole.WriteLine();

        if (!string.IsNullOrWhiteSpace(plan.BlockTitle))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(plan.BlockTitle)}:[/] {Markup.Escape(plan.BlockReason ?? string.Empty)}");
        }

        if (!string.IsNullOrWhiteSpace(plan.BlockSuggestedFix))
        {
            AnsiConsole.MarkupLine($"[grey]Next step:[/] {Markup.Escape(plan.BlockSuggestedFix!)}");
        }

        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");
    }

    private static void RenderManualReviewItems(ScaffoldPlan plan)
    {
        var reviewItems = plan.Items
            .Where(item => item.Action == ScaffoldPlanAction.ManualReview)
            .ToArray();

        if (reviewItems.Length == 0)
        {
            return;
        }

        var table = new Table().RoundedBorder();
        table.AddColumn("Review");
        table.AddColumn("Target");
        table.AddColumn("Reason");
        table.AddColumn("Guidance");

        foreach (var item in reviewItems)
        {
            table.AddRow(
                Markup.Escape(item.Summary),
                Markup.Escape(item.TargetPath),
                Markup.Escape(item.Reason),
                Markup.Escape(string.IsNullOrWhiteSpace(item.Guidance) ? "-" : item.Guidance));
        }

        AnsiConsole.MarkupLine($"[yellow]Manual review items:[/] {reviewItems.Length}");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        foreach (var item in reviewItems.Where(item => !string.IsNullOrWhiteSpace(item.Guidance)))
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(item.Summary)}[/]");
            AnsiConsole.MarkupLine($"[grey]Guidance:[/] {Markup.Escape(item.Guidance!)}");
        }

        if (reviewItems.Any(item => !string.IsNullOrWhiteSpace(item.Guidance)))
        {
            AnsiConsole.WriteLine();
        }
    }

    private static void RenderApplyResult(ScaffoldPlan plan, ScaffoldApplyResult result, ScaffoldProvider? provider)
    {
        AnsiConsole.Write(new Rule("[green]AgentBlazor Scaffold Applied[/]").RuleStyle("green"));
        AnsiConsole.MarkupLine($"[blue]Host:[/] {Markup.Escape(plan.HostProjectName)}");
        AnsiConsole.MarkupLine($"[blue]Project:[/] {Markup.Escape(plan.HostProjectPath)}");
        if (!string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath))
        {
            AnsiConsole.MarkupLine($"[blue]UI project:[/] {Markup.Escape(plan.Readiness.UiProjectPath!)}");
        }
        if (provider is { } providerChoice)
        {
            AnsiConsole.MarkupLine($"[blue]Provider:[/] {Markup.Escape(providerChoice.ToDisplayName())}");
        }
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
        RenderManualReviewItems(plan);
        AnsiConsole.MarkupLine($"[grey]Verify command:[/] {BuildDoctorCommand(plan.InputPath, plan.HostProjectName)}");
        if (provider is { } selectedProvider && WillScaffoldProvider(plan))
        {
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProvider.GetConfigurationHint())}");
        }
        else if (provider is { } selectedProviderForManualReview)
        {
            AnsiConsole.MarkupLine($"[grey]Provider note:[/] startup wiring was not auto-applied for this host. Use {Markup.Escape(selectedProviderForManualReview.ToDisplayName())} when completing the manual service registration step.");
            AnsiConsole.MarkupLine($"[grey]Config step:[/] {Markup.Escape(selectedProviderForManualReview.GetConfigurationHint())}");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Human step:[/] choose a provider with `--provider openai` (recommended), `--provider azure-openai`, or `--provider ollama` and rerun scaffold preview if you want concrete Program.cs registration.");
        }
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

    private static string BuildCommand(
        string inputPath,
        string hostProjectName,
        string trailingFlag,
        ScaffoldProvider? provider)
    {
        var providerSegment = provider is null
            ? string.Empty
            : $" --provider {provider.Value.ToOptionValue()}";

        return $"`agentblazor scaffold {QuoteIfNeeded(inputPath)} --host {QuoteIfNeeded(hostProjectName)}{providerSegment} {trailingFlag}`";
    }

    private static string BuildDoctorCommand(string inputPath, string hostProjectName)
        => $"`agentblazor doctor {QuoteIfNeeded(inputPath)} --host {QuoteIfNeeded(hostProjectName)}`";

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
