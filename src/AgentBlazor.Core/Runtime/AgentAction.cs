namespace AgentBlazor.Runtime;

public sealed record AgentAction(
    string Name,
    IReadOnlyDictionary<string, object?> Parameters)
{
    public static AgentAction Create(string name, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(
            name,
            parameters ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

    public bool TryGetParameter<T>(string key, out T? value)
    {
        value = default;
        if (!Parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        try
        {
            value = (T?)Convert.ChangeType(raw, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
