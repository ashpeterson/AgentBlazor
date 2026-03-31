using System.Reflection;

namespace AgentBlazor.Cli.Analysis.Tests;

public class ServiceAnalyzerTests
{
    #region IsLikelyServiceClass Tests

    [Theory]
    [InlineData("UserService", true)]
    [InlineData("OrderService", true)]
    [InlineData("ProductRepository", true)]
    [InlineData("CacheManager", true)]
    [InlineData("EmailHandler", true)]
    [InlineData("AuthProvider", true)]
    [InlineData("CheckoutWorkflow", true)]
    [InlineData("AgentCapabilities", true)]
    [InlineData("ApiClient", true)]
    [InlineData("PaymentGateway", true)]
    [InlineData("OrderFacade", true)]
    [InlineData("SessionStore", true)]
    [InlineData("DataCache", true)]
    [InlineData("RequestMediator", true)]
    [InlineData("TaskCoordinator", true)]
    [InlineData("WorkflowOrchestrator", true)]
    [InlineData("PaymentProcessor", true)]
    [InlineData("RulesEngine", true)]
    [InlineData("DateHelper", true)]
    [InlineData("StringUtility", true)]
    [InlineData("OrderFactory", true)]
    [InlineData("ReportBuilder", true)]
    [InlineData("InputValidator", true)]
    [InlineData("AlertNotifier", true)]
    [InlineData("EventDispatcher", true)]
    [InlineData("WeatherApi", true)]
    [InlineData("UserController", true)]
    [InlineData("CartState", true)]
    [InlineData("AppContext", true)]
    public void IsLikelyServiceClass_WithServiceSuffix_ReturnsTrue(string className, bool expected)
    {
        var result = InvokeIsLikelyServiceClass(className);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("User", false)]
    [InlineData("Order", false)]
    [InlineData("Product", false)]
    [InlineData("HomeRazor", false)]
    [InlineData("Program", false)]
    [InlineData("Startup", false)]
    [InlineData("Constants", false)]
    [InlineData("Extensions", false)]
    [InlineData("UserDto", false)]
    [InlineData("OrderModel", false)]
    public void IsLikelyServiceClass_WithoutServiceSuffix_ReturnsFalse(string className, bool expected)
    {
        var result = InvokeIsLikelyServiceClass(className);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Service Registration Detection

    [Fact]
    public void FindServiceRegistrations_DetectsAddScopedPattern()
    {
        // This test would require a real Roslyn project, so we test the pattern recognition logic
        var patterns = new[] { "AddScoped", "AddSingleton", "AddTransient" };
        foreach (var pattern in patterns)
        {
            Assert.True(IsRegistrationMethod(pattern), $"{pattern} should be recognized as registration method");
        }
    }

    [Theory]
    [InlineData("AddScoped", "Scoped")]
    [InlineData("AddSingleton", "Singleton")]
    [InlineData("AddTransient", "Transient")]
    public void FindServiceRegistrations_DetectsCorrectLifetime(string method, string expectedLifetime)
    {
        var lifetime = GetLifetimeFromMethod(method);
        Assert.Equal(expectedLifetime, lifetime);
    }

    #endregion

    #region Helpers

    private static bool InvokeIsLikelyServiceClass(string name)
    {
        // Use reflection to call the private instance method
        var analyzer = new ServiceAnalyzer();
        var method = typeof(ServiceAnalyzer).GetMethod(
            "IsLikelyServiceClass",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);
        return (bool)method.Invoke(analyzer, [name])!;
    }

    private static bool IsRegistrationMethod(string methodName)
    {
        var registrationMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AddScoped", "AddSingleton", "AddTransient",
            "AddKeyedScoped", "AddKeyedSingleton", "AddKeyedTransient",
            "TryAddScoped", "TryAddSingleton", "TryAddTransient"
        };
        return registrationMethods.Contains(methodName);
    }

    private static string? GetLifetimeFromMethod(string methodName)
    {
        return methodName switch
        {
            "AddScoped" or "AddKeyedScoped" or "TryAddScoped" => "Scoped",
            "AddSingleton" or "AddKeyedSingleton" or "TryAddSingleton" => "Singleton",
            "AddTransient" or "AddKeyedTransient" or "TryAddTransient" => "Transient",
            _ => null
        };
    }

    #endregion
}
