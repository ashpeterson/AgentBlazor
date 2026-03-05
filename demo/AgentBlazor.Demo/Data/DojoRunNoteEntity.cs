namespace AgentBlazor.Demo.Data;

internal sealed class DojoRunNoteEntity
{
    public int Id { get; set; }

    public string SessionKey { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public string Message { get; set; } = string.Empty;
}
