using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Core.Runtime.Middleware;

public sealed class AgentTurnContext
{
    public AgentTurnContext(AgentTurnRequest request)
    {
        Request = request;
    }

    public AgentTurnRequest Request { get; }
    public AgentTurnResponse? Response { get; set; }
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
    public bool IsShortCircuited => Response is not null;
}
