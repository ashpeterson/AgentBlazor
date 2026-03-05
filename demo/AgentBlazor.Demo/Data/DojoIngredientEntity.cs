namespace AgentBlazor.Demo.Data;

internal sealed class DojoIngredientEntity
{
    public int Id { get; set; }

    public string SessionKey { get; set; } = string.Empty;

    public string IngredientId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Amount { get; set; } = string.Empty;

    public bool Optional { get; set; }

    public string Notes { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
