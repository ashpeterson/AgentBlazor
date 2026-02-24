using System.Collections.Concurrent;

namespace AgentBlazor.Core.Runtime.Internal;

public interface IInternalPageStructureRegistry
{
    void RegisterPageStructure(string pageRoute, PageComponentNode root);
    void UpdateCurrentPage(string pageRoute);
    IReadOnlyList<PageComponentNode> GetCurrentPageComponents();
    PageComponentNode? GetPageStructure(string pageRoute);
    IReadOnlyList<string> GetKnownRoutes();
    void Clear();
}

public class PageComponentNode
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public List<PageComponentNode> Children { get; set; } = new();
    public Dictionary<string, string> Attributes { get; set; } = new();
    public List<ColumnDefinition> Columns { get; set; } = new();
    public List<string> AvailableActions { get; set; } = new();
    public Dictionary<string, string> State { get; set; } = new();
}

public class ColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public bool Sortable { get; set; }
    public bool Filterable { get; set; }
}

internal sealed class InMemoryPageStructureRegistry : IInternalPageStructureRegistry
{
    private readonly ConcurrentDictionary<string, PageComponentNode> _pageStructures = new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _currentPageRoute;

    public void RegisterPageStructure(string pageRoute, PageComponentNode root)
    {
        _pageStructures[pageRoute.Trim('/').ToLowerInvariant()] = root;
    }

    public void UpdateCurrentPage(string pageRoute)
    {
        _currentPageRoute = pageRoute.Trim('/').ToLowerInvariant();
    }

    public IReadOnlyList<PageComponentNode> GetCurrentPageComponents()
    {
        if (string.IsNullOrWhiteSpace(_currentPageRoute))
        {
            return [];
        }

        if (!_pageStructures.TryGetValue(_currentPageRoute, out var root))
        {
            return [];
        }

        return FlattenTree(root);
    }

    public PageComponentNode? GetPageStructure(string pageRoute)
    {
        return _pageStructures.GetValueOrDefault(pageRoute.Trim('/').ToLowerInvariant());
    }

    public IReadOnlyList<string> GetKnownRoutes()
    {
        return _pageStructures.Keys.ToList().Select(k => "/" + k).ToList();
    }

    public void Clear()
    {
        _pageStructures.Clear();
        _currentPageRoute = null;
    }

    private static List<PageComponentNode> FlattenTree(PageComponentNode root)
    {
        var result = new List<PageComponentNode> { root };
        FlattenChildren(root, result);
        return result;
    }

    private static void FlattenChildren(PageComponentNode node, List<PageComponentNode> result)
    {
        foreach (var child in node.Children)
        {
            result.Add(child);
            FlattenChildren(child, result);
        }
    }
}
