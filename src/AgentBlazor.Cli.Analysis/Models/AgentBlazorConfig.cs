using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentBlazor.Cli.Analysis.Models;

/// <summary>
/// Configuration for the agentblazor CLI tool.
/// Stored in .agentblazorc at the solution root.
/// </summary>
public sealed class AgentBlazorConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Name of the Blazor host project to analyze.
    /// If not specified, auto-detection is used.
    /// </summary>
    public string? HostProject { get; init; }

    /// <summary>
    /// Short description of the application for AGENT.md header.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Debounce time in milliseconds for watch mode.
    /// Default: 500ms
    /// </summary>
    public int? WatchDebounceMs { get; init; }

    /// <summary>
    /// Additional service class suffixes to detect (beyond built-in patterns).
    /// Example: ["Interactor", "UseCase"]
    /// </summary>
    public List<string>? AdditionalServiceSuffixes { get; init; }

    /// <summary>
    /// Additional domain verbs to recognize as actions.
    /// Example: ["Claim", "Release", "Reserve"]
    /// </summary>
    public List<string>? AdditionalDomainVerbs { get; init; }

    /// <summary>
    /// Method name patterns to exclude from action detection.
    /// Example: ["Setup", "Cleanup", "Initialize"]
    /// </summary>
    public List<string>? ExcludeMethodPatterns { get; init; }

    /// <summary>
    /// Service name patterns to exclude from analysis.
    /// Example: ["Test", "Mock", "Stub"]
    /// </summary>
    public List<string>? ExcludeServicePatterns { get; init; }

    /// <summary>
    /// Directories to exclude from scanning (relative to host project).
    /// Example: ["Migrations", "Generated"]
    /// </summary>
    public List<string>? ExcludeDirectories { get; init; }

    /// <summary>
    /// Whether to auto-update AGENT.md on build.
    /// Default: true (via MSBuild target)
    /// </summary>
    public bool? AutoUpdateOnBuild { get; init; }

    /// <summary>
    /// Provider used by `agentblazor analyze` for workflow suggestions.
    /// Supported v1 values: openai, azure-openai.
    /// </summary>
    public string? AnalyzeProvider { get; init; }

    /// <summary>
    /// Model name used by `agentblazor analyze`.
    /// API keys should be supplied through environment variables, not this file.
    /// </summary>
    public string? AnalyzeModel { get; init; }

    /// <summary>
    /// Reads configuration from .agentblazorc file in the specified directory.
    /// Returns null if file doesn't exist.
    /// </summary>
    public static async Task<AgentBlazorConfig?> ReadAsync(string directory, CancellationToken ct = default)
    {
        var configPath = FindConfigFile(directory);
        if (configPath == null) return null;

        try
        {
            var json = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AgentBlazorConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Invalid JSON - treat as no config
            return null;
        }
        catch (IOException)
        {
            // File access issue - treat as no config
            return null;
        }
    }

    /// <summary>
    /// Writes configuration to .agentblazorc file in the specified directory.
    /// </summary>
    public async Task WriteAsync(string directory, CancellationToken ct = default)
    {
        var configPath = Path.Combine(directory, ".agentblazorc");
        var json = JsonSerializer.Serialize(this, JsonOptions);
        await File.WriteAllTextAsync(configPath, json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the .agentblazorc file, searching up the directory tree.
    /// </summary>
    public static string? FindConfigFile(string startDirectory)
    {
        var currentDir = startDirectory;

        while (!string.IsNullOrEmpty(currentDir))
        {
            var configPath = Path.Combine(currentDir, ".agentblazorc");
            if (File.Exists(configPath))
                return configPath;

            currentDir = Path.GetDirectoryName(currentDir);
        }

        return null;
    }

    /// <summary>
    /// Creates a default configuration with common settings.
    /// </summary>
    public static AgentBlazorConfig CreateDefault(string? hostProject = null, string? description = null)
    {
        return new AgentBlazorConfig
        {
            HostProject = hostProject,
            Description = description,
            WatchDebounceMs = 500,
            AutoUpdateOnBuild = true
        };
    }
}
