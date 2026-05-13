using AgentBlazor.Demo.Services;

namespace AgentBlazor.IntegrationTests;

public class DemoTrafficRouteFilterTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/demo")]
    [InlineData("/demo/workflows/support-inbox")]
    [InlineData("/docs/quickstart")]
    public void IsHumanPageViewRoute_AllowsBrowserRoutes(string path)
    {
        Assert.True(DemoTrafficRouteFilter.IsHumanPageViewRoute(path));
    }

    [Theory]
    [InlineData("/_blazor")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/AgentBlazor/AgentBlazor.min.js")]
    [InlineData("/internal/demo-logs")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    [InlineData("/favicon.png")]
    [InlineData("/.git/HEAD")]
    [InlineData("/wp-admin/")]
    [InlineData("/wp-login.php")]
    [InlineData("/xmlrpc.php")]
    [InlineData("/phpmyadmin")]
    public void IsHumanPageViewRoute_RejectsFrameworkAssetsAndBotProbes(string path)
    {
        Assert.False(DemoTrafficRouteFilter.IsHumanPageViewRoute(path));
    }
}
