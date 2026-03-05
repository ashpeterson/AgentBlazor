namespace AgentBlazor.Demo.Data;

internal sealed class DemoFileWorkflowEventEntity
{
    public long Id { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
