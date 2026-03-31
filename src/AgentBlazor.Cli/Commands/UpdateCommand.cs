using System.ComponentModel;
using System.Security.Cryptography;
using Spectre.Console;
using Spectre.Console.Cli;
using AgentBlazor.Cli.Analysis;
using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Commands;

public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-f|--force")]
        [Description("Force full rebuild even if no changes detected")]
        public bool Force { get; init; }

        [CommandOption("-y|--non-interactive")]
        [Description("Skip interactive prompts (for CI/build integration)")]
        public bool NonInteractive { get; init; }

        [CommandOption("-q|--quiet")]
        [Description("Suppress non-essential output")]
        public bool Quiet { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        void Log(string message)
        {
            if (!settings.Quiet)
                AnsiConsole.MarkupLine(message);
        }

        try
        {
            // Find existing state
            var agentBlazorDir = FindAgentBlazorDirectory();
            if (agentBlazorDir == null)
            {
                if (!settings.Quiet)
                    AnsiConsole.MarkupLine("[red]No .agentblazor directory found. Run 'agentblazor init' first.[/]");
                return 1;
            }

            var state = await ModelWriter.ReadStateAsync(Path.GetDirectoryName(agentBlazorDir)!);
            if (state == null)
            {
                if (!settings.Quiet)
                    AnsiConsole.MarkupLine("[red]State file missing. Run 'agentblazor init' to regenerate.[/]");
                return 1;
            }

            Log($"[blue]Project:[/] {Path.GetFileName(state.SolutionPath)}");
            Log($"[grey]Last updated: {state.LastGeneratedUtc:yyyy-MM-dd HH:mm:ss} UTC[/]");

            // Check for changes
            var hostProjectDir = FindHostProjectDirectory(state.SolutionPath, state.HostProject);
            if (hostProjectDir == null)
            {
                AnsiConsole.MarkupLine("[red]Could not locate host project directory.[/]");
                return 1;
            }

            var changedFiles = new List<string>();
            var newFiles = new List<string>();

            if (!settings.Force)
            {
                var currentHashes = await ComputeFileHashesAsync(hostProjectDir);

                foreach (var (file, hash) in currentHashes)
                {
                    if (state.FileHashes.TryGetValue(file, out var oldHash))
                    {
                        if (hash != oldHash)
                            changedFiles.Add(file);
                    }
                    else
                    {
                        newFiles.Add(file);
                    }
                }

                if (changedFiles.Count == 0 && newFiles.Count == 0)
                {
                    Log("[green]No changes detected. AGENT.md is up to date.[/]");
                    return 0;
                }

                Log($"[yellow]Changes:[/] {changedFiles.Count} modified, {newFiles.Count} new");
            }

            // Load config
            var solutionDir = Path.GetDirectoryName(state.SolutionPath);
            var config = solutionDir != null ? await AgentBlazorConfig.ReadAsync(solutionDir) : null;

            // Rebuild
            var warnings = new List<string>();
            var modelBuilder = new ModelBuilder();
            modelBuilder.OnWarning += msg => warnings.Add(msg);
            var model = await modelBuilder.BuildModelAsync(
                state.SolutionPath,
                state.HostProject,
                state.Description,
                config);

            var outputDir = solutionDir ?? Path.GetDirectoryName(state.SolutionPath)!;
            var modelWriter = new ModelWriter();
            var markdownGenerator = new MarkdownGenerator();

            await markdownGenerator.GenerateAsync(model, outputDir);
            await modelWriter.WriteStateAsync(model, outputDir);

            // Report warnings
            if (!settings.Quiet && warnings.Count > 0)
            {
                AnsiConsole.MarkupLine($"[yellow]Warnings ({warnings.Count}):[/]");
                foreach (var warning in warnings.Take(5))
                {
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(warning)}[/]");
                }
                if (warnings.Count > 5)
                {
                    AnsiConsole.MarkupLine($"  [grey]... and {warnings.Count - 5} more[/]");
                }
            }

            // Report
            if (!settings.Quiet)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]Updated.[/]");
                AnsiConsole.MarkupLine($"[blue]Routes:[/] {model.Routes.Count}  [blue]Actions:[/] {model.Actions.Count(a => a.ExposureMode != ActionExposureMode.Ignored)}");
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            if (!settings.Quiet)
                AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            if (!settings.Quiet)
                AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (FileNotFoundException ex)
        {
            if (!settings.Quiet)
                AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(ex.FileName ?? ex.Message)}");
            return 1;
        }
        catch (Exception ex)
        {
            if (!settings.Quiet)
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
            }
            return 1;
        }
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

        // Look for host project directory
        var projectDir = Path.Combine(solutionDir, hostProjectName);
        if (Directory.Exists(projectDir))
            return projectDir;

        // Try src/ subfolder
        projectDir = Path.Combine(solutionDir, "src", hostProjectName);
        if (Directory.Exists(projectDir))
            return projectDir;

        // Search for .csproj
        var csprojFiles = Directory.GetFiles(solutionDir, $"{hostProjectName}.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length > 0)
            return Path.GetDirectoryName(csprojFiles[0]);

        return solutionDir;
    }

    private static async Task<Dictionary<string, string>> ComputeFileHashesAsync(string directory)
    {
        var hashes = new Dictionary<string, string>();

        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ||
                       f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(directory, file);
            var content = await File.ReadAllBytesAsync(file);
            var hash = Convert.ToHexString(SHA256.HashData(content));
            hashes[relativePath] = hash;
        }

        return hashes;
    }
}
