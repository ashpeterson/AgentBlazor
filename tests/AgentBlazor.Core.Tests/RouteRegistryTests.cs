using AgentBlazor.Core.Runtime.Routing;

namespace AgentBlazor.Core.Tests;

public class RouteRegistryTests
{
    [Fact]
    public void Register_AddsRouteToRegistry()
    {
        var registry = new InMemoryRouteRegistry();
        var route = new RouteDefinition
        {
            Path = "/suppliers",
            Description = "Suppliers page",
            Aliases = ["supplier", "vendors"]
        };

        registry.Register(route);

        Assert.True(registry.HasRoute("/suppliers"));
    }

    [Fact]
    public void TryResolve_ExactMatch_ReturnsHighConfidence()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition
        {
            Path = "/suppliers",
            Description = "Suppliers page"
        });

        var found = registry.TryResolve("/suppliers", out var match);

        Assert.True(found);
        Assert.Equal("/suppliers", match.Path);
        Assert.Equal(1.0f, match.Confidence);
    }

    [Fact]
    public void TryResolve_AliasMatch_ReturnsMatch()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition
        {
            Path = "/suppliers",
            Description = "Suppliers page",
            Aliases = ["vendor", "vendors"]
        });

        var found = registry.TryResolve("vendor", out var match);

        Assert.True(found);
        Assert.Equal("/suppliers", match.Path);
        Assert.True(match.Confidence >= 0.85f);
    }

    [Fact]
    public void TryResolve_FuzzyMatch_ReturnsMatchWithLowerConfidence()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition
        {
            Path = "/suppliers",
            Description = "Suppliers page",
            Aliases = ["supplier"]
        });

        // "supplr" is close to "supplier" - should fuzzy match
        var found = registry.TryResolve("supplr", out var match);

        Assert.True(found);
        Assert.Equal("/suppliers", match.Path);
        Assert.True(match.Confidence < 1.0f);
    }

    [Fact]
    public void TryResolve_NoMatch_ReturnsFalse()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition
        {
            Path = "/suppliers",
            Description = "Suppliers page"
        });

        var found = registry.TryResolve("xyzabc123", out _);

        Assert.False(found);
    }

    [Fact]
    public void ResolveAll_ReturnsMultipleMatches()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition
        {
            Path = "/supplier-list",
            Description = "Supplier list",
            Aliases = ["supplier"]
        });
        registry.Register(new RouteDefinition
        {
            Path = "/supplier-details",
            Description = "Supplier details",
            Aliases = ["supplier"]
        });

        var matches = registry.ResolveAll("supplier", maxResults: 5);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void GetAll_ReturnsAllRegisteredRoutes()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition { Path = "/a", Description = "A" });
        registry.Register(new RouteDefinition { Path = "/b", Description = "B" });
        registry.Register(new RouteDefinition { Path = "/c", Description = "C" });

        var all = registry.GetAll();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void Remove_RemovesRouteFromRegistry()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition { Path = "/test", Description = "Test" });

        var removed = registry.Remove("/test");

        Assert.True(removed);
        Assert.False(registry.HasRoute("/test"));
    }

    [Fact]
    public void Clear_RemovesAllRoutes()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition { Path = "/a", Description = "A" });
        registry.Register(new RouteDefinition { Path = "/b", Description = "B" });

        registry.Clear();

        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void RegisterDefaults_RegistersHomeRoute()
    {
        var registry = new InMemoryRouteRegistry();

        registry.RegisterDefaults();

        Assert.True(registry.HasRoute("/"));
    }

    [Fact]
    public void TryResolve_NormalizesNavigationPhrases()
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition
        {
            Path = "/suppliers",
            Description = "Suppliers page",
            Aliases = ["suppliers"]
        });

        var found = registry.TryResolve("go to suppliers page", out var match);

        Assert.True(found);
        Assert.Equal("/suppliers", match.Path);
    }

    [Theory]
    [InlineData("/suppliers", "/suppliers")]
    [InlineData("suppliers", "/suppliers")]
    [InlineData("/SUPPLIERS", "/suppliers")]
    [InlineData("  /suppliers  ", "/suppliers")]
    public void Register_NormalizesPath(string input, string expectedNormalized)
    {
        var registry = new InMemoryRouteRegistry();
        registry.Register(new RouteDefinition { Path = input, Description = "Test" });

        Assert.True(registry.HasRoute(expectedNormalized));
    }

    [Fact]
    public void RouteDefinition_Home_CreatesCorrectRecord()
    {
        var home = RouteDefinition.Home();

        Assert.Equal("/", home.Path);
        Assert.Contains("home", home.Aliases);
        Assert.Contains("main", home.Aliases);
    }
}
