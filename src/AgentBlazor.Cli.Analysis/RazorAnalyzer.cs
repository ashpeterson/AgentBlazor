using System.Text.RegularExpressions;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Analyzes .razor files to extract routes, injections, layouts, and component usage.
/// Uses regex-based parsing for directives (simpler than full Razor SDK for this use case).
/// </summary>
public sealed partial class RazorAnalyzer
{
    // Regex patterns for Razor directives
    [GeneratedRegex(@"@page\s+""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex PageDirectiveRegex();

    [GeneratedRegex(@"@inject\s+(\S+)\s+(\S+)", RegexOptions.Compiled)]
    private static partial Regex InjectDirectiveRegex();

    [GeneratedRegex(@"@layout\s+(\S+)", RegexOptions.Compiled)]
    private static partial Regex LayoutDirectiveRegex();

    [GeneratedRegex(@"@namespace\s+(\S+)", RegexOptions.Compiled)]
    private static partial Regex NamespaceDirectiveRegex();

    // Pattern for component tags (PascalCase tags that aren't HTML)
    [GeneratedRegex(@"<([A-Z][a-zA-Z0-9_]*)\b", RegexOptions.Compiled)]
    private static partial Regex ComponentTagRegex();

    // Pattern for route parameters like {id}, {id:int}, {*catchall}
    [GeneratedRegex(@"\{(\*?)([^:}]+)(?::([^}]+))?\}", RegexOptions.Compiled)]
    private static partial Regex RouteParameterRegex();

    // Known MudBlazor components that indicate UI patterns
    private static readonly HashSet<string> MudBlazorComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        "MudDataGrid", "MudTable", "MudDialog", "MudForm", "MudButton",
        "MudTextField", "MudSelect", "MudAutocomplete", "MudDatePicker",
        "MudTimePicker", "MudCheckBox", "MudSwitch", "MudRadio",
        "MudCard", "MudPaper", "MudList", "MudNavMenu", "MudTabs",
        "MudStepper", "MudExpansionPanels", "MudTreeView", "MudChip",
        "MudAlert", "MudSnackbar", "MudTooltip", "MudPopover",
        "MudMenu", "MudDrawer", "MudAppBar", "MudNavGroup"
    };

    public async Task<RazorFileAnalysis> AnalyzeFileAsync(string filePath, CancellationToken ct = default)
    {
        var content = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        return AnalyzeContent(filePath, content);
    }

    public RazorFileAnalysis AnalyzeContent(string filePath, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var routes = new List<ExtractedRoute>();
        var injectedServices = new List<InjectedServiceModel>();
        string? layout = null;
        string? @namespace = null;
        var childComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mudComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract @page directives (can have multiple)
        foreach (Match match in PageDirectiveRegex().Matches(content))
        {
            var template = match.Groups[1].Value;
            var parameters = ExtractRouteParameters(template);
            routes.Add(new ExtractedRoute
            {
                Template = template,
                ComponentName = fileName,
                FilePath = filePath,
                Parameters = parameters
            });
        }

        // Extract @inject directives
        foreach (Match match in InjectDirectiveRegex().Matches(content))
        {
            injectedServices.Add(new InjectedServiceModel
            {
                TypeName = match.Groups[1].Value,
                FieldName = match.Groups[2].Value
            });
        }

        // Extract @layout
        var layoutMatch = LayoutDirectiveRegex().Match(content);
        if (layoutMatch.Success)
        {
            layout = layoutMatch.Groups[1].Value;
        }

        // Extract @namespace
        var namespaceMatch = NamespaceDirectiveRegex().Match(content);
        if (namespaceMatch.Success)
        {
            @namespace = namespaceMatch.Groups[1].Value;
        }

        // Extract child component references
        foreach (Match match in ComponentTagRegex().Matches(content))
        {
            var componentName = match.Groups[1].Value;

            // Skip HTML elements and common non-components
            if (IsKnownHtmlElement(componentName)) continue;

            childComponents.Add(componentName);

            if (MudBlazorComponents.Contains(componentName))
            {
                mudComponents.Add(componentName);
            }
        }

        return new RazorFileAnalysis
        {
            FilePath = filePath,
            ComponentName = fileName,
            Namespace = @namespace,
            IsPage = routes.Count > 0,
            Routes = routes,
            InjectedServices = injectedServices,
            Layout = layout,
            ChildComponents = childComponents.ToList(),
            MudBlazorComponents = mudComponents.ToList()
        };
    }

    private static List<RouteParameterModel> ExtractRouteParameters(string template)
    {
        var parameters = new List<RouteParameterModel>();

        foreach (Match match in RouteParameterRegex().Matches(template))
        {
            var isCatchAll = match.Groups[1].Value == "*";
            var name = match.Groups[2].Value;
            var constraint = match.Groups[3].Success ? match.Groups[3].Value : null;
            var isOptional = name.EndsWith('?') || constraint?.EndsWith('?') == true;

            // Clean up optional marker
            name = name.TrimEnd('?');
            constraint = constraint?.TrimEnd('?');

            parameters.Add(new RouteParameterModel
            {
                Name = name,
                Constraint = constraint,
                IsOptional = isOptional,
                IsCatchAll = isCatchAll
            });
        }

        return parameters;
    }

    private static bool IsKnownHtmlElement(string tagName)
    {
        // Common HTML elements that might match PascalCase pattern
        return tagName.Equals("PageTitle", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("HeadContent", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("HeadOutlet", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("ErrorBoundary", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("CascadingValue", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("Router", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("RouteView", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("FocusOnNavigate", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("AuthorizeView", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("NotAuthorized", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("Authorized", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans a directory for all .razor files and analyzes them.
    /// </summary>
    public async Task<IReadOnlyList<RazorFileAnalysis>> AnalyzeDirectoryAsync(
        string directory,
        CancellationToken ct = default)
    {
        var analyses = new List<RazorFileAnalysis>();
        var razorFiles = Directory.GetFiles(directory, "*.razor", SearchOption.AllDirectories);

        foreach (var file in razorFiles)
        {
            ct.ThrowIfCancellationRequested();
            var analysis = await AnalyzeFileAsync(file, ct);
            analyses.Add(analysis);
        }

        return analyses;
    }
}

public sealed class RazorFileAnalysis
{
    public string FilePath { get; init; } = "";
    public string ComponentName { get; init; } = "";
    public string? Namespace { get; init; }
    public bool IsPage { get; init; }
    public IReadOnlyList<ExtractedRoute> Routes { get; init; } = [];
    public IReadOnlyList<InjectedServiceModel> InjectedServices { get; init; } = [];
    public string? Layout { get; init; }
    public IReadOnlyList<string> ChildComponents { get; init; } = [];
    public IReadOnlyList<string> MudBlazorComponents { get; init; } = [];
}

public sealed class ExtractedRoute
{
    public string Template { get; init; } = "";
    public string ComponentName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public IReadOnlyList<RouteParameterModel> Parameters { get; init; } = [];
}
