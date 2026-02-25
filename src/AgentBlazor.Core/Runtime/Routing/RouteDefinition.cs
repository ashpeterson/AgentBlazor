namespace AgentBlazor.Core.Runtime.Routing;

/// <summary>
/// Defines a route that can be navigated to in the application.
/// </summary>
public sealed record RouteDefinition
{
    /// <summary>
    /// The route path (e.g., "/orders", "/settings/profile").
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Human-readable description of what this route shows.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Alternative names/phrases that can match this route.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// Additional metadata about the route.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The page/component type name if known.
    /// </summary>
    public string? PageTypeName { get; init; }

    /// <summary>
    /// Whether this route is the home/default route.
    /// </summary>
    public bool IsHome => Path is "/" or "" or "/home" or "/index";

    /// <summary>
    /// Creates a route definition from a path with auto-generated aliases.
    /// </summary>
    public static RouteDefinition FromPath(string path, string? description = null)
    {
        var aliases = GenerateAliases(path);
        return new RouteDefinition
        {
            Path = NormalizePath(path),
            Description = description ?? GenerateDescription(path),
            Aliases = aliases
        };
    }

    /// <summary>
    /// Creates a route definition for the home page.
    /// </summary>
    public static RouteDefinition Home(string? description = null) => new()
    {
        Path = "/",
        Description = description ?? "Home page",
        Aliases = ["home", "dashboard", "landing", "start", "main", "index"]
    };

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        path = path.Trim();
        if (!path.StartsWith('/'))
            path = "/" + path;

        return path.TrimEnd('/').ToLowerInvariant();
    }

    private static string GenerateDescription(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "Home page";

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "Home page";

        // Convert kebab-case to Title Case
        var name = string.Join(" ", segments.Select(s =>
            string.Join(" ", s.Split('-').Select(w =>
                w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w))));

        return $"{name} page";
    }

    private static List<string> GenerateAliases(string path)
    {
        var aliases = new List<string>();
        if (string.IsNullOrWhiteSpace(path))
            return aliases;

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            // Add the segment itself
            aliases.Add(segment.ToLowerInvariant());

            // Add singular/plural variations
            if (segment.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                aliases.Add(segment[..^1].ToLowerInvariant()); // Remove 's'
            }
            else
            {
                aliases.Add(segment.ToLowerInvariant() + "s"); // Add 's'
            }

            // Add kebab-case to space-separated
            if (segment.Contains('-'))
            {
                aliases.Add(segment.Replace("-", " ").ToLowerInvariant());
            }
        }

        // Add the full path without leading slash
        aliases.Add(path.TrimStart('/').ToLowerInvariant());

        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>
/// Represents a matched route with confidence score.
/// </summary>
public sealed record RouteMatch
{
    /// <summary>
    /// The matched route path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Confidence score between 0.0 and 1.0.
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Any parameters extracted from the match.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The route definition that was matched.
    /// </summary>
    public RouteDefinition? Definition { get; init; }

    /// <summary>
    /// How the match was made (exact, alias, fuzzy).
    /// </summary>
    public string MatchMethod { get; init; } = "exact";

    /// <summary>
    /// Creates a high-confidence exact match.
    /// </summary>
    public static RouteMatch Exact(RouteDefinition route) => new()
    {
        Path = route.Path,
        Confidence = 1.0f,
        Definition = route,
        MatchMethod = "exact"
    };

    /// <summary>
    /// Creates an alias-based match.
    /// </summary>
    public static RouteMatch FromAlias(RouteDefinition route, float confidence) => new()
    {
        Path = route.Path,
        Confidence = confidence,
        Definition = route,
        MatchMethod = "alias"
    };

    /// <summary>
    /// Creates a fuzzy match.
    /// </summary>
    public static RouteMatch Fuzzy(RouteDefinition route, float confidence) => new()
    {
        Path = route.Path,
        Confidence = confidence,
        Definition = route,
        MatchMethod = "fuzzy"
    };
}

/// <summary>
/// Builder for creating route definitions fluently.
/// </summary>
public sealed class RouteBuilder
{
    private readonly string _path;
    private string? _description;
    private readonly List<string> _aliases = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private string? _pageTypeName;

    public RouteBuilder(string path)
    {
        _path = path;
    }

    public RouteBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public RouteBuilder WithAlias(params string[] aliases)
    {
        _aliases.AddRange(aliases);
        return this;
    }

    public RouteBuilder WithMetadata(string key, string value)
    {
        _metadata[key] = value;
        return this;
    }

    public RouteBuilder WithPageType(string typeName)
    {
        _pageTypeName = typeName;
        return this;
    }

    public RouteDefinition Build()
    {
        var baseRoute = RouteDefinition.FromPath(_path, _description);
        return baseRoute with
        {
            Aliases = [..baseRoute.Aliases, .._aliases.Distinct(StringComparer.OrdinalIgnoreCase)],
            Metadata = _metadata.Count > 0 ? _metadata : baseRoute.Metadata,
            PageTypeName = _pageTypeName
        };
    }
}
