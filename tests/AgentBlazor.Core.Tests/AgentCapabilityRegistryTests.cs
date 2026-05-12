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
        Assert.Equal("missing_argument", result.Outputs["errorCode"]);
        Assert.Equal("supplierIds", result.Outputs["parameterName"]);
        Assert.Equal("supplier_compliance.prepare_remediation", result.Outputs["actionId"]);
        Assert.Contains(result.NextActions, action => action.Contains("supplierIds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequiredArgumentHasInvalidShape_ReturnsStructuredFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CapabilityGate { CanPrepare = true });
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<SupplierCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "supplier_compliance.show_at_risk_suppliers",
            new Dictionary<string, object?>
            {
                ["days"] = new { value = "soon" }
            },
            provider);

        Assert.False(result.Succeeded);
        Assert.False(result.RequiresClarification);
        Assert.Equal("invalid_argument_shape", result.Outputs["errorCode"]);
        Assert.Equal("days", result.Outputs["parameterName"]);
        Assert.Equal("an integer", result.Outputs["expectedShape"]);
        Assert.Equal("object", result.Outputs["actualShape"]);
        Assert.Contains("days", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.NextActions, action => action.Contains("days", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnumArgumentHasInvalidValue_ReturnsStructuredFailure()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddCapability<TicketCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "ticket_workflow.prioritize_ticket",
            new Dictionary<string, object?>
            {
                ["priority"] = "urgent-ish"
            },
            provider);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_argument_shape", result.Outputs["errorCode"]);
        Assert.Equal("priority", result.Outputs["parameterName"]);
        Assert.Contains(nameof(TicketPriority.High), result.Outputs["expectedShape"]?.ToString(), StringComparison.Ordinal);
        Assert.Contains("priority", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequiredArgumentIsNull_ReturnsStructuredFailure()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddCapability<TicketCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "ticket_workflow.summarize_ticket",
            new Dictionary<string, object?>
            {
                ["ticketId"] = null
            },
            provider);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_argument_shape", result.Outputs["errorCode"]);
        Assert.Equal("ticketId", result.Outputs["parameterName"]);
        Assert.Equal("null", result.Outputs["actualShape"]);
        Assert.DoesNotContain("Object reference", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCapabilityThrowsValidationException_ReturnsRecoverableFailure()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddCapability<TicketCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "ticket_workflow.summarize_ticket",
            new Dictionary<string, object?>
            {
                ["ticketId"] = "BAD-1"
            },
            provider);

        Assert.False(result.Succeeded);
        Assert.Equal("recoverable_failure", result.Outputs["errorCode"]);
        Assert.Equal("ticket_workflow.summarize_ticket", result.Outputs["actionId"]);
        Assert.Equal(nameof(ArgumentException), result.Outputs["exceptionType"]);
        Assert.Contains("ticketId must start with TCK-", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.NextActions, action => action.Contains("summarize_ticket", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAsyncCapabilityThrowsUnexpectedException_ReturnsTerseFailure()
    {
        var services = new ServiceCollection();
        services.AddAgentBlazorServices()
            .AddCapability<TicketCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "ticket_workflow.sync_external_ticket",
            new Dictionary<string, object?>
            {
                ["ticketId"] = "TCK-1042"
            },
            provider);

        Assert.False(result.Succeeded);
        Assert.Equal("capability_invocation_failed", result.Outputs["errorCode"]);
        Assert.Equal("ticket_workflow.sync_external_ticket", result.Outputs["actionId"]);
        Assert.Equal(nameof(ApplicationException), result.Outputs["exceptionType"]);
        Assert.Equal("Capability action 'ticket_workflow.sync_external_ticket' failed unexpectedly.", result.Summary);
        Assert.DoesNotContain("upstream CRM token", result.Summary, StringComparison.OrdinalIgnoreCase);
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
    public async Task ExecuteAsync_WhenRuntimeContextIsMissing_ReturnsStructuredFailure()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CapabilityRecorder>();
        services.AddAgentBlazorServices()
            .AddCapability<ContextCapabilities>();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IAgentCapabilityRegistry>();

        var result = await registry.ExecuteAsync(
            "context_scoped.capture_session",
            new Dictionary<string, object?>(),
            provider);

        Assert.False(result.Succeeded);
        Assert.False(result.RequiresClarification);
        Assert.Equal("missing_runtime_context", result.Outputs["errorCode"]);
        Assert.Equal(AgentRuntimeContextKeys.SessionId, result.Outputs["contextKey"]);
        Assert.Contains(result.NextActions, action => action.Contains("runtime context", StringComparison.OrdinalIgnoreCase));
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

    [AgentCapability("ticket_workflow", Name = "Ticket Workflow", Category = "Support")]
    public sealed class TicketCapabilities
    {
        [AgentAction("Prioritize a ticket", ActionId = "prioritize_ticket")]
        public CapabilityResult PrioritizeTicket(
            [AgentParam("Ticket priority", Required = true)] TicketPriority priority)
            => CapabilityResult.Success($"Priority set to {priority}.");

        [AgentAction("Summarize a ticket", ActionId = "summarize_ticket")]
        public CapabilityResult SummarizeTicket([AgentParam("Ticket ID", Required = true)] string ticketId)
        {
            if (!ticketId.StartsWith("TCK-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("ticketId must start with TCK-.", nameof(ticketId));
            }

            return CapabilityResult.Success($"Summarized {ticketId}.");
        }

        [AgentAction("Sync an external ticket", ActionId = "sync_external_ticket")]
        public async Task<CapabilityResult> SyncExternalTicketAsync([AgentParam("Ticket ID", Required = true)] string ticketId)
        {
            await Task.Yield();
            throw new ApplicationException($"upstream CRM token rejected while syncing {ticketId}");
        }
    }

    public enum TicketPriority
    {
        Low,
        Medium,
        High
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
