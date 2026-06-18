using System.Text;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

public interface IAnalysisRetrieval
{
    IReadOnlyList<AnalysisRetrievalResult> Search(AnalysisCorpus corpus, string query, int maxResults = 8);
}

public sealed record AnalysisRetrievalResult
{
    public AnalysisCorpusChunk Chunk { get; init; } = new();

    public double Score { get; init; }

    public IReadOnlyList<string> MatchedTerms { get; init; } = [];
}

public sealed class LexicalAnalysisRetrieval : IAnalysisRetrieval
{
    public IReadOnlyList<AnalysisRetrievalResult> Search(AnalysisCorpus corpus, string query, int maxResults = 8)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var queryTerms = Tokenize(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTerms.Count == 0)
        {
            return [];
        }

        return corpus.Chunks
            .Select(chunk => ScoreChunk(chunk, queryTerms))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.Kind)
            .ThenBy(result => result.Chunk.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    private static AnalysisRetrievalResult ScoreChunk(
        AnalysisCorpusChunk chunk,
        HashSet<string> queryTerms)
    {
        var weightedText = new StringBuilder();
        weightedText.Append(chunk.Title).Append(' ').Append(chunk.Title).Append(' ');
        weightedText.Append(chunk.Text).Append(' ');
        weightedText.AppendJoin(' ', chunk.DomainTerms).Append(' ');
        weightedText.AppendJoin(' ', chunk.RelatedRoutes).Append(' ');
        weightedText.AppendJoin(' ', chunk.RelatedServices).Append(' ');
        weightedText.AppendJoin(' ', chunk.RelatedMethods);

        var chunkTerms = Tokenize(weightedText.ToString()).ToList();
        if (chunkTerms.Count == 0)
        {
            return new AnalysisRetrievalResult { Chunk = chunk };
        }

        var matchedTerms = chunkTerms
            .Where(queryTerms.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matchedTerms.Count == 0)
        {
            return new AnalysisRetrievalResult { Chunk = chunk };
        }

        var matchDensity = matchedTerms.Count / (double)queryTerms.Count;
        var frequency = chunkTerms.Count(term => queryTerms.Contains(term)) / (double)chunkTerms.Count;
        var kindBoost = chunk.Kind is AnalysisCorpusChunkKind.WorkflowCluster or AnalysisCorpusChunkKind.Method
            ? 0.15
            : 0.0;

        return new AnalysisRetrievalResult
        {
            Chunk = chunk,
            Score = Math.Round(matchDensity + frequency + kindBoost, 4),
            MatchedTerms = matchedTerms
        };
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var character in text)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(character) && !char.IsUpper(current[^1]))
            {
                Flush();
            }

            current.Append(char.ToLowerInvariant(character));
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length < 3)
            {
                current.Clear();
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }
    }
}
