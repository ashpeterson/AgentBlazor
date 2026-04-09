using System.Text.Json;
using System.Xml.Linq;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class ExistingAppScaffoldApplier
{
    private const string DefaultAgentBlazorVersion = "1.0.0";
    private const string DefaultMudBlazorVersion = "9.0.0";

    public async Task<ScaffoldPreviewResult> PreviewAsync(
        ScaffoldPlan plan,
        string? agentBlazorSourceRoot = null,
        ScaffoldProvider? provider = null,
        CancellationToken ct = default)
    {
        var changes = new List<ScaffoldPreviewFile>();
        var projectDirectory = Path.GetDirectoryName(plan.HostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the host project directory for '{plan.HostProjectPath}'.");
        var projectName = Path.GetFileNameWithoutExtension(plan.HostProjectPath);
        var rootNamespace = await ResolveRootNamespaceAsync(plan.HostProjectPath, projectName, ct).ConfigureAwait(false);
        var sourceReferences = ResolveSourceReferences(agentBlazorSourceRoot);

        if (ShouldApply(plan, "package-references"))
        {
            await PreviewProjectFileAsync(plan.HostProjectPath, sourceReferences, changes, ct).ConfigureAwait(false);
        }

        if (plan.Readiness.HostShape.Kind == HostShapeKind.Standard)
        {
            var importsPath = ResolveFirstExistingPath(projectDirectory,
                Path.Combine("Components", "_Imports.razor"),
                "_Imports.razor")
                ?? Path.Combine(projectDirectory, "Components", "_Imports.razor");
            await PreviewImportsAsync(importsPath, rootNamespace, changes, ct).ConfigureAwait(false);
        }

        var programPath = ResolveTargetPath(plan, "mud-services")
            ?? ResolveTargetPath(plan, "agentblazor-services")
            ?? ResolveTargetPath(plan, "workflow-registration")
            ?? ResolveTargetPath(plan, "endpoint-mapping")
            ?? Path.Combine(projectDirectory, "Program.cs");
        if (File.Exists(programPath) && ShouldApply(plan, "mud-services", "agentblazor-services", "workflow-registration", "endpoint-mapping"))
        {
            await PreviewProgramAsync(programPath, rootNamespace, provider, changes, ct).ConfigureAwait(false);
        }

        var importsTargetPath = ResolveTargetPath(plan, "ui-imports");
        if (importsTargetPath is not null && ShouldApply(plan, "ui-imports"))
        {
            await PreviewUiImportsAsync(importsTargetPath, changes, ct).ConfigureAwait(false);
        }

        var appShellPath = ResolveTargetPath(plan, "shell-assets");
        if (appShellPath is not null && ShouldApply(plan, "shell-assets"))
        {
            await PreviewAppShellAsync(appShellPath, changes, ct).ConfigureAwait(false);
        }

        var mainLayoutPath = ResolveTargetPath(plan, "mud-providers");
        if (mainLayoutPath is not null && ShouldApply(plan, "mud-providers"))
        {
            await PreviewMainLayoutAsync(mainLayoutPath, changes, ct).ConfigureAwait(false);
        }

        var workflowPath = ResolveTargetPath(plan, "workflow-file") ?? Path.Combine(projectDirectory, "Workflows", "AppCapabilities.cs");
        if (ShouldApply(plan, "workflow-file"))
        {
            await PreviewWorkflowFileAsync(workflowPath, rootNamespace, changes).ConfigureAwait(false);
        }

        var chatPagePath = ResolveTargetPath(plan, "chat-surface") ?? Path.Combine(projectDirectory, "Components", "Pages", "Home.razor");
        if (ShouldApply(plan, "chat-surface"))
        {
            await PreviewChatSurfaceAsync(chatPagePath, changes, ct).ConfigureAwait(false);
        }

        return new ScaffoldPreviewResult { Changes = changes };
    }

    public Task<ScaffoldApplyResult> ApplyAsync(
        ScaffoldPlan plan,
        string? agentBlazorSourceRoot = null,
        ScaffoldProvider? provider = null,
        CancellationToken ct = default)
        => ApplyAsync(plan, preview: null, agentBlazorSourceRoot, provider, ct);

    public async Task<ScaffoldApplyResult> ApplyAsync(
        ScaffoldPlan plan,
        ScaffoldPreviewResult? preview,
        string? agentBlazorSourceRoot = null,
        ScaffoldProvider? provider = null,
        CancellationToken ct = default)
    {
        preview ??= await PreviewAsync(plan, agentBlazorSourceRoot, provider, ct).ConfigureAwait(false);

        foreach (var change in preview.Changes)
        {
            var directory = Path.GetDirectoryName(change.Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(change.Path, change.UpdatedContent, ct).ConfigureAwait(false);
        }

        var manifestPath = await WriteManifestAsync(plan, preview, ct).ConfigureAwait(false);
        return new ScaffoldApplyResult
        {
            Changes = preview.Changes
                .Select(change => new ScaffoldAppliedChange
                {
                    Path = change.Path,
                    ChangeKind = change.ChangeKind,
                    Summary = change.Summary
                })
                .ToArray(),
            ManifestPath = manifestPath
        };
    }

    private static async Task PreviewProjectFileAsync(
        string projectPath,
        IReadOnlyList<string> sourceReferences,
        List<ScaffoldPreviewFile> changes,
        CancellationToken ct)
    {
        var original = await File.ReadAllTextAsync(projectPath, ct).ConfigureAwait(false);
        var document = XDocument.Parse(original);
        var root = document.Root ?? throw new InvalidOperationException($"Could not parse project file '{projectPath}'.");
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Could not determine the project directory for '{projectPath}'.");
        var changed = false;

        if (sourceReferences.Count > 0)
        {
            changed |= EnsureAgentBlazorProjectReferences(root, sourceReferences);
        }
        else
        {
            changed |= EnsurePackageReference(root, "AgentBlazor", DefaultAgentBlazorVersion);
        }

        changed |= EnsurePackageReference(root, "MudBlazor", DefaultMudBlazorVersion);

        if (!changed)
        {
            return;
        }

        AddTextChange(
            changes,
            projectPath,
            sourceReferences.Count > 0
                ? "Added local AgentBlazor project references and any missing MudBlazor package reference."
                : "Added missing AgentBlazor and/or MudBlazor package references.",
            original,
            document.ToString());
    }

    private static bool EnsurePackageReference(XElement root, string packageId, string version)
    {
        var hasReference = root.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Any(element => string.Equals(element.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));

        if (hasReference)
        {
            return false;
        }

        var itemGroup = root.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "ItemGroup" &&
            element.Elements().Any(child => child.Name.LocalName == "PackageReference"));

        if (itemGroup is null)
        {
            itemGroup = new XElement(root.GetDefaultNamespace() + "ItemGroup");
            root.Add(itemGroup);
        }

        var reference = new XElement(root.GetDefaultNamespace() + "PackageReference");
        reference.SetAttributeValue("Include", packageId);
        reference.SetAttributeValue("Version", version);
        itemGroup.Add(reference);
        return true;
    }

    private static bool EnsureAgentBlazorProjectReferences(XElement root, IReadOnlyList<string> sourceReferences)
    {
        var changed = false;
        foreach (var sourceReference in sourceReferences)
        {
            var normalizedPath = Path.GetFullPath(sourceReference).Replace('\\', '/');
            changed |= EnsureProjectReference(root, normalizedPath);
        }

        return changed;
    }

    private static bool EnsureProjectReference(XElement root, string includePath)
    {
        var hasReference = root.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Any(element => string.Equals(
                element.Attribute("Include")?.Value?.Replace('\\', '/'),
                includePath,
                StringComparison.OrdinalIgnoreCase));

        if (hasReference)
        {
            return false;
        }

        var itemGroup = root.Elements().FirstOrDefault(element =>
            element.Name.LocalName == "ItemGroup" &&
            element.Elements().Any(child => child.Name.LocalName == "ProjectReference"));

        if (itemGroup is null)
        {
            itemGroup = new XElement(root.GetDefaultNamespace() + "ItemGroup");
            root.Add(itemGroup);
        }

        var reference = new XElement(root.GetDefaultNamespace() + "ProjectReference");
        reference.SetAttributeValue("Include", includePath);
        itemGroup.Add(reference);
        return true;
    }

    private static async Task PreviewImportsAsync(string importsPath, string rootNamespace, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var exists = File.Exists(importsPath);
        var original = exists ? await File.ReadAllTextAsync(importsPath, ct).ConfigureAwait(false) : string.Empty;
        var updated = original;

        updated = EnsureLine(updated, "@using AgentBlazor");
        updated = EnsureLine(updated, "@using AgentBlazor.App");
        updated = EnsureLine(updated, "@using AgentBlazor.Attributes");
        updated = EnsureLine(updated, "@using AgentBlazor.Components");
        updated = EnsureLine(updated, $"@using {rootNamespace}.Workflows");
        updated = EnsureLine(updated, "@using MudBlazor");

        AddTextChange(
            changes,
            importsPath,
            exists
                ? "Added AgentBlazor and MudBlazor imports."
                : "Created _Imports.razor with AgentBlazor and MudBlazor imports.",
            original,
            updated);
    }

    private static async Task PreviewProgramAsync(
        string programPath,
        string rootNamespace,
        ScaffoldProvider? provider,
        List<ScaffoldPreviewFile> changes,
        CancellationToken ct)
    {
        var original = await File.ReadAllTextAsync(programPath, ct).ConfigureAwait(false);
        var updated = original;

        updated = EnsureUsing(updated, "AgentBlazor");
        updated = EnsureUsing(updated, "MudBlazor.Services");
        updated = EnsureUsing(updated, $"{rootNamespace}.Workflows");

        if (!updated.Contains("builder.Services.AddMudServices();", StringComparison.Ordinal))
        {
            updated = InsertAfterFirstStatementContainingAny(
                updated,
                "builder.Services.AddMudServices();\n",
                "builder.Services.AddRazorComponents(",
                "builder.Services.AddRazorPages(",
                "builder.Services.AddServerSideBlazor(",
                "var builder = WebApplication.CreateBuilder(args);");
        }

        if (!updated.Contains("builder.Services.AddAgentBlazor(", StringComparison.Ordinal))
        {
            var providerBlock = BuildProviderBlock(provider);
            var agentBlazorBlock = $$"""

builder.Services.AddAgentBlazor(options =>
{
{{providerBlock}}

    if (builder.Environment.IsDevelopment())
    {
        options.UseDevTools();
    }

    options.ConfigureBuilder(agentBuilder =>
    {
        agentBuilder.AddWorkflow<AppCapabilities>("assistant", agent =>
        {
            agent.WithDescription("Help users understand and complete tasks in this application.");
            agent.WithRoutePrefixes("/");
        });
    });
});
""";
            updated = InsertAfterFirstStatementContainingAny(
                updated,
                agentBlazorBlock.TrimStart('\n') + "\n",
                "builder.Services.AddMudServices();",
                "builder.Services.AddRazorComponents(",
                "builder.Services.AddRazorPages(",
                "builder.Services.AddServerSideBlazor(");
        }

        if (!updated.Contains("app.MapAgentBlazorEndpoints();", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirstContainingAny(
                updated,
                "app.MapAgentBlazorEndpoints();\n",
                "app.MapFallbackToFile(\"index.html\")",
                "app.MapFallbackToPage(\"/_Host\")",
                "app.Run();");
        }

        AddTextChange(
            changes,
            programPath,
            provider is null
                ? "Added AgentBlazor startup wiring to Program.cs with provider guidance."
                : $"Added AgentBlazor startup wiring to Program.cs with {provider.Value.ToDisplayName()} configuration.",
            original,
            updated);
    }

    private static string BuildProviderBlock(ScaffoldProvider? provider)
        => provider switch
        {
            ScaffoldProvider.OpenAI => """
    options.UseOpenAI(
        apiKey: builder.Configuration["OpenAI:ApiKey"]!,
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-5.4-mini");
""",
            ScaffoldProvider.AzureOpenAI => """
    options.UseAzureOpenAI(
        endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
        deploymentName: builder.Configuration["AzureOpenAI:DeploymentName"]!,
        apiKey: builder.Configuration["AzureOpenAI:ApiKey"]);
""",
            ScaffoldProvider.Ollama => """
    options.UseOllama(
        model: builder.Configuration["Ollama:Model"] ?? "llama3.2",
        endpoint: builder.Configuration["Ollama:Endpoint"] ?? "http://127.0.0.1:11434/v1",
        apiKey: builder.Configuration["Ollama:ApiKey"]);
""",
            null => """
    // Recommended first path:
    // options.UseOpenAI(
    //     apiKey: builder.Configuration["OpenAI:ApiKey"]!,
    //     model: builder.Configuration["OpenAI:Model"] ?? "gpt-5.4-mini");
    //
    // Alternatives:
    // options.UseAzureOpenAI(
    //     endpoint: builder.Configuration["AzureOpenAI:Endpoint"]!,
    //     deploymentName: builder.Configuration["AzureOpenAI:DeploymentName"]!,
    //     apiKey: builder.Configuration["AzureOpenAI:ApiKey"]);
    // options.UseOllama(
    //     model: builder.Configuration["Ollama:Model"] ?? "llama3.2",
    //     endpoint: builder.Configuration["Ollama:Endpoint"] ?? "http://127.0.0.1:11434/v1",
    //     apiKey: builder.Configuration["Ollama:ApiKey"]);
""",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    private static async Task PreviewAppShellAsync(string appShellPath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var original = await File.ReadAllTextAsync(appShellPath, ct).ConfigureAwait(false);
        var updated = original;
        var isStaticHtmlShell = Path.GetExtension(appShellPath).Equals(".html", StringComparison.OrdinalIgnoreCase);

        if (!updated.Contains("_content/MudBlazor/MudBlazor.min.css", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</head>",
                isStaticHtmlShell
                    ? "    <link rel=\"stylesheet\" href=\"_content/MudBlazor/MudBlazor.min.css\" />\n"
                    : "    <link rel=\"stylesheet\" href=\"@Assets[\"_content/MudBlazor/MudBlazor.min.css\"]\" />\n");
        }

        if (!updated.Contains("AgentBlazorAssetPaths.Css", StringComparison.Ordinal) &&
            !updated.Contains("_content/AgentBlazor/AgentBlazor.min.css", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</head>",
                isStaticHtmlShell
                    ? "    <link rel=\"stylesheet\" href=\"_content/AgentBlazor/AgentBlazor.min.css\" />\n"
                    : "    <link rel=\"stylesheet\" href=\"@Assets[AgentBlazorAssetPaths.Css]\" />\n");
        }

        if (!updated.Contains("_content/MudBlazor/MudBlazor.min.js", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</body>",
                isStaticHtmlShell
                    ? "    <script src=\"_content/MudBlazor/MudBlazor.min.js\"></script>\n"
                    : "    <script src=\"@Assets[\"_content/MudBlazor/MudBlazor.min.js\"]\"></script>\n");
        }

        if (!updated.Contains("AgentBlazorAssetPaths.Js", StringComparison.Ordinal) &&
            !updated.Contains("_content/AgentBlazor/AgentBlazor.min.js", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</body>",
                isStaticHtmlShell
                    ? "    <script src=\"_content/AgentBlazor/AgentBlazor.min.js\"></script>\n"
                    : "    <script src=\"@Assets[AgentBlazorAssetPaths.Js]\"></script>\n");
        }

        AddTextChange(
            changes,
            appShellPath,
            "Added MudBlazor and AgentBlazor shell asset references.",
            original,
            updated);
    }

    private static async Task PreviewUiImportsAsync(string importsPath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var exists = File.Exists(importsPath);
        var original = exists ? await File.ReadAllTextAsync(importsPath, ct).ConfigureAwait(false) : string.Empty;
        var updated = original;

        updated = EnsureLine(updated, "@using AgentBlazor.Components");
        updated = EnsureLine(updated, "@using MudBlazor");

        AddTextChange(
            changes,
            importsPath,
            exists
                ? "Added AgentBlazor and MudBlazor UI imports."
                : "Created _Imports.razor with AgentBlazor and MudBlazor UI imports.",
            original,
            updated);
    }

    private static async Task PreviewMainLayoutAsync(string mainLayoutPath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var original = await File.ReadAllTextAsync(mainLayoutPath, ct).ConfigureAwait(false);
        var updated = original;

        updated = EnsureMarkupLine(updated, "<MudThemeProvider />");
        updated = EnsureMarkupLine(updated, "<MudPopoverProvider />");
        updated = EnsureMarkupLine(updated, "<MudDialogProvider />");
        updated = EnsureMarkupLine(updated, "<MudSnackbarProvider />");

        AddTextChange(
            changes,
            mainLayoutPath,
            "Added the standard MudBlazor providers to the main layout.",
            original,
            updated);
    }

    private static Task PreviewWorkflowFileAsync(string workflowPath, string rootNamespace, List<ScaffoldPreviewFile> changes)
    {
        if (File.Exists(workflowPath))
        {
            return Task.CompletedTask;
        }

        var content = $$"""
using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace {{rootNamespace}}.Workflows;

[AgentCapability("assistant", Name = "Assistant", Description = "Starter AgentBlazor workflow for this application.")]
public sealed class AppCapabilities
{
    [AgentAction("Summarize what the user can do in this app")]
    public CapabilityResult SummarizeApp()
        => CapabilityResult.Success("AgentBlazor is installed. Replace this starter workflow with app-specific actions.");

    [AgentAction("Suggest the next integration step")]
    public CapabilityResult SuggestNextStep()
        => CapabilityResult.Success("Connect a model provider in Program.cs, then add domain-specific [AgentAction] methods.");
}
""";
        AddTextChange(
            changes,
            workflowPath,
            "Created the starter AppCapabilities workflow.",
            string.Empty,
            content);
        return Task.CompletedTask;
    }

    private static async Task PreviewChatSurfaceAsync(string chatPagePath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        if (!File.Exists(chatPagePath))
        {
            var content = """
@page "/"

<h1>Home</h1>

<AgentChatWidget Title="Assistant" />
""";
            AddTextChange(
                changes,
                chatPagePath,
                "Created a starter page with AgentChatWidget.",
                string.Empty,
                content);
            return;
        }

        var original = await File.ReadAllTextAsync(chatPagePath, ct).ConfigureAwait(false);
        if (original.Contains("<AgentChatWidget", StringComparison.Ordinal) ||
            original.Contains("<AgentChatSurface", StringComparison.Ordinal) ||
            original.Contains("<AgentChatPanel", StringComparison.Ordinal))
        {
            return;
        }

        var updated = original.TrimEnd() + "\n\n<AgentChatWidget Title=\"Assistant\" />\n";
        AddTextChange(
            changes,
            chatPagePath,
            "Mounted AgentChatWidget on the default page.",
            original,
            updated);
    }

    private static void AddTextChange(
        List<ScaffoldPreviewFile> changes,
        string path,
        string summary,
        string original,
        string updated)
    {
        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add(new ScaffoldPreviewFile
        {
            Path = path,
            ChangeKind = string.IsNullOrEmpty(original) && !File.Exists(path)
                ? ScaffoldPreviewChangeKind.Create
                : ScaffoldPreviewChangeKind.Update,
            Summary = summary,
            OriginalContent = original,
            UpdatedContent = updated
        });
    }

    private static async Task<string> WriteManifestAsync(
        ScaffoldPlan plan,
        ScaffoldPreviewResult preview,
        CancellationToken ct)
    {
        var projectDirectory = Path.GetDirectoryName(plan.HostProjectPath)
            ?? throw new InvalidOperationException($"Could not determine the host project directory for '{plan.HostProjectPath}'.");
        var manifestDirectory = Path.Combine(projectDirectory, ".agentblazor");
        Directory.CreateDirectory(manifestDirectory);

        var manifestPath = Path.Combine(manifestDirectory, "scaffold-manifest.json");
        var manifest = new ScaffoldManifest
        {
            AppliedAtUtc = DateTime.UtcNow,
            InputPath = plan.InputPath,
            HostProjectName = plan.HostProjectName,
            HostProjectPath = plan.HostProjectPath,
            ChangedFiles = preview.Changes.Select(change => new ScaffoldManifestFile
            {
                Path = change.Path,
                RelativePath = Path.GetRelativePath(projectDirectory, change.Path),
                ChangeKind = change.ChangeKind.ToString(),
                Summary = change.Summary
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        await File.WriteAllTextAsync(manifestPath, json, ct).ConfigureAwait(false);
        return manifestPath;
    }

    private static string EnsureUsing(string content, string namespaceName)
    {
        var usingLine = $"using {namespaceName};";
        if (content.Contains(usingLine, StringComparison.Ordinal))
        {
            return content;
        }

        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var insertIndex = 0;
        while (insertIndex < lines.Count && lines[insertIndex].StartsWith("using ", StringComparison.Ordinal))
        {
            insertIndex++;
        }

        lines.Insert(insertIndex, usingLine);
        return string.Join('\n', lines);
    }

    private static string EnsureLine(string content, string line)
    {
        if (content.Contains(line, StringComparison.Ordinal))
        {
            return content;
        }

        var trimmed = content.TrimEnd();
        return string.IsNullOrWhiteSpace(trimmed)
            ? line + "\n"
            : trimmed + "\n" + line + "\n";
    }

    private static string EnsureMarkupLine(string content, string line)
    {
        if (content.Contains(line, StringComparison.Ordinal))
        {
            return content;
        }

        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n').ToList();
        var insertIndex = 0;

        while (insertIndex < lines.Count && lines[insertIndex].StartsWith("@", StringComparison.Ordinal))
        {
            insertIndex++;
        }

        lines.Insert(insertIndex, line);
        return string.Join('\n', lines);
    }

    private static string InsertAfterStatementContaining(string content, string marker, string block)
    {
        var index = content.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return content;
        }

        var statementEnd = content.IndexOf(';', index);
        if (statementEnd < 0)
        {
            return content;
        }

        return content.Insert(statementEnd + 1, "\n" + block);
    }

    private static string InsertAfterFirstStatementContainingAny(string content, string block, params string[] markers)
    {
        if (markers.Length == 0)
        {
            return content;
        }

        foreach (var marker in markers)
        {
            var updated = InsertAfterStatementContaining(content, marker, block);
            if (!string.Equals(updated, content, StringComparison.Ordinal))
            {
                return updated;
            }
        }

        return content;
    }

    private static string InsertBeforeFirst(string content, string marker, string block)
    {
        var index = content.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return content;
        }

        return content.Insert(index, block);
    }

    private static string InsertBeforeFirstContainingAny(string content, string block, params string[] markers)
    {
        if (markers.Length == 0)
        {
            return content;
        }

        foreach (var marker in markers)
        {
            var updated = InsertBeforeFirst(content, marker, block);
            if (!string.Equals(updated, content, StringComparison.Ordinal))
            {
                return updated;
            }
        }

        return content;
    }

    private static string? ResolveFirstExistingPath(string projectDirectory, params string[] relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var candidate = Path.Combine(projectDirectory, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveTargetPath(ScaffoldPlan plan, string itemId)
        => plan.Items.FirstOrDefault(item => item.Id == itemId)?.TargetPath;

    private static async Task<string> ResolveRootNamespaceAsync(string projectPath, string fallbackProjectName, CancellationToken ct)
    {
        var csprojText = await File.ReadAllTextAsync(projectPath, ct).ConfigureAwait(false);
        try
        {
            var document = XDocument.Parse(csprojText);
            var rootNamespace = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "RootNamespace")
                ?.Value;

            return string.IsNullOrWhiteSpace(rootNamespace) ? fallbackProjectName : rootNamespace;
        }
        catch
        {
            return fallbackProjectName;
        }
    }

    private static IReadOnlyList<string> ResolveSourceReferences(string? agentBlazorSourceRoot)
    {
        if (string.IsNullOrWhiteSpace(agentBlazorSourceRoot))
        {
            return [];
        }

        var normalizedRoot = Path.GetFullPath(agentBlazorSourceRoot);
        var requiredProjects = new[]
        {
            Path.Combine(normalizedRoot, "src", "AgentBlazor.Core", "AgentBlazor.Core.csproj"),
            Path.Combine(normalizedRoot, "src", "AgentBlazor.Hosting", "AgentBlazor.Hosting.csproj"),
            Path.Combine(normalizedRoot, "src", "AgentBlazor.Components", "AgentBlazor.Components.csproj")
        };

        if (requiredProjects.All(File.Exists))
        {
            return requiredProjects;
        }

        throw new InvalidOperationException(
            $"Could not find the AgentBlazor source projects under '{normalizedRoot}'. Expected AgentBlazor.Core, AgentBlazor.Hosting, and AgentBlazor.Components.");
    }

    private static bool ShouldApply(ScaffoldPlan plan, params string[] itemIds)
        => plan.Items.Any(item =>
            itemIds.Contains(item.Id, StringComparer.Ordinal) &&
            item.Action != ScaffoldPlanAction.ManualReview);

    private sealed record ScaffoldManifest
    {
        public DateTime AppliedAtUtc { get; init; }

        public string InputPath { get; init; } = string.Empty;

        public string HostProjectName { get; init; } = string.Empty;

        public string HostProjectPath { get; init; } = string.Empty;

        public IReadOnlyList<ScaffoldManifestFile> ChangedFiles { get; init; } = [];
    }

    private sealed record ScaffoldManifestFile
    {
        public string Path { get; init; } = string.Empty;

        public string RelativePath { get; init; } = string.Empty;

        public string ChangeKind { get; init; } = string.Empty;

        public string Summary { get; init; } = string.Empty;
    }
}
