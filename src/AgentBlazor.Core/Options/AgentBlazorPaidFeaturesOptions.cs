namespace AgentBlazor.Options;

/// <summary>
/// Controls paid-only capabilities that can be turned on or off at runtime.
/// </summary>
public sealed class AgentBlazorPaidFeaturesOptions
{
    /// <summary>
    /// Enables persistent memory routing. When disabled, in-memory services are used.
    /// </summary>
    public bool EnablePersistentMemory { get; set; }

    /// <summary>
    /// When true and persistent memory is enabled, startup/runtime should fail if
    /// persistent providers are not registered.
    /// </summary>
    public bool RequirePersistentProviders { get; set; }
}
