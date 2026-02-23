using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Options;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Core.Runtime.Routing;

/// <summary>
/// In-memory implementation of IRouteRegistry with fuzzy matching support.
/// When constructed with options, scans options.AssembliesToScan for [Route] pages on first use.
/// </summary>
internal sealed class InMemoryRouteRegistry : IRouteRegistry
{
    private readonly ConcurrentDictionary<string, RouteDefinition> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOptions<AgentBlazorOptions>? _options;
    private bool _scanned;
    private readonly object _scanLock = new();

    public InMemoryRouteRegistry(IOptions<AgentBlazorOptions>? options = null)
    {
        _options = options;
    }

    public void Register(RouteDefinition route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var normalizedPath = NormalizePath(route.Path);
        _routes[normalizedPath] = route with { Path = normalizedPath };
    }

    public bool TryResolve(string intent, out RouteMatch match)
    {
        match = default!;
        EnsureScanned();

        if (string.IsNullOrWhiteSpace(intent))
            return false;

        var matches = ResolveAll(intent, 1);
        if (matches.Count == 0)
            return false;

        match = matches[0];
        return true;
    }

    public IReadOnlyCollection<RouteDefinition> GetAll()
    {
        EnsureScanned();
        return _routes.Values.ToArray();
    }

    public IReadOnlyList<RouteMatch> ResolveAll(string intent, int maxResults = 5)
    {
        EnsureScanned();
        if (string.IsNullOrWhiteSpace(intent))
            return [];

        var normalizedIntent = NormalizeIntent(intent);
        var results = new List<(RouteDefinition Route, float Confidence, string Method)>();

        foreach (var route in _routes.Values)
        {
            // Check exact path match
            if (string.Equals(route.Path, normalizedIntent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(route.Path.TrimStart('/'), normalizedIntent, StringComparison.OrdinalIgnoreCase))
            {
                results.Add((route, 1.0f, "exact"));
                continue;
            }

            // Check alias match
            var aliasMatch = route.Aliases.FirstOrDefault(a =>
                string.Equals(a, normalizedIntent, StringComparison.OrdinalIgnoreCase) ||
                normalizedIntent.Contains(a, StringComparison.OrdinalIgnoreCase));

            if (aliasMatch is not null)
            {
                var confidence = string.Equals(aliasMatch, normalizedIntent, StringComparison.OrdinalIgnoreCase)
                    ? 0.95f
                    : 0.85f;
                results.Add((route, confidence, "alias"));
                continue;
            }

            // Check fuzzy match
            var fuzzyScore = CalculateFuzzyScore(normalizedIntent, route);
            if (fuzzyScore > 0.5f)
            {
                results.Add((route, fuzzyScore, "fuzzy"));
            }
        }

        return results
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.Route.Path.Length)
            .Take(maxResults)
            .Select(r => new RouteMatch
            {
                Path = r.Route.Path,
                Confidence = r.Confidence,
                Definition = r.Route,
                MatchMethod = r.Method
            })
            .ToArray();
    }

    public bool HasRoute(string path)
    {
        EnsureScanned();
        var normalized = NormalizePath(path);
        return _routes.ContainsKey(normalized);
    }

    public bool Remove(string path)
    {
        var normalized = NormalizePath(path);
        return _routes.TryRemove(normalized, out _);
    }

    public void Clear() => _routes.Clear();

    private void EnsureScanned()
    {
        if (_scanned || _options?.Value?.AssembliesToScan is not { Count: > 0 })
        {
            if (_options is not null)
                _scanned = true;
            return;
        }
        lock (_scanLock)
        {
            if (_scanned)
                return;
            foreach (var assembly in _options!.Value!.AssembliesToScan)
            {
                if (assembly is not null)
                    ScanAssembly(assembly);
            }
            _scanned = true;
        }
    }

    /// <summary>
    /// Scans an assembly for Blazor page components and registers their routes.
    /// This method looks for types with RouteAttribute (via reflection) to support
    /// both Blazor and ASP.NET Core route definitions.
    /// </summary>
    public void ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetExportedTypes())
        {
            // Look for RouteAttribute by name (supports both Blazor and ASP.NET Core)
            var routeAttributes = type.GetCustomAttributes()
                .Where(a => a.GetType().Name == "RouteAttribute")
                .ToArray();

            foreach (var routeAttr in routeAttributes)
            {
                // Get the Template property via reflection
                var templateProperty = routeAttr.GetType().GetProperty("Template");
                var template = templateProperty?.GetValue(routeAttr) as string;

                if (string.IsNullOrWhiteSpace(template))
                    continue;

                var description = type.GetCustomAttribute<DescriptionAttribute>()?.Description
                    ?? type.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
                    ?? GenerateDescriptionFromTypeName(type.Name);

                var route = new RouteDefinition
                {
                    Path = NormalizePath(template),
                    Description = description,
                    Aliases = GenerateAliasesFromPath(template),
                    PageTypeName = type.FullName
                };

                Register(route);
            }
        }
    }

    /// <summary>
    /// Registers common default routes.
    /// </summary>
    public void RegisterDefaults()
    {
        Register(RouteDefinition.Home());
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        path = path.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        // Remove route parameters for storage
        path = Regex.Replace(path, @"\{[^}]+\}", "");
        path = Regex.Replace(path, @"//+", "/");

        return path.TrimEnd('/').ToLowerInvariant();
    }

    private static string NormalizeIntent(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent))
            return string.Empty;

        // Remove common navigation prefixes
        var normalized = Regex.Replace(
            intent,
            @"^(?:go\s+to|navigate\s+to|open|show\s+me|take\s+me\s+to|bring\s+me\s+to)\s+(?:the\s+)?",
            string.Empty,
            RegexOptions.IgnoreCase);

        // Remove common suffixes
        normalized = Regex.Replace(
            normalized,
            @"\s+(?:page|screen|view|section)$",
            string.Empty,
            RegexOptions.IgnoreCase);

        return normalized.Trim().ToLowerInvariant();
    }

    private static float CalculateFuzzyScore(string intent, RouteDefinition route)
    {
        var pathSegments = route.Path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var intentTokens = Regex.Matches(intent, @"[a-zA-Z0-9]+")
            .Select(m => m.Value.ToLowerInvariant())
            .ToArray();

        if (intentTokens.Length == 0)
            return 0f;

        var matches = 0;
        var totalTokens = intentTokens.Length;

        foreach (var token in intentTokens)
        {
            // Check path segments
            if (pathSegments.Any(s => s.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                matches++;
                continue;
            }

            // Check aliases
            if (route.Aliases.Any(a => a.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                matches++;
                continue;
            }

            // Check with Levenshtein distance
            var minDistance = int.MaxValue;
            foreach (var segment in pathSegments.Concat(route.Aliases))
            {
                var distance = LevenshteinDistance(token, segment.ToLowerInvariant());
                minDistance = Math.Min(minDistance, distance);
            }

            // Accept if edit distance is small relative to word length
            if (minDistance <= Math.Max(1, token.Length / 3))
            {
                matches++;
            }
        }

        return (float)matches / totalTokens * 0.8f; // Cap fuzzy matches at 0.8 confidence
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
            dp[i, 0] = i;
        for (var j = 0; j <= n; j++)
            dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[m, n];
    }

    private static string GenerateDescriptionFromTypeName(string typeName)
    {
        // Remove common suffixes
        var name = typeName;
        foreach (var suffix in new[] { "Page", "Component", "View", "Screen" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        // Convert PascalCase to spaces
        name = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");

        return $"{name} page";
    }

    private static IReadOnlyList<string> GenerateAliasesFromPath(string path)
    {
        var aliases = new List<string>();
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            // Skip route parameters
            if (segment.StartsWith('{'))
                continue;

            var clean = segment.ToLowerInvariant();
            aliases.Add(clean);

            // Add singular/plural
            if (clean.EndsWith("s"))
                aliases.Add(clean[..^1]);
            else
                aliases.Add(clean + "s");

            // Add space-separated for kebab-case
            if (clean.Contains('-'))
                aliases.Add(clean.Replace("-", " "));
        }

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
