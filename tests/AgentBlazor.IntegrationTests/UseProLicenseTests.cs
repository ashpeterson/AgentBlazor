using AgentBlazor;
using AgentBlazor.Core.Paid;
using AgentBlazor.Licensing;
using AgentBlazor.Options;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentBlazor.IntegrationTests;

/// <summary>
/// Tests for UseProLicense() license key activation (2a).
/// </summary>
public class UseProLicenseTests
{
    [Fact]
    public void UseProLicense_ValidPaidKey_SetsTierToPaid()
    {
        var services = new ServiceCollection();

        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-PRO-VALID-KEY-12345678");
        });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.Equal(AgentBlazorTier.Paid, opts.LicensedTier);
    }

    [Fact]
    public void UseProLicense_ValidEnterpriseKey_SetsTierToPremium()
    {
        var services = new ServiceCollection();

        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-ENT-VALID-KEY-12345678");
        });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.Equal(AgentBlazorTier.Premium, opts.LicensedTier);
    }

    [Fact]
    public void UseProLicense_InvalidPrefix_Throws()
    {
        var options = new AgentBlazorRegistrationOptions();

        var ex = Assert.Throws<ArgumentException>(() =>
            options.UseProLicense("INVALID-KEY-1234567890"));

        Assert.Contains("AB-PRO-", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UseProLicense_TooShort_Throws()
    {
        var options = new AgentBlazorRegistrationOptions();

        Assert.Throws<ArgumentException>(() =>
            options.UseProLicense("AB-PRO-SHORT"));
    }

    [Fact]
    public void UseProLicense_EmptyKey_Throws()
    {
        var options = new AgentBlazorRegistrationOptions();

        Assert.Throws<ArgumentException>(() =>
            options.UseProLicense(""));
    }

    [Fact]
    public void NoLicense_DefaultTier_IsFree()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services);

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.Equal(AgentBlazorTier.Free, opts.LicensedTier);
    }

    [Fact]
    public async Task UseProLicense_OverridesDiWithPaidHistoryStore()
    {
        var services = new ServiceCollection();

        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseProLicense("AB-PRO-VALID-KEY-12345678");
        });

        var provider = services.BuildServiceProvider();
        try
        {
            var store = provider.GetRequiredService<IActionHistoryStore>();

            Assert.IsType<SqliteActionHistoryStore>(store);
        }
        finally
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public void NoLicense_UsesNullHistoryStore()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services);

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IActionHistoryStore>();

        // Free tier uses the no-op store
        Assert.IsNotType<InMemoryActionHistoryStore>(store);
    }

    [Fact]
    public void UseDevTools_EnablesDevToolsOptions()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseDevTools(autoShow: true);
        });

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<AgentBlazorOptions>>().Value;

        Assert.True(opts.EnableDevTools);
        Assert.True(opts.AutoShowDevTools);
    }

    [Fact]
    public void UseDevTools_RegistersInMemoryInspectorStore()
    {
        var services = new ServiceCollection();
        AgentBlazorUnifiedServiceCollectionExtensions.AddAgentBlazor(services, options =>
        {
            options.UseDevTools();
        });

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IAgentInspectorStore>();

        Assert.IsType<InMemoryAgentInspectorStore>(store);
    }
}
