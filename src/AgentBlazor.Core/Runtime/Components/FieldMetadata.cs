namespace AgentBlazor.Core.Runtime.Components;

/// <summary>
/// Metadata about a form field, including type information and validation constraints.
/// Extracted via reflection from data annotation attributes.
/// </summary>
public sealed record FieldMetadata
{
    /// <summary>
    /// The field's data type (e.g., "string", "integer", "boolean", "datetime", "enum").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the field is required (has [Required] attribute).
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Whether the field can be null.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Maximum length constraint (from [MaxLength] or [StringLength]).
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Minimum length constraint (from [MinLength] or [StringLength]).
    /// </summary>
    public int? MinLength { get; init; }

    /// <summary>
    /// Regex pattern constraint (from [RegularExpression]).
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Minimum value constraint (from [Range]).
    /// </summary>
    public object? MinValue { get; init; }

    /// <summary>
    /// Maximum value constraint (from [Range]).
    /// </summary>
    public object? MaxValue { get; init; }

    /// <summary>
    /// Allowed values for enum types.
    /// </summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }

    /// <summary>
    /// Display name (from [Display] attribute).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Description or prompt text (from [Display] attribute).
    /// </summary>
    public string? Description { get; init; }
}
