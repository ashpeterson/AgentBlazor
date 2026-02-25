namespace AgentBlazor.Core.Runtime.Components;

public enum EntityResolutionStatus
{
    Resolved = 0,
    Ambiguous = 1,
    NotFound = 2
}

public sealed record EntityResolutionResult(
    EntityResolutionStatus Status,
    string? Value,
    double Confidence,
    IReadOnlyList<string> Candidates);

/// <summary>
/// Deterministic resolver for field/column/identifier hints.
/// </summary>
public static class DeterministicEntityResolver
{
    private const double DefaultMinConfidence = 0.6;
    private const double DefaultAmbiguityMargin = 0.08;

    public static EntityResolutionResult Resolve(
        string hint,
        IEnumerable<string> candidates,
        IReadOnlyDictionary<string, string>? aliases = null,
        double minConfidence = DefaultMinConfidence,
        double ambiguityMargin = DefaultAmbiguityMargin)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return new EntityResolutionResult(EntityResolutionStatus.NotFound, null, 0, []);
        }

        var candidateList = candidates
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidateList.Length == 0)
        {
            return new EntityResolutionResult(EntityResolutionStatus.NotFound, null, 0, []);
        }

        var direct = candidateList.FirstOrDefault(candidate =>
            string.Equals(candidate, hint, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return new EntityResolutionResult(EntityResolutionStatus.Resolved, direct, 1.0, [direct]);
        }

        if (aliases is not null)
        {
            if (aliases.TryGetValue(hint, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped) &&
                candidateList.Any(candidate => string.Equals(candidate, mapped, StringComparison.OrdinalIgnoreCase)))
            {
                return new EntityResolutionResult(EntityResolutionStatus.Resolved, mapped, 0.99, [mapped]);
            }

            foreach (var (alias, canonical) in aliases)
            {
                if (!string.Equals(alias, hint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var matching = candidateList.FirstOrDefault(candidate =>
                    string.Equals(candidate, canonical, StringComparison.OrdinalIgnoreCase));
                if (matching is not null)
                {
                    return new EntityResolutionResult(EntityResolutionStatus.Resolved, matching, 0.99, [matching]);
                }
            }
        }

        var normalizedHint = NormalizeIdentifier(hint);
        if (normalizedHint.Length == 0)
        {
            return new EntityResolutionResult(EntityResolutionStatus.NotFound, null, 0, []);
        }

        var normalizedMatch = candidateList.FirstOrDefault(candidate =>
            string.Equals(NormalizeIdentifier(candidate), normalizedHint, StringComparison.OrdinalIgnoreCase));
        if (normalizedMatch is not null)
        {
            return new EntityResolutionResult(EntityResolutionStatus.Resolved, normalizedMatch, 0.97, [normalizedMatch]);
        }

        var scored = candidateList
            .Select(candidate => (Candidate: candidate, Score: ScoreMatch(hint, candidate)))
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Candidate, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var topSuggestions = scored
            .Take(3)
            .Select(static item => item.Candidate)
            .ToArray();

        if (scored.Length == 0 || scored[0].Score < minConfidence)
        {
            return new EntityResolutionResult(
                EntityResolutionStatus.NotFound,
                null,
                scored.Length == 0 ? 0 : scored[0].Score,
                topSuggestions);
        }

        if (scored.Length > 1 && Math.Abs(scored[0].Score - scored[1].Score) <= ambiguityMargin)
        {
            return new EntityResolutionResult(
                EntityResolutionStatus.Ambiguous,
                null,
                scored[0].Score,
                topSuggestions);
        }

        return new EntityResolutionResult(
            EntityResolutionStatus.Resolved,
            scored[0].Candidate,
            scored[0].Score,
            topSuggestions);
    }

    private static double ScoreMatch(string hint, string candidate)
    {
        var normalizedHint = NormalizeIdentifier(hint);
        var normalizedCandidate = NormalizeIdentifier(candidate);
        if (normalizedHint.Length == 0 || normalizedCandidate.Length == 0)
        {
            return 0;
        }

        var hintTokens = SplitIdentifierTokens(hint);
        var candidateTokens = SplitIdentifierTokens(candidate);
        var tokenOverlap = CalculateTokenOverlap(hintTokens, candidateTokens);

        var containsBonus =
            normalizedCandidate.Contains(normalizedHint, StringComparison.OrdinalIgnoreCase) ||
            normalizedHint.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase)
                ? 0.2
                : 0;

        var editSimilarity = CalculateEditSimilarity(normalizedHint, normalizedCandidate);
        return Math.Min(1.0, tokenOverlap * 0.6 + containsBonus + editSimilarity * 0.2);
    }

    private static double CalculateTokenOverlap(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var overlap = left.Count(token => right.Contains(token, StringComparer.OrdinalIgnoreCase));
        var denominator = Math.Max(left.Count, right.Count);
        return denominator == 0 ? 0 : (double)overlap / denominator;
    }

    private static double CalculateEditSimilarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var distance = LevenshteinDistance(left, right);
        var maxLength = Math.Max(left.Length, right.Length);
        return maxLength == 0 ? 1 : 1 - (double)distance / maxLength;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];

        for (var i = 0; i <= left.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }

    private static string NormalizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static List<string> SplitIdentifierTokens(string value)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        var builder = new System.Text.StringBuilder();
        char? previous = null;

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                Flush();
                previous = null;
                continue;
            }

            if (previous is not null &&
                char.IsLower(previous.Value) &&
                char.IsUpper(ch))
            {
                Flush();
            }

            builder.Append(char.ToLowerInvariant(ch));
            previous = ch;
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (builder.Length == 0)
            {
                return;
            }

            tokens.Add(builder.ToString());
            builder.Clear();
        }
    }
}
