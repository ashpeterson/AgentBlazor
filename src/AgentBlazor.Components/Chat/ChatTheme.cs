namespace AgentBlazor.Components.Chat;

/// <summary>
/// Theme configuration for Agent Chat components (Surface, Panel, Widget).
/// Use <see cref="Variant"/> for built-in light/dark, or <see cref="CssVariables"/> for full control.
/// </summary>
public sealed class ChatTheme
{
    /// <summary>Built-in variant. When set, applies a consistent palette; use "Default" to rely only on CssVariables or host.</summary>
    public ChatThemeVariant Variant { get; set; } = ChatThemeVariant.Default;

    /// <summary>Optional CSS custom property overrides (e.g. "--ab-chat-primary": "#0f6bdc"). Applied after Variant.</summary>
    public IReadOnlyDictionary<string, string>? CssVariables { get; set; }

    /// <summary>Optional CSS class(es) applied to the chat root element.</summary>
    public string? CssClass { get; set; }

    /// <summary>Gets the data-theme value for the root element (e.g. "light", "dark", or null for default).</summary>
    public string? DataTheme => Variant switch
    {
        ChatThemeVariant.Light => "light",
        ChatThemeVariant.Dark => "dark",
        _ => null
    };

    /// <summary>Builds an inline style string from <see cref="CssVariables"/> (and variant defaults if needed).</summary>
    public string? GetStyleString()
    {
        if (CssVariables is null || CssVariables.Count == 0)
            return null;

        return string.Join("; ", CssVariables.Select(kv => $"{kv.Key}:{kv.Value}"));
    }
}

/// <summary>Built-in chat theme variants.</summary>
public enum ChatThemeVariant
{
    /// <summary>No variant; use host or CssVariables only.</summary>
    Default,

    /// <summary>Light background, dark text.</summary>
    Light,

    /// <summary>Dark background, light text.</summary>
    Dark
}
