namespace AgentBlazor.Demo.Data;

internal sealed class DemoFileWorkflowFileEntity
{
    public long Id { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string UploadMode { get; set; } = "Local";
    public string? StorageToken { get; set; }
    public DateTime AddedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
