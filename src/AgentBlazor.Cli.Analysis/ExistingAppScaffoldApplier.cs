using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public sealed class ExistingAppScaffoldApplier
{
    private const string DefaultMudBlazorVersion = "9.0.0";
    private const string DefaultChatWidgetMarkup = """<AgentChatWidget @rendermode="InteractiveServer" Title="Assistant" />""";

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
        var hostSourceReferences = ResolveHostSourceReferences(agentBlazorSourceRoot);
        var uiSourceReferences = ResolveUiSourceReferences(agentBlazorSourceRoot);

        if (ShouldApply(plan, "package-references"))
        {
            await PreviewProjectFileAsync(plan.HostProjectPath, hostSourceReferences, changes, ct).ConfigureAwait(false);
        }

        if (ShouldApply(plan, "ui-package-references") &&
            !string.IsNullOrWhiteSpace(plan.Readiness.UiProjectPath) &&
            !string.Equals(plan.Readiness.UiProjectPath, plan.HostProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            await PreviewProjectFileAsync(plan.Readiness.UiProjectPath!, uiSourceReferences, changes, ct).ConfigureAwait(false);
        }

        if (plan.Readiness.HostShape.Kind == HostShapeKind.Standard)
        {
            var importsPath = ResolveFirstExistingPath(projectDirectory,
                Path.Combine("Components", "_Imports.razor"),
                "_Imports.razor")
                ?? Path.Combine(projectDirectory, "Components", "_Imports.razor");
            await PreviewImportsAsync(importsPath, changes, ct).ConfigureAwait(false);
        }

        var programPath = ResolveTargetPath(plan, "mud-services")
            ?? ResolveTargetPath(plan, "agentblazor-services")
            ?? ResolveTargetPath(plan, "workflow-registration")
            ?? ResolveTargetPath(plan, "endpoint-mapping")
            ?? Path.Combine(projectDirectory, "Program.cs");
        if (File.Exists(programPath) && ShouldApply(plan, "mud-services", "agentblazor-services", "workflow-registration", "endpoint-mapping"))
        {
            await PreviewProgramAsync(
                programPath,
                rootNamespace,
                provider,
                patchMudServices: ShouldApply(plan, "mud-services"),
                patchAgentBlazorRegistration: ShouldApply(plan, "agentblazor-services", "workflow-registration"),
                patchEndpointMapping: ShouldApply(plan, "endpoint-mapping"),
                changes,
                ct).ConfigureAwait(false);
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
        var updated = original;
        var centralPackageFile = await ResolveCentralPackageFileAsync(projectPath, ct).ConfigureAwait(false);
        var centralPackageVersions = new List<(string PackageId, string Version)>();

        if (sourceReferences.Count > 0)
        {
            foreach (var sourceReference in sourceReferences)
            {
                var normalizedPath = Path.GetFullPath(sourceReference).Replace('\\', '/');
                updated = EnsureProjectReference(updated, normalizedPath);
            }
        }
        else
        {
            updated = EnsurePackageReference(
                updated,
                "AgentBlazor",
                ResolveDefaultAgentBlazorVersion(),
                centralPackageFile is not null,
                centralPackageVersions);
        }

        updated = EnsurePackageReference(
            updated,
            "MudBlazor",
            DefaultMudBlazorVersion,
            centralPackageFile is not null,
            centralPackageVersions);

        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            await PreviewCentralPackageVersionsAsync(centralPackageFile, centralPackageVersions, changes, ct).ConfigureAwait(false);
            return;
        }

        AddTextChange(
            changes,
            projectPath,
            sourceReferences.Count > 0
                ? "Added local AgentBlazor project references and any missing MudBlazor package reference."
                : "Added missing AgentBlazor and/or MudBlazor package references.",
            original,
            updated);

        await PreviewCentralPackageVersionsAsync(centralPackageFile, centralPackageVersions, changes, ct).ConfigureAwait(false);
    }

    private static string EnsurePackageReference(
        string content,
        string packageId,
        string version,
        bool useCentralPackageManagement = false,
        List<(string PackageId, string Version)>? centralPackageVersions = null)
    {
        if (HasXmlReference(content, "PackageReference", packageId, normalizePath: false))
        {
            return content;
        }

        if (useCentralPackageManagement)
        {
            centralPackageVersions?.Add((packageId, version));
        }

        return InsertXmlReference(
            content,
            "PackageReference",
            useCentralPackageManagement
                ? $"<PackageReference Include=\"{EscapeXmlAttribute(packageId)}\" />"
                : $"<PackageReference Include=\"{EscapeXmlAttribute(packageId)}\" Version=\"{EscapeXmlAttribute(version)}\" />");
    }

    private static async Task PreviewCentralPackageVersionsAsync(
        CentralPackageFile? centralPackageFile,
        IReadOnlyList<(string PackageId, string Version)> packageVersions,
        List<ScaffoldPreviewFile> changes,
        CancellationToken ct)
    {
        if (centralPackageFile is null || packageVersions.Count == 0)
        {
            return;
        }

        var updated = centralPackageFile.Content;
        foreach (var (packageId, version) in packageVersions)
        {
            updated = EnsurePackageVersion(updated, packageId, version);
        }

        if (string.Equals(centralPackageFile.Content, updated, StringComparison.Ordinal))
        {
            return;
        }

        AddTextChange(
            changes,
            centralPackageFile.Path,
            "Added missing AgentBlazor and/or MudBlazor central package versions.",
            centralPackageFile.Content,
            updated);
    }

    private static string EnsurePackageVersion(string content, string packageId, string version)
    {
        if (HasXmlReference(content, "PackageVersion", packageId, normalizePath: false))
        {
            return content;
        }

        return InsertXmlReference(
            content,
            "PackageVersion",
            $"<PackageVersion Include=\"{EscapeXmlAttribute(packageId)}\" Version=\"{EscapeXmlAttribute(version)}\" />");
    }

    private static async Task<CentralPackageFile?> ResolveCentralPackageFileAsync(string projectPath, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(projectPath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var centralPackagePath = Path.Combine(directory, "Directory.Packages.props");
            if (File.Exists(centralPackagePath))
            {
                var content = await File.ReadAllTextAsync(centralPackagePath, ct).ConfigureAwait(false);
                return IsCentralPackageManagementEnabled(content)
                    ? new CentralPackageFile(centralPackagePath, content)
                    : null;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }

    private static bool IsCentralPackageManagementEnabled(string content)
    {
        try
        {
            var document = XDocument.Parse(content);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "ManagePackageVersionsCentrally")
                .Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return content.Contains("<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ResolveDefaultAgentBlazorVersion()
    {
        var version = typeof(ExistingAppScaffoldApplier).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(ExistingAppScaffoldApplier).Assembly.GetName().Version?.ToString()
            ?? "0.1.0-preview.10";

        return version.Split('+', 2)[0];
    }

    private static string EnsureProjectReference(string content, string includePath)
    {
        if (HasXmlReference(content, "ProjectReference", includePath, normalizePath: true))
        {
            return content;
        }

        return InsertXmlReference(
            content,
            "ProjectReference",
            $"<ProjectReference Include=\"{EscapeXmlAttribute(includePath)}\" />");
    }

    private static bool HasXmlReference(string content, string elementName, string includeValue, bool normalizePath)
    {
        try
        {
            var document = XDocument.Parse(content);
            return document.Descendants()
                .Where(element => element.Name.LocalName == elementName)
                .Any(element =>
                {
                    var include = element.Attribute("Include")?.Value ?? string.Empty;
                    if (normalizePath)
                    {
                        include = include.Replace('\\', '/');
                    }

                    return string.Equals(include, includeValue, StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            return content.Contains($"<{elementName}", StringComparison.OrdinalIgnoreCase) &&
                content.Contains($"Include=\"{includeValue}\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string InsertXmlReference(string content, string elementName, string referenceLine)
    {
        var newLine = DetectNewLine(content);
        var searchIndex = 0;

        while (true)
        {
            var itemGroupStart = content.IndexOf("<ItemGroup", searchIndex, StringComparison.Ordinal);
            if (itemGroupStart < 0)
            {
                break;
            }

            var itemGroupClose = content.IndexOf("</ItemGroup>", itemGroupStart, StringComparison.Ordinal);
            if (itemGroupClose < 0)
            {
                break;
            }

            var itemGroup = content[itemGroupStart..itemGroupClose];
            if (itemGroup.Contains($"<{elementName}", StringComparison.Ordinal))
            {
                var closeLineStart = content.LastIndexOf('\n', itemGroupClose);
                closeLineStart = closeLineStart < 0 ? itemGroupClose : closeLineStart + 1;
                var closeIndent = GetLineIndent(content, closeLineStart);
                var childIndent = InferChildIndent(itemGroup, elementName, closeIndent + "  ");
                return content.Insert(closeLineStart, $"{childIndent}{referenceLine}{newLine}");
            }

            searchIndex = itemGroupClose + "</ItemGroup>".Length;
        }

        var projectClose = content.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (projectClose < 0)
        {
            return EnsureLine(content, referenceLine);
        }

        var closeLineStartIndex = content.LastIndexOf('\n', projectClose);
        closeLineStartIndex = closeLineStartIndex < 0 ? projectClose : closeLineStartIndex + 1;
        var projectCloseIndent = GetLineIndent(content, closeLineStartIndex);
        var itemGroupIndent = projectCloseIndent + "  ";
        var childIndentForNewGroup = itemGroupIndent + "  ";
        var block = $"{itemGroupIndent}<ItemGroup>{newLine}{childIndentForNewGroup}{referenceLine}{newLine}{itemGroupIndent}</ItemGroup>{newLine}";
        return content.Insert(closeLineStartIndex, block);
    }

    private static string DetectNewLine(string content)
        => content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string GetLineIndent(string content, int lineStart)
    {
        var index = lineStart;
        while (index < content.Length && content[index] is ' ' or '\t')
        {
            index++;
        }

        return content[lineStart..index];
    }

    private static string InferChildIndent(string itemGroup, string elementName, string fallback)
    {
        var elementIndex = itemGroup.IndexOf($"<{elementName}", StringComparison.Ordinal);
        if (elementIndex < 0)
        {
            return fallback;
        }

        var lineStart = itemGroup.LastIndexOf('\n', elementIndex);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        return GetLineIndent(itemGroup, lineStart);
    }

    private static string EscapeXmlAttribute(string value)
        => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static async Task PreviewImportsAsync(string importsPath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var exists = File.Exists(importsPath);
        var original = exists ? await File.ReadAllTextAsync(importsPath, ct).ConfigureAwait(false) : string.Empty;
        var updated = original;

        updated = EnsureLine(updated, "@using static Microsoft.AspNetCore.Components.Web.RenderMode");
        updated = EnsureLine(updated, "@using AgentBlazor.Components");

        AddTextChange(
            changes,
            importsPath,
            exists
                ? "Added AgentBlazor component imports."
                : "Created _Imports.razor with AgentBlazor component imports.",
            original,
            updated);
    }

    private static async Task PreviewProgramAsync(
        string programPath,
        string rootNamespace,
        ScaffoldProvider? provider,
        bool patchMudServices,
        bool patchAgentBlazorRegistration,
        bool patchEndpointMapping,
        List<ScaffoldPreviewFile> changes,
        CancellationToken ct)
    {
        var original = await File.ReadAllTextAsync(programPath, ct).ConfigureAwait(false);
        var updated = original;

        if (patchAgentBlazorRegistration || patchEndpointMapping)
        {
            updated = EnsureUsing(updated, "AgentBlazor");
        }

        if (patchMudServices)
        {
            updated = EnsureUsing(updated, "MudBlazor.Services");
        }

        if (patchAgentBlazorRegistration)
        {
            updated = EnsureUsing(updated, $"{rootNamespace}.Workflows");
        }

        if (patchMudServices && !updated.Contains("AddMudServices(", StringComparison.Ordinal))
        {
            updated = InsertAfterFirstStatementContainingAny(
                updated,
                "builder.Services.AddMudServices();\n",
                "builder.Services.AddRazorComponents(",
                "builder.Services.AddRazorPages(",
                "builder.Services.AddServerSideBlazor(",
                "var builder = WebApplication.CreateBuilder(args);");
        }

        if (patchAgentBlazorRegistration && !updated.Contains("AddAgentBlazor(", StringComparison.Ordinal))
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
                "builder.Services.AddServerSideBlazor(",
                ".AddServerUI(",
                ".AddInfrastructure(",
                ".AddApplication(",
                "var builder = WebApplication.CreateBuilder(args);");
        }

        if (patchEndpointMapping && !updated.Contains("MapAgentBlazorEndpoints(", StringComparison.Ordinal))
        {
            updated = InsertBeforeLineContainingAny(
                updated,
                "app.MapAgentBlazorEndpoints();\n",
                "app.MapFallbackToFile(\"index.html\")",
                "app.MapFallbackToPage(\"/_Host\")",
                "await app.RunAsync(",
                "app.RunAsync(",
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
        model: builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini");
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
    //     model: builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini");
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
        var linkNonceAttribute = FindExistingNonceAttribute(original, "link");
        var scriptNonceAttribute = FindExistingNonceAttribute(original, "script");

        if (!updated.Contains("_content/MudBlazor/MudBlazor.min.css", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</head>",
                isStaticHtmlShell
                    ? BuildStylesheetTag("_content/MudBlazor/MudBlazor.min.css", linkNonceAttribute)
                    : BuildStylesheetTag("@Assets[\"_content/MudBlazor/MudBlazor.min.css\"]", linkNonceAttribute));
        }

        if (!updated.Contains("AgentBlazorAssetPaths.Css", StringComparison.Ordinal) &&
            !updated.Contains("_content/AgentBlazor/AgentBlazor.min.css", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</head>",
                isStaticHtmlShell
                    ? BuildStylesheetTag("_content/AgentBlazor/AgentBlazor.min.css", linkNonceAttribute)
                    : BuildStylesheetTag("@Assets[AgentBlazorAssetPaths.Css]", linkNonceAttribute));
        }

        if (!updated.Contains("_content/MudBlazor/MudBlazor.min.js", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</body>",
                isStaticHtmlShell
                    ? BuildScriptTag("_content/MudBlazor/MudBlazor.min.js", scriptNonceAttribute)
                    : BuildScriptTag("@Assets[\"_content/MudBlazor/MudBlazor.min.js\"]", scriptNonceAttribute));
        }

        if (!updated.Contains("AgentBlazorAssetPaths.Js", StringComparison.Ordinal) &&
            !updated.Contains("_content/AgentBlazor/AgentBlazor.min.js", StringComparison.Ordinal))
        {
            updated = InsertBeforeFirst(
                updated,
                "</body>",
                isStaticHtmlShell
                    ? BuildScriptTag("_content/AgentBlazor/AgentBlazor.min.js", scriptNonceAttribute)
                    : BuildScriptTag("@Assets[AgentBlazorAssetPaths.Js]", scriptNonceAttribute));
        }

        AddTextChange(
            changes,
            appShellPath,
            "Added MudBlazor and AgentBlazor shell asset references.",
            original,
            updated);
    }

    private static string BuildStylesheetTag(string href, string? nonceAttribute)
        => $"    <link rel=\"stylesheet\" href=\"{href}\"{nonceAttribute ?? string.Empty} />\n";

    private static string BuildScriptTag(string src, string? nonceAttribute)
        => $"    <script src=\"{src}\"{nonceAttribute ?? string.Empty}></script>\n";

    private static string? FindExistingNonceAttribute(string markup, string tagName)
    {
        var search = $"<{tagName}";
        var index = 0;

        while (index < markup.Length)
        {
            var tagStart = markup.IndexOf(search, index, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0)
            {
                return null;
            }

            var tagEnd = markup.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                return null;
            }

            var tag = markup.Substring(tagStart, tagEnd - tagStart);
            var nonceIndex = FindAttributeNameIndex(tag, "nonce");
            if (nonceIndex >= 0)
            {
                return ExtractAttribute(tag, nonceIndex);
            }

            index = tagEnd + 1;
        }

        return null;
    }

    private static int FindAttributeNameIndex(string tag, string attributeName)
    {
        var search = $"{attributeName}=";
        var index = 0;

        while (index < tag.Length)
        {
            var candidate = tag.IndexOf(search, index, StringComparison.OrdinalIgnoreCase);
            if (candidate < 0)
            {
                return -1;
            }

            if (candidate == 0 || char.IsWhiteSpace(tag[candidate - 1]) || tag[candidate - 1] == '<')
            {
                return candidate;
            }

            index = candidate + search.Length;
        }

        return -1;
    }

    private static string? ExtractAttribute(string tag, int attributeNameIndex)
    {
        var attributeStart = attributeNameIndex > 0 && char.IsWhiteSpace(tag[attributeNameIndex - 1])
            ? attributeNameIndex - 1
            : attributeNameIndex;
        var valueStart = attributeNameIndex + "nonce=".Length;
        if (valueStart >= tag.Length)
        {
            return null;
        }

        int attributeEnd;
        var quote = tag[valueStart];
        if (quote is '"' or '\'')
        {
            var valueEnd = tag.IndexOf(quote, valueStart + 1);
            if (valueEnd < 0)
            {
                return null;
            }

            attributeEnd = valueEnd + 1;
        }
        else
        {
            attributeEnd = valueStart;
            while (attributeEnd < tag.Length &&
                   !char.IsWhiteSpace(tag[attributeEnd]) &&
                   tag[attributeEnd] is not '>')
            {
                attributeEnd++;
            }
        }

        return tag.Substring(attributeStart, attributeEnd - attributeStart);
    }

    private static async Task PreviewUiImportsAsync(string importsPath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var exists = File.Exists(importsPath);
        var original = exists ? await File.ReadAllTextAsync(importsPath, ct).ConfigureAwait(false) : string.Empty;
        var updated = original;

        updated = EnsureLine(updated, "@using AgentBlazor.Components");

        AddTextChange(
            changes,
            importsPath,
            exists
                ? "Added AgentBlazor component UI imports."
                : "Created _Imports.razor with AgentBlazor component UI imports.",
            original,
            updated);
    }

    private static async Task PreviewMainLayoutAsync(string mainLayoutPath, List<ScaffoldPreviewFile> changes, CancellationToken ct)
    {
        var original = await File.ReadAllTextAsync(mainLayoutPath, ct).ConfigureAwait(false);
        var updated = original;

        updated = EnsureMarkupLine(updated, "@using MudBlazor");
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
        var existingChangeIndex = changes.FindIndex(change =>
            string.Equals(change.Path, chatPagePath, StringComparison.OrdinalIgnoreCase));

        if (!File.Exists(chatPagePath))
        {
            var content = """
@page "/"

<h1>Home</h1>

<AgentChatWidget @rendermode="InteractiveServer" Title="Assistant" />
""";
            AddTextChange(
                changes,
                chatPagePath,
                "Created a starter page with AgentChatWidget.",
                string.Empty,
                content);
            return;
        }

        var original = existingChangeIndex >= 0
            ? changes[existingChangeIndex].UpdatedContent
            : await File.ReadAllTextAsync(chatPagePath, ct).ConfigureAwait(false);
        if (original.Contains("<AgentChatWidget", StringComparison.Ordinal) ||
            original.Contains("<AgentChatSurface", StringComparison.Ordinal) ||
            original.Contains("<AgentChatPanel", StringComparison.Ordinal))
        {
            return;
        }

        var updated = InsertChatWidgetMarkup(original);
        if (existingChangeIndex >= 0)
        {
            var existingChange = changes[existingChangeIndex];
            changes[existingChangeIndex] = existingChange with
            {
                Summary = $"{existingChange.Summary} Mounted AgentChatWidget on the default layout or page.",
                UpdatedContent = updated
            };
            return;
        }

        AddTextChange(
            changes,
            chatPagePath,
            "Mounted AgentChatWidget on the default layout or page.",
            original,
            updated);
    }

    private static string InsertChatWidgetMarkup(string content)
    {
        var newLine = DetectNewLine(content);
        var bodyIndex = content.IndexOf("@Body", StringComparison.Ordinal);
        if (bodyIndex >= 0)
        {
            var lineStart = content.LastIndexOf('\n', bodyIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var lineEnd = content.IndexOf('\n', bodyIndex);
            lineEnd = lineEnd < 0 ? content.Length : lineEnd + 1;
            var indent = GetLineIndent(content, lineStart);
            return content.Insert(lineEnd, $"{indent}{DefaultChatWidgetMarkup}{newLine}");
        }

        var codeIndex = content.LastIndexOf("@code", StringComparison.Ordinal);
        if (codeIndex < 0)
        {
            codeIndex = content.LastIndexOf("@functions", StringComparison.Ordinal);
        }

        if (codeIndex >= 0)
        {
            var lineStart = content.LastIndexOf('\n', codeIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            return content.Insert(lineStart, $"{DefaultChatWidgetMarkup}{newLine}{newLine}");
        }

        return content.TrimEnd() + $"{newLine}{newLine}{DefaultChatWidgetMarkup}{newLine}";
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

        updated = PreserveUtf8BomIfPresent(path, updated);

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

    private static string PreserveUtf8BomIfPresent(string path, string updated)
    {
        if (!File.Exists(path) || updated.Length == 0 || updated[0] == '\uFEFF')
        {
            return updated;
        }

        Span<byte> buffer = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        var read = stream.Read(buffer);
        return read == 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF
            ? "\uFEFF" + updated
            : updated;
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

    private static string InsertBeforeLineContainingAny(string content, string block, params string[] markers)
    {
        if (markers.Length == 0)
        {
            return content;
        }

        foreach (var marker in markers)
        {
            var index = content.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var lineStart = content.LastIndexOf('\n', index);
            return content.Insert(lineStart < 0 ? 0 : lineStart + 1, block);
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

    private static IReadOnlyList<string> ResolveHostSourceReferences(string? agentBlazorSourceRoot)
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

    private static IReadOnlyList<string> ResolveUiSourceReferences(string? agentBlazorSourceRoot)
    {
        if (string.IsNullOrWhiteSpace(agentBlazorSourceRoot))
        {
            return [];
        }

        var normalizedRoot = Path.GetFullPath(agentBlazorSourceRoot);
        var componentsProject = Path.Combine(normalizedRoot, "src", "AgentBlazor.Components", "AgentBlazor.Components.csproj");

        if (File.Exists(componentsProject))
        {
            return [componentsProject];
        }

        throw new InvalidOperationException(
            $"Could not find the AgentBlazor components project under '{normalizedRoot}'. Expected AgentBlazor.Components.");
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

    private sealed record CentralPackageFile(string Path, string Content);
}
