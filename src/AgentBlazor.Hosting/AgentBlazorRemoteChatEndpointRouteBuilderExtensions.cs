using AgentBlazor.Core.Runtime.Agents;
using AgentBlazor.Core.Runtime.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AgentBlazor.Hosting;

public static class AgentBlazorRemoteChatEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAgentBlazorRemoteChat(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/agentblazor/chat/run")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(pattern, AgentBlazorRemoteChatEndpoint.RunAsync)
            .WithDisplayName("AgentBlazor Remote Chat");
    }
}

internal static class AgentBlazorRemoteChatEndpoint
{
    public static async Task<IResult> RunAsync(
        AgentBlazorRemoteChatRequest request,
        IAgentRuntimeAdapter runtime,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            return Results.BadRequest(new { error = "UserMessage is required." });
        }

        var response = await runtime.RunTurnAsync(new AgentTurnRequest(
            request.UserMessage,
            request.AgentName,
            request.SessionId,
            request.UserId,
            request.Context is { Count: > 0 }
                ? new Dictionary<string, string>(request.Context, StringComparer.Ordinal)
                : null), ct).ConfigureAwait(false);

        return Results.Ok(new AgentBlazorRemoteChatResponse(
            response.AgentName,
            response.ResponseText,
            response.RequiresClarification,
            response.ClarificationQuestion,
            response.RequiresApproval,
            response.PendingApprovals.Count));
    }
}

internal sealed record AgentBlazorRemoteChatRequest(
    string UserMessage,
    string? AgentName,
    string? SessionId,
    string? UserId,
    IReadOnlyDictionary<string, string>? Context);

internal sealed record AgentBlazorRemoteChatResponse(
    string AgentName,
    string ResponseText,
    bool RequiresClarification,
    string? ClarificationQuestion,
    bool RequiresApproval,
    int PendingApprovalCount);
