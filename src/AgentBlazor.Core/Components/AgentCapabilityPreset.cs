namespace AgentBlazor.Components;

/// <summary>Preset for which component actions are registered in the catalog.</summary>
public enum AgentCapabilityPreset
{
    /// <summary>Same components as Full but only actions that do not require approval.</summary>
    Minimal = 0,
    /// <summary>All shipped component actions (including those requiring approval).</summary>
    Full = 1
}
