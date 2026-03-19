namespace AgentBlazor.Core.Runtime.Interfaces;

public interface IAgentExecutionScopeAccessor
{
    IServiceProvider? Current { get; }

    IDisposable Push(IServiceProvider serviceProvider);
}
