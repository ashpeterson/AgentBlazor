namespace AgentBlazor.Demo.Data;

internal sealed class DojoWorkspaceEntity
{
    public int Id { get; set; }

    public string SessionKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Minutes { get; set; }

    public string Difficulty { get; set; } = string.Empty;

    public bool HighProtein { get; set; }

    public bool LowCarb { get; set; }

    public bool Spicy { get; set; }

    public bool Vegetarian { get; set; }

    public bool BudgetFriendly { get; set; }

    public bool OnePotMeal { get; set; }

    public bool Vegan { get; set; }

    public DateTime? LastSavedUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
