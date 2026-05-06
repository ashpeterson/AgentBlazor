using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Paid.Audit;
using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using AgentBlazor.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.IntegrationTests;

public class CapabilityAuditIntegrationTests
{
    [Fact]
    public async Task CapabilityApprovalContinuation_PreservesApprovedArguments()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ApprovalCapabilityChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalCapabilityChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ApprovalCapabilities>()
            .AddAgent("approval-agent", agent =>
            {
                agent.WithAllowedActions("approval_workflow.create_change_request");
            });

        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();

        var approvalResponse = await runtime.RunTurnAsync(new AgentTurnRequest(
            "Create the change request",
            AgentName: "approval-agent",
            SessionId: "integration-approval-args"));

        var approval = Assert.Single(approvalResponse.PendingApprovals);
        Assert.Equal("CAB-99", approval.Parameters["name"]?.ToString());

        var approvedResponse = await runtime.RunTurnAsync(new AgentTurnRequest(
            "Approved. Continue by invoking the approved action(s): approval_workflow.create_change_request.",
            AgentName: "approval-agent",
            SessionId: "integration-approval-args",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentblazor.approvals"] = "approval_workflow.create_change_request",
                ["agentblazor.approvalArgs.approval_workflow.create_change_request"] = """{"name":"CAB-99"}"""
            }));

        var executionStep = Assert.Single(approvedResponse.ExecutionPlan?.Steps ?? []);
        Assert.Equal("Created 'CAB-99'.", executionStep.Message);
        Assert.False(approvedResponse.RequiresApproval);
    }

    [Fact]
    public async Task CapabilityExecution_WithAuditLog_DoesNotRequireHttpContextAccessor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RecordingAuditLogService>();
        services.AddSingleton<IAuditLogService>(static sp => sp.GetRequiredService<RecordingAuditLogService>());
        services.AddSingleton<ApprovalCapabilityChatClient>();
        services.AddSingleton<IChatClient>(static sp => sp.GetRequiredService<ApprovalCapabilityChatClient>());
        services.AddAgentBlazorServices()
            .UseChatClientRuntimeAdapter()
            .AddCapability<ApprovalCapabilities>()
            .AddAgent("approval-agent", agent =>
            {
                agent.WithAllowedActions("approval_workflow.create_change_request");
            });

        await using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IAgentRuntimeAdapter>();
        var auditLog = provider.GetRequiredService<RecordingAuditLogService>();

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            "Create the change request",
            AgentName: "approval-agent",
            SessionId: "integration-approval",
            Context: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentblazor.approvals"] = "all"
            }));

        Assert.Equal("approval-capability-invoked", response.ResponseText);
        Assert.Collection(
            auditLog.Events,
            evt => Assert.Equal(AuditEventType.ActionApproved, evt.EventType),
            evt => Assert.Equal(AuditEventType.ActionExecuted, evt.EventType));
    }

    [AgentCapability("approval_workflow", Name = "Approval Workflow")]
    private sealed class ApprovalCapabilities
    {
        [AgentAction("Create a change request", ActionId = "create_change_request", RequiresApproval = true)]
        public CapabilityResult CreateChangeRequest(string name = "Default")
            => CapabilityResult.Success($"Created '{name}'.");
    }

    private sealed class ApprovalCapabilityChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            var tool = Assert.Single(
                options?.Tools?.OfType<AIFunction>().Where(static function =>
                    function.Name.Contains("capability_approval_workflow_create_change_request", StringComparison.OrdinalIgnoreCase)) ??
                []);

            await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["name"] = "CAB-99"
            }), cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "approval-capability-invoked"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            _ = serviceType;
            _ = serviceKey;
            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingAuditLogService : IAuditLogService
    {
        public List<AuditEvent> Events { get; } = [];

        public Task LogAsync(AuditEvent evt, CancellationToken ct = default)
        {
            _ = ct;
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task LogActionAsync(
            string userId,
            string? userEmail,
            string actionId,
            string agentId,
            bool succeeded,
            string? errorMessage = null,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            _ = ct;
            Events.Add(new AuditEvent(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                userId,
                userEmail,
                succeeded ? AuditEventType.ActionExecuted : AuditEventType.ActionFailed,
                "action",
                actionId,
                succeeded
                    ? $"Executed action '{actionId}' via agent '{agentId}'"
                    : $"Failed to execute action '{actionId}' via agent '{agentId}': {errorMessage}",
                ipAddress));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
        {
            _ = query;
            _ = ct;
            return Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
        }

        public Task<IReadOnlyList<AuditEvent>> GetByUserAsync(string userId, int limit = 100, CancellationToken ct = default)
        {
            _ = userId;
            _ = limit;
            _ = ct;
            return Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
        }

        public Task<IReadOnlyList<AuditEvent>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
        {
            _ = limit;
            _ = ct;
            return Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
        }

        public Task<Stream> ExportAsync(AuditQuery query, AuditExportFormat format, CancellationToken ct = default)
        {
            _ = query;
            _ = format;
            _ = ct;
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task PruneAsync(int retentionDays = 365, CancellationToken ct = default)
        {
            _ = retentionDays;
            _ = ct;
            return Task.CompletedTask;
        }
    }
}
