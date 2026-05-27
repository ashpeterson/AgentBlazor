using Spectre.Console;

namespace AgentBlazor.Cli;

internal static class CommandPathResolver
{
    public static Task<string?> ResolvePathAsync(string? path, bool nonInteractive)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return Task.FromResult<string?>(System.IO.Path.GetFullPath(path));
        }

        var searchDir = !string.IsNullOrEmpty(path) && Directory.Exists(path)
            ? System.IO.Path.GetFullPath(path)
            : Directory.GetCurrentDirectory();

        var solutionFiles = Directory.GetFiles(searchDir, "*.sln")
            .Concat(Directory.GetFiles(searchDir, "*.slnx"))
            .ToList();

        if (solutionFiles.Count == 1)
        {
            return Task.FromResult<string?>(solutionFiles[0]);
        }

        if (solutionFiles.Count > 1)
        {
            if (nonInteractive)
            {
                return Task.FromResult<string?>(solutionFiles[0]);
            }

            var solutionChoices = solutionFiles
                .Select(file => System.IO.Path.GetFileName(file) ?? file)
                .ToList();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Multiple solution files found. Select one:")
                    .AddChoices(solutionChoices));

            return Task.FromResult<string?>(solutionFiles.First(f => System.IO.Path.GetFileName(f) == selected));
        }

        var csprojFiles = Directory.GetFiles(searchDir, "*.csproj");
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
