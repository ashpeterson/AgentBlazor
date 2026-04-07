using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.Core.Tests;

public class AgentCapabilityRegistryTests
{
    [Fact]
    public void GetCapabilities_ReturnsRegisteredCapabilityMetadata()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CapabilityGate { CanPrepare = true });
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<SupplierCapabilities>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var capabilities = registry.GetCapabilities(provider);

        var capability = Assert.Single(capabilities);
        Assert.Equal("supplier_compliance", capability.CapabilityId);
        Assert.Equal("Supplier Compliance", capability.Name);
        Assert.Equal("Compliance", capability.Category);

        Assert.Collection(
            capability.Actions.OrderBy(static action => action.ActionId, StringComparer.OrdinalIgnoreCase),
            prepare =>
            {
                Assert.Equal("supplier_compliance.prepare_remediation", prepare.ActionId);
                Assert.True(prepare.RequiresApproval);
                Assert.True(prepare.IsAvailable);
                var supplierIds = Assert.Single(prepare.Parameters);
                Assert.Equal("supplierIds", supplierIds.Name);
                Assert.Equal("array of string", supplierIds.Type);
                Assert.True(supplierIds.Required);
            },
            show =>
            {
                Assert.Equal("supplier_compliance.show_at_risk_suppliers", show.ActionId);
                Assert.False(show.RequiresApproval);
                Assert.True(show.IsAvailable);
                var days = Assert.Single(show.Parameters);
                Assert.Equal("days", days.Name);
                Assert.Equal("integer", days.Type);
                Assert.False(days.Required);
            });
    }

    [Fact]
    public void GetCapabilities_ExcludesUnavailableActions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CapabilityGate { CanPrepare = false });
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<SupplierCapabilities>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var capability = Assert.Single(registry.GetCapabilities(provider));

        Assert.DoesNotContain(capability.Actions, static action =>
            string.Equals(action.LocalActionId, "prepare_remediation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(capability.Actions, static action =>
            string.Equals(action.LocalActionId, "show_at_risk_suppliers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_InvokesCapabilityAndReturnsStructuredResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CapabilityGate { CanPrepare = true });
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<SupplierCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();
        var recorder = provider.GetRequiredService<CapabilityRecorder>();

        var result = await registry.ExecuteAsync(
            "supplier_compliance.show_at_risk_suppliers",
            new Dictionary<string, object?>
            {
                ["days"] = 14
            },
            provider);

        Assert.True(result.Succeeded);
        Assert.Equal("Prepared a 14-day at-risk supplier review.", result.Summary);
        Assert.Equal(14, recorder.LastDays);
        Assert.Equal(14, result.Outputs["days"]);
        Assert.Contains("highlight-at-risk-grid", result.NextActions);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequiredArgumentMissing_ReturnsClarification()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CapabilityGate { CanPrepare = true });
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<SupplierCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "supplier_compliance.prepare_remediation",
            new Dictionary<string, object?>(),
            provider);

        Assert.False(result.Succeeded);
        Assert.True(result.RequiresClarification);
        Assert.Contains("supplierIds", result.ClarificationQuestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CoercesArrayArgumentsFromRawList()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CapabilityGate { CanPrepare = true });
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<SupplierCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();
        var recorder = provider.GetRequiredService<CapabilityRecorder>();

        var result = await registry.ExecuteAsync(
            "supplier_compliance.prepare_remediation",
            new Dictionary<string, object?>
            {
                ["supplierIds"] = new List<object?> { "SUP-1", "SUP-2" }
            },
            provider);

        Assert.True(result.Succeeded);
        var supplierIds = Assert.IsType<string[]>(recorder.LastPreparedSupplierIds);
        Assert.Equal(["SUP-1", "SUP-2"], supplierIds);
    }

    [Fact]
    public async Task ExecuteAsync_BindsContextScopedParametersWithoutProjectingThem()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<ContextCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();
        var recorder = provider.GetRequiredService<CapabilityRecorder>();

        var capability = Assert.Single(registry.GetCapabilities(provider));
        var action = Assert.Single(capability.Actions);
        Assert.Empty(action.Parameters);
        Assert.DoesNotContain("agentblazor.session_id", action.InputSchema, StringComparison.OrdinalIgnoreCase);

        var result = await registry.ExecuteAsync(
            "context_scoped.capture_session",
            new Dictionary<string, object?>
            {
                [AgentRuntimeContextKeys.SessionId] = "ctx-123"
            },
            provider);

        Assert.True(result.Succeeded);
        Assert.Equal("ctx-123", recorder.LastSessionId);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation_WhenCapabilityIsCanceled()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddCapability<CancellationCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.ExecuteAsync(
                "cancellation_probe.wait_for_cancel",
                new Dictionary<string, object?>(),
                provider,
                cts.Token));
    }

    [AgentCapability(
        "supplier_compliance",
        Name = "Supplier Compliance",
        Description = "Compliance workflows over supplier records.",
        Category = "Compliance")]
    public sealed class SupplierCapabilities(CapabilityGate gate, CapabilityRecorder recorder)
    {
        [AgentAction(
            "Show suppliers likely to fail compliance review",
            ActionId = "show_at_risk_suppliers")]
        public CapabilityResult ShowAtRiskSuppliers([AgentParam("Days to look ahead")] int days = 30)
        {
            recorder.LastDays = days;
            return CapabilityResult.Success($"Prepared a {days}-day at-risk supplier review.") with
            {
                Outputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["days"] = days
                },
                NextActions = ["highlight-at-risk-grid"]
            };
        }

        [AgentAction(
            "Prepare remediation tasks for selected suppliers",
            ActionId = "prepare_remediation",
            RequiresApproval = true,
            AvailableWhen = nameof(CanPrepareRemediation))]
        public Task<CapabilityResult> PrepareRemediationAsync(
            [AgentParam("Supplier IDs", Required = true)] string[] supplierIds,
            CancellationToken cancellationToken = default)
        {
            recorder.LastPreparedSupplierIds = supplierIds;
            return Task.FromResult(CapabilityResult.Success(
                $"Prepared remediation for {supplierIds.Length} suppliers."));
        }

        private bool CanPrepareRemediation() => gate.CanPrepare;
    }

    [AgentCapability("context_scoped", Name = "Context Scoped", Category = "Workflow")]
    public sealed class ContextCapabilities(CapabilityRecorder recorder)
    {
        [AgentAction("Capture the current session", ActionId = "capture_session")]
        public CapabilityResult CaptureSessionAsync(
            [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId)
        {
            recorder.LastSessionId = sessionId;
            return CapabilityResult.Success($"Captured {sessionId}.");
        }
    }

    [AgentCapability("cancellation_probe", Name = "Cancellation Probe", Category = "Workflow")]
    public sealed class CancellationCapabilities
    {
        [AgentAction("Wait for cancellation", ActionId = "wait_for_cancel")]
        public async Task<CapabilityResult> WaitForCancelAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return CapabilityResult.Success("Unexpected completion.");
        }
    }

    public sealed class CapabilityGate
    {
        public bool CanPrepare { get; set; }
    }

    public sealed class CapabilityRecorder
    {
        public int LastDays { get; set; }

        public string[]? LastPreparedSupplierIds { get; set; }

        public string? LastSessionId { get; set; }
    }
}
