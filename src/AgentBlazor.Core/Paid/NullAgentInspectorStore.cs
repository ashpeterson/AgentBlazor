namespace AgentBlazor.Core.Paid;

public sealed class NullAgentInspectorStore : IAgentInspectorStore
{
    public void RecordRun(InspectorRunRecord run) { }

    public IReadOnlyList<InspectorRunRecord> GetRecentRuns(string sessionId, int limit = 20) => [];
}
