using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentBlazor.Cli.Analysis;

public sealed class SkillViewStore
{
    public async Task<string> ViewAsync(
        string agentBlazorDirectory,
        string? skillName = null,
        string? referencePath = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentBlazorDirectory);

        var skillsDirectory = Path.Combine(agentBlazorDirectory, "skills");
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return await File.ReadAllTextAsync(Path.Combine(skillsDirectory, "index.json"), ct).ConfigureAwait(false);
        }

        var skillDirectory = ResolveSkillDirectory(skillsDirectory, skillName);
        if (string.IsNullOrWhiteSpace(referencePath))
        {
            return await File.ReadAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), ct).ConfigureAwait(false);
        }

        var fullReferencePath = Path.GetFullPath(Path.Combine(skillDirectory, referencePath));
        if (!IsInsideRoot(skillDirectory, fullReferencePath))
        {
            throw new InvalidOperationException("Skill reference path must stay inside the selected skill directory.");
        }

        return await File.ReadAllTextAsync(fullReferencePath, ct).ConfigureAwait(false);
    }

    private static string ResolveSkillDirectory(string skillsDirectory, string skillName)
    {
        var safeName = WorkflowOnboardingPlanner.ToSlug(skillName);
        var directory = Path.Combine(skillsDirectory, safeName);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Skill '{skillName}' was not found.");
        }

        return directory;
    }

    private static bool IsInsideRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SkillCurator
{
    public async Task<SkillCuratorResult> CurateAsync(
        string agentBlazorDirectory,
        DateOnly today,
        CancellationToken ct = default)
    {
        var skillsDirectory = Path.Combine(agentBlazorDirectory, "skills");
        var metadataPath = Path.Combine(skillsDirectory, ".metadata.json");
        if (!File.Exists(metadataPath))
        {
            return new SkillCuratorResult();
        }

        var root = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath, ct).ConfigureAwait(false))?.AsObject()
            ?? new JsonObject();
        var skills = root["skills"]?.AsArray() ?? new JsonArray();
        var markedStale = new List<string>();
        var archived = new List<string>();

        foreach (var node in skills.OfType<JsonObject>())
        {
            var name = node["name"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var pinned = node["pinned"]?.GetValue<bool>() ?? false;
            if (pinned)
            {
                continue;
            }

            var lastActivity = ReadDate(node["lastExecuted"]?.GetValue<string?>()) ??
                ReadDate(node["lastRead"]?.GetValue<string?>()) ??
                ReadDate(node["lastReviewed"]?.GetValue<string?>()) ??
                today;
            var inactiveDays = today.DayNumber - lastActivity.DayNumber;
            var currentState = node["state"]?.GetValue<string>() ?? "active";

            if (inactiveDays >= 90)
            {
                var source = Path.Combine(skillsDirectory, name);
                var archiveRoot = Path.Combine(skillsDirectory, ".archive");
                var target = Path.Combine(archiveRoot, name);
                if (Directory.Exists(source))
                {
                    Directory.CreateDirectory(archiveRoot);
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, recursive: true);
                    }

                    Directory.Move(source, target);
                }

                node["state"] = "archived";
                node["archivedAt"] = $"{today:yyyy-MM-dd}";
                archived.Add(name);
                continue;
            }

            if (inactiveDays >= 30 && !string.Equals(currentState, "stale", StringComparison.OrdinalIgnoreCase))
            {
                node["state"] = "stale";
                markedStale.Add(name);
            }
        }

        await File.WriteAllTextAsync(
            metadataPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            ct).ConfigureAwait(false);

        return new SkillCuratorResult
        {
            MarkedStale = markedStale,
            Archived = archived
        };
    }

    private static DateOnly? ReadDate(string? value)
        => DateOnly.TryParse(value, out var date) ? date : null;
}

public sealed record SkillCuratorResult
{
    public IReadOnlyList<string> MarkedStale { get; init; } = [];

    public IReadOnlyList<string> Archived { get; init; } = [];
}
