using AgentBlazor.Core.Paid;

namespace AgentBlazor.Components.Inspector;

public static class InspectorRunCorrelationLens
{
    public static IReadOnlyDictionary<string, int> BuildHandoffChainMap(
        IReadOnlyList<InspectorRunRecord> runs,
        TimeSpan? maxGap = null)
    {
        if (runs.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var threshold = maxGap ?? TimeSpan.FromMinutes(2);
        var ordered = runs
            .OrderBy(static run => run.StartedAt)
            .ThenBy(static run => run.RunId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chainMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentChainId = 0;
        InspectorRunRecord? previous = null;

        foreach (var run in ordered)
        {
            if (previous is null || !IsLinked(previous, run, threshold))
            {
                currentChainId++;
            }

            chainMap[run.RunId] = currentChainId;
            previous = run;
        }

        return chainMap;
    }

    public static bool TryGetLastHandoff(InspectorRunRecord run, out string fromAgent, out string toAgent)
    {
        fromAgent = string.Empty;
        toAgent = string.Empty;

        var handoffDetail = run.Events
            .LastOrDefault(static ev => string.Equals(ev.Kind, "AgentHandoff", StringComparison.OrdinalIgnoreCase))
            ?.Detail;

        return TryParseHandoffDetail(handoffDetail, out fromAgent, out toAgent);
    }

    public static bool TryParseHandoffDetail(string? handoffDetail, out string fromAgent, out string toAgent)
    {
        fromAgent = string.Empty;
        toAgent = string.Empty;

        if (string.IsNullOrWhiteSpace(handoffDetail))
        {
            return false;
        }

        var detail = handoffDetail.Trim();
        var arrowIndex = detail.IndexOf("->", StringComparison.Ordinal);
        if (arrowIndex <= 0 || arrowIndex >= detail.Length - 2)
        {
            return false;
        }

        var left = detail[..arrowIndex].Trim();
        var right = detail[(arrowIndex + 2)..].Trim();
        var atIndex = right.IndexOf(" @ ", StringComparison.Ordinal);
        if (atIndex >= 0)
        {
            right = right[..atIndex].Trim();
        }

        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        fromAgent = left;
        toAgent = right;
        return true;
    }

    private static bool IsLinked(
        InspectorRunRecord previous,
        InspectorRunRecord current,
        TimeSpan threshold)
    {
        var previousEnd = previous.FinishedAt ?? previous.StartedAt;
        if (current.StartedAt < previous.StartedAt)
        {
            return false;
        }

        if (current.StartedAt - previousEnd > threshold)
        {
            return false;
        }

        if (TryGetLastHandoff(previous, out _, out var handoffTo))
        {
            return string.Equals(handoffTo, current.AgentName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
