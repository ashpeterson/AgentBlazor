namespace AgentBlazor.Components.Chat;

/// <summary>
/// Comprehensive theme configuration for Agent Chat components.
/// Supports built-in variants, custom CSS variables, and fine-grained visual controls.
/// </summary>
public sealed class ChatTheme
{
    /// <summary>Built-in color variant (Light, Dark, System, or Default for host-controlled).</summary>
    public ChatThemeVariant Variant { get; set; } = ChatThemeVariant.Default;

    /// <summary>Visual style preset for message bubbles and containers.</summary>
    public ChatVisualStyle Style { get; set; } = ChatVisualStyle.Modern;

    /// <summary>Border radius preset (None, Small, Medium, Large, Full).</summary>
    public ChatBorderRadius BorderRadius { get; set; } = ChatBorderRadius.Medium;

    /// <summary>Spacing/density preset (Compact, Normal, Comfortable).</summary>
    public ChatSpacing Spacing { get; set; } = ChatSpacing.Normal;

    /// <summary>Optional CSS custom property overrides. Applied after built-in values.</summary>
    public IReadOnlyDictionary<string, string>? CssVariables { get; set; }

    /// <summary>Optional CSS class(es) applied to the chat root element.</summary>
    public string? CssClass { get; set; }

    // ─── Color Overrides ────────────────────────────────────────────────────────

    /// <summary>Primary accent color (buttons, links, focus rings).</summary>
    public string? AccentColor { get; set; }

    /// <summary>Background color for the chat container.</summary>
    public string? BackgroundColor { get; set; }

    /// <summary>Background color for user message bubbles.</summary>
    public string? UserBubbleColor { get; set; }

    /// <summary>Background color for assistant message bubbles.</summary>
    public string? AssistantBubbleColor { get; set; }

    /// <summary>Primary text color.</summary>
    public string? TextColor { get; set; }

    /// <summary>Secondary/muted text color.</summary>
    public string? TextMutedColor { get; set; }

    /// <summary>Border color for inputs and containers.</summary>
    public string? BorderColor { get; set; }

    // ─── Typography ─────────────────────────────────────────────────────────────

    /// <summary>Font family for the chat interface.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Base font size (e.g., "14px", "0.875rem").</summary>
    public string? FontSize { get; set; }

    // ─── Visual Effects ─────────────────────────────────────────────────────────

    /// <summary>Enable glass-morphism backdrop blur effect.</summary>
    public bool EnableGlassEffect { get; set; } = true;

    /// <summary>Enable smooth animations and transitions.</summary>
    public bool EnableAnimations { get; set; } = true;

    /// <summary>Enable shadow effects on bubbles and containers.</summary>
    public bool EnableShadows { get; set; } = true;

    // ─── Component Visibility ───────────────────────────────────────────────────

    /// <summary>Show timestamps on messages.</summary>
    public bool ShowTimestamps { get; set; } = false;

    /// <summary>Show typing indicator when agent is processing.</summary>
    public bool ShowTypingIndicator { get; set; } = true;

    /// <summary>Show message status indicators (sent, delivered, etc.).</summary>
    public bool ShowMessageStatus { get; set; } = false;

    /// <summary>Show avatars next to messages.</summary>
    public bool ShowAvatars { get; set; } = false;

    /// <summary>URL or icon class for user avatar.</summary>
    public string? UserAvatar { get; set; }

    /// <summary>URL or icon class for assistant avatar.</summary>
    public string? AssistantAvatar { get; set; }

    // ─── Layout Options ─────────────────────────────────────────────────────────

    /// <summary>Message alignment style.</summary>
    public ChatMessageAlignment MessageAlignment { get; set; } = ChatMessageAlignment.Aligned;

    /// <summary>Input position (Bottom or Top).</summary>
    public ChatInputPosition InputPosition { get; set; } = ChatInputPosition.Bottom;

    /// <summary>Maximum width for message bubbles (e.g., "80%", "400px").</summary>
    public string? MaxBubbleWidth { get; set; }

    // ─── Computed Properties ────────────────────────────────────────────────────

    /// <summary>Gets the data-theme attribute value.</summary>
    public string? DataTheme => Variant switch
    {
        ChatThemeVariant.Light => "light",
        ChatThemeVariant.Dark => "dark",
        _ => null
    };

    /// <summary>Gets the data-style attribute value.</summary>
    public string DataStyle => Style switch
    {
        ChatVisualStyle.Classic => "classic",
        ChatVisualStyle.Minimal => "minimal",
        ChatVisualStyle.Glass => "glass",
        _ => "modern"
    };

    /// <summary>Gets the data-radius attribute value.</summary>
    public string DataRadius => BorderRadius switch
    {
        ChatBorderRadius.None => "none",
        ChatBorderRadius.Small => "small",
        ChatBorderRadius.Large => "large",
        ChatBorderRadius.Full => "full",
        _ => "medium"
    };

    /// <summary>Gets the data-spacing attribute value.</summary>
    public string DataSpacing => Spacing switch
    {
        ChatSpacing.Compact => "compact",
        ChatSpacing.Comfortable => "comfortable",
        _ => "normal"
    };

    /// <summary>Builds the inline style string from all configuration options.</summary>
    public string? GetStyleString()
    {
        var styles = new List<string>();

        // Color overrides
        if (!string.IsNullOrWhiteSpace(AccentColor))
            styles.Add($"--ab-chat-accent: {AccentColor}");
        if (!string.IsNullOrWhiteSpace(BackgroundColor))
            styles.Add($"--ab-chat-bg: {BackgroundColor}");
        if (!string.IsNullOrWhiteSpace(UserBubbleColor))
            styles.Add($"--ab-chat-user-bg: {UserBubbleColor}");
        if (!string.IsNullOrWhiteSpace(AssistantBubbleColor))
            styles.Add($"--ab-chat-assistant-bg: {AssistantBubbleColor}");
        if (!string.IsNullOrWhiteSpace(TextColor))
            styles.Add($"--ab-chat-text: {TextColor}");
        if (!string.IsNullOrWhiteSpace(TextMutedColor))
            styles.Add($"--ab-chat-text-muted: {TextMutedColor}");
        if (!string.IsNullOrWhiteSpace(BorderColor))
            styles.Add($"--ab-chat-border: {BorderColor}");

        // Typography
        if (!string.IsNullOrWhiteSpace(FontFamily))
            styles.Add($"--ab-chat-font: {FontFamily}");
        if (!string.IsNullOrWhiteSpace(FontSize))
            styles.Add($"--ab-chat-font-size: {FontSize}");

        // Layout
        if (!string.IsNullOrWhiteSpace(MaxBubbleWidth))
            styles.Add($"--ab-chat-bubble-max-width: {MaxBubbleWidth}");

        // Custom variables
        if (CssVariables is { Count: > 0 })
        {
            foreach (var kv in CssVariables)
            {
                styles.Add($"{kv.Key}: {kv.Value}");
            }
        }

        return styles.Count > 0 ? string.Join("; ", styles) : null;
    }

    /// <summary>Gets CSS classes based on feature flags.</summary>
    public string GetFeatureClasses()
    {
        var classes = new List<string>();

        if (!EnableAnimations)
            classes.Add("ab-chat--no-animations");
        if (!EnableShadows)
            classes.Add("ab-chat--no-shadows");
        if (!EnableGlassEffect)
            classes.Add("ab-chat--no-glass");
        if (ShowAvatars)
            classes.Add("ab-chat--with-avatars");
        if (ShowTimestamps)
            classes.Add("ab-chat--with-timestamps");
        if (MessageAlignment == ChatMessageAlignment.Bubbles)
            classes.Add("ab-chat--bubble-style");

        return string.Join(" ", classes);
    }

    // ─── Static Presets ─────────────────────────────────────────────────────────

    /// <summary>Creates a minimal, clean theme.</summary>
    public static ChatTheme Minimal() => new()
    {
        Style = ChatVisualStyle.Minimal,
        EnableGlassEffect = false,
        EnableShadows = false,
        BorderRadius = ChatBorderRadius.Small
    };

    /// <summary>Creates a glass-morphism theme.</summary>
    public static ChatTheme Glass() => new()
    {
        Style = ChatVisualStyle.Glass,
        EnableGlassEffect = true,
        EnableShadows = true,
        BorderRadius = ChatBorderRadius.Large
    };

    /// <summary>Creates a dark mode theme.</summary>
    public static ChatTheme Dark() => new()
    {
        Variant = ChatThemeVariant.Dark,
        Style = ChatVisualStyle.Modern
    };

    /// <summary>Creates a light mode theme.</summary>
    public static ChatTheme Light() => new()
    {
        Variant = ChatThemeVariant.Light,
        Style = ChatVisualStyle.Modern
    };

    /// <summary>Creates a branded theme with custom accent color.</summary>
    public static ChatTheme Branded(string accentColor) => new()
    {
        AccentColor = accentColor,
        Style = ChatVisualStyle.Modern
    };
}

/// <summary>Built-in color scheme variants.</summary>
public enum ChatThemeVariant
{
    /// <summary>Use host/system preference or CssVariables only.</summary>
    Default,

    /// <summary>Light color scheme.</summary>
    Light,

    /// <summary>Dark color scheme.</summary>
    Dark
}

/// <summary>Visual style presets.</summary>
public enum ChatVisualStyle
{
    /// <summary>Modern with subtle gradients and shadows.</summary>
    Modern,

    /// <summary>Clean lines, minimal decoration.</summary>
    Minimal,

    /// <summary>Traditional chat UI style.</summary>
    Classic,

    /// <summary>Glass-morphism with backdrop blur.</summary>
    Glass
}

/// <summary>Border radius presets.</summary>
public enum ChatBorderRadius
{
    /// <summary>No border radius (square corners).</summary>
    None,

    /// <summary>Small radius (4px).</summary>
    Small,

    /// <summary>Medium radius (8px).</summary>
    Medium,

    /// <summary>Large radius (16px).</summary>
    Large,

    /// <summary>Full/pill radius.</summary>
    Full
}

/// <summary>Spacing/density presets.</summary>
public enum ChatSpacing
{
    /// <summary>Reduced padding and gaps.</summary>
    Compact,

    /// <summary>Standard spacing.</summary>
    Normal,

    /// <summary>Increased padding and gaps.</summary>
    Comfortable
}

/// <summary>Message alignment options.</summary>
public enum ChatMessageAlignment
{
    /// <summary>All messages left-aligned with role labels.</summary>
    Aligned,

    /// <summary>User right, assistant left (bubble style).</summary>
    Bubbles
}

/// <summary>Input field position.</summary>
public enum ChatInputPosition
{
    /// <summary>Input at the bottom.</summary>
    Bottom,

    /// <summary>Input at the top.</summary>
    Top
}
