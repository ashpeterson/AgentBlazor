namespace AgentBlazor.Core.Paid;

public interface IAgentInspectorStore
{
    void RecordRun(InspectorRunRecord run);
    IReadOnlyList<InspectorRunRecord> GetRecentRuns(string sessionId, int limit = 20);
}
