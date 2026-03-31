using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Commands;

public sealed class WatchCommand : AsyncCommand<WatchCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-d|--debounce <MS>")]
        [Description("Debounce time in milliseconds (default: 500)")]
        public int? Debounce { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Find existing state
        var agentBlazorDir = FindAgentBlazorDirectory();
        if (agentBlazorDir == null)
        {
            AnsiConsole.MarkupLine("[red]No .agentblazor directory found. Run 'agentblazor init' first.[/]");
            return 1;
        }

        var outputDir = Path.GetDirectoryName(agentBlazorDir)!;
        var state = await ModelWriter.ReadStateAsync(outputDir);
        if (state == null)
        {
            AnsiConsole.MarkupLine("[red]State file missing. Run 'agentblazor init' to regenerate.[/]");
            return 1;
        }

        // Load config for debounce setting
        var config = await AgentBlazorConfig.ReadAsync(outputDir);
        var debounceMs = settings.Debounce ?? config?.WatchDebounceMs ?? 500;

        var hostProjectDir = FindHostProjectDirectory(state.SolutionPath, state.HostProject);
        if (hostProjectDir == null)
        {
            AnsiConsole.MarkupLine("[red]Could not locate host project directory.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[blue]Watching:[/] {Path.GetFileName(state.SolutionPath)}");
        AnsiConsole.MarkupLine($"[grey]Directory: {hostProjectDir}[/]");
        AnsiConsole.MarkupLine($"[grey]Debounce: {debounceMs}ms[/]");
        AnsiConsole.MarkupLine("[yellow]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var pendingChanges = new HashSet<string>();
        var rebuildLock = new SemaphoreSlim(1, 1);

        using var watcher = new FileSystemWatcher(hostProjectDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Changed += OnFileChange;
        watcher.Created += OnFileChange;
        watcher.Deleted += OnFileChange;
        watcher.Renamed += (s, e) =>
        {
            OnFileChange(s, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath)!, e.OldName!));
            OnFileChange(s, e);
        };

        void OnFileChange(object? sender, FileSystemEventArgs e)
        {
            if (!IsWatchedFile(e.FullPath)) return;

            lock (pendingChanges)
            {
                pendingChanges.Add(e.FullPath);
            }
        }

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(debounceMs, cts.Token);

                List<string> changes;
                lock (pendingChanges)
                {
                    if (pendingChanges.Count == 0) continue;
                    changes = pendingChanges.ToList();
                    pendingChanges.Clear();
                }

                await rebuildLock.WaitAsync(cts.Token);
                try
                {
                    await RebuildAsync(state, changes, outputDir, config, cts.Token);
                }
                finally
                {
                    rebuildLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Watch stopped.[/]");
        return 0;
    }

    private static async Task RebuildAsync(ModelState state, List<string> changedFiles, string outputDir, AgentBlazorConfig? config, CancellationToken ct)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [yellow]Changes ({changedFiles.Count} files)[/]");

        foreach (var file in changedFiles.Take(3))
        {
            AnsiConsole.MarkupLine($"  [grey]- {Path.GetFileName(file)}[/]");
        }
        if (changedFiles.Count > 3)
        {
            AnsiConsole.MarkupLine($"  [grey]... and {changedFiles.Count - 3} more[/]");
        }

        try
        {
            var modelBuilder = new ModelBuilder();
            var model = await modelBuilder.BuildModelAsync(
                state.SolutionPath,
                state.HostProject,
                state.Description,
                config,
                ct);

            var modelWriter = new ModelWriter();
            var markdownGenerator = new MarkdownGenerator();

            await markdownGenerator.GenerateAsync(model, outputDir, ct);
            await modelWriter.WriteStateAsync(model, outputDir, ct);

            AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [green]AGENT.md updated[/] — {model.Routes.Count} routes, {model.Actions.Count(a => a.ExposureMode != ActionExposureMode.Ignored)} actions");
            AnsiConsole.WriteLine();
        }
        catch (OperationCanceledException)
        {
            // Don't log - expected during shutdown
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [red]Error: {Markup.Escape(ex.Message)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[grey]{timestamp}[/] [red]Error: {Markup.Escape(ex.Message)}[/]");
            if (Environment.GetEnvironmentVariable("AGENTBLAZOR_DEBUG") == "1")
            {
                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(ex.StackTrace ?? "")}[/]");
            }
        }
    }

    private static bool IsWatchedFile(string path)
    {
        return path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindAgentBlazorDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();

        var dir = Path.Combine(currentDir, ".agentblazor");
        if (Directory.Exists(dir))
            return dir;

        var parent = Directory.GetParent(currentDir);
        while (parent != null)
        {
            dir = Path.Combine(parent.FullName, ".agentblazor");
            if (Directory.Exists(dir))
                return dir;
            parent = parent.Parent;
        }

        return null;
    }

    private static string? FindHostProjectDirectory(string solutionPath, string hostProjectName)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath);
        if (solutionDir == null) return null;

        var projectDir = Path.Combine(solutionDir, hostProjectName);
        if (Directory.Exists(projectDir))
            return projectDir;

        projectDir = Path.Combine(solutionDir, "src", hostProjectName);
        if (Directory.Exists(projectDir))
            return projectDir;

        var csprojFiles = Directory.GetFiles(solutionDir, $"{hostProjectName}.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length > 0)
            return Path.GetDirectoryName(csprojFiles[0]);

        return solutionDir;
    }
}
