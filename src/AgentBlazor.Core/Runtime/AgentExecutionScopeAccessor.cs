using System.Threading;
using AgentBlazor.Core.Runtime.Interfaces;

namespace AgentBlazor.Core.Runtime;

internal sealed class AgentExecutionScopeAccessor : IAgentExecutionScopeAccessor
{
    private static readonly AsyncLocal<IServiceProvider?> CurrentProvider = new();

    public IServiceProvider? Current => CurrentProvider.Value;

    public IDisposable Push(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var previous = CurrentProvider.Value;
        CurrentProvider.Value = serviceProvider;
        return new RevertScope(previous);
    }

    private sealed class RevertScope(IServiceProvider? previous) : IDisposable
    {
        private IServiceProvider? _previous = previous;

        public void Dispose()
        {
            CurrentProvider.Value = _previous;
            _previous = null;
        }
    }
}
