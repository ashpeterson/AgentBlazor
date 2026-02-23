namespace AgentBlazor.Components;

/// <summary>Applies capability presets to the component catalog (delegates to <see cref="AgentComponentCapabilityProfile"/>).</summary>
public static class AgentCapabilityPresets
{
    public static void Apply(ComponentCapabilityCatalogBuilder builder, AgentCapabilityPreset preset)
    {
        ArgumentNullException.ThrowIfNull(builder);
        switch (preset)
        {
            case AgentCapabilityPreset.Minimal:
                AgentComponentCapabilityProfile.ApplyMinimal(builder);
                break;
            case AgentCapabilityPreset.Full:
                AgentComponentCapabilityProfile.Apply(builder);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unsupported capability preset.");
        }
    }
}
