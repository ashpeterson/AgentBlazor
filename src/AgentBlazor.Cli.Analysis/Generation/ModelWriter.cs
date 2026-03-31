using System.Text.Json;
using System.Text.Json.Serialization;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Generation;

/// <summary>
/// Writes minimal state for incremental updates.
/// </summary>
public sealed class ModelWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes state file for incremental updates.
    /// </summary>
    public async Task WriteStateAsync(
        ProjectModel model,
        string outputDirectory,
        CancellationToken ct = default)
    {
        var agentBlazorDir = Path.Combine(outputDirectory, ".agentblazor");
        Directory.CreateDirectory(agentBlazorDir);

        var state = new ModelState
        {
            SolutionPath = model.SolutionPath,
            HostProject = model.BlazorHostProject,
            Description = model.Description,
            LastGeneratedUtc = model.GeneratedAtUtc,
            FileHashes = model.FileHashes
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(agentBlazorDir, ".state"), json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads state file for incremental updates.
    /// </summary>
    public static async Task<ModelState?> ReadStateAsync(
        string outputDirectory,
        CancellationToken ct = default)
    {
        var statePath = Path.Combine(outputDirectory, ".agentblazor", ".state");
        if (!File.Exists(statePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(statePath, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<ModelState>(json, JsonOptions);

            // Validate required fields
            if (state == null ||
                string.IsNullOrEmpty(state.SolutionPath) ||
                string.IsNullOrEmpty(state.HostProject))
            {
                return null;
            }

            return state;
        }
        catch (JsonException)
        {
            // Corrupted state file - treat as missing
            return null;
        }
    }
}

public sealed class ModelState
{
    public string SolutionPath { get; init; } = "";
    public string HostProject { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTimeOffset LastGeneratedUtc { get; init; }
    public IReadOnlyDictionary<string, string> FileHashes { get; init; } = new Dictionary<string, string>();
}
