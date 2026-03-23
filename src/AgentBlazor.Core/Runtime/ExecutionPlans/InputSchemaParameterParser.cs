namespace AgentBlazor.Core.Runtime.ExecutionPlans;

/// <summary>
/// Parses legacy action input schema strings into planner-friendly parameters.
/// Expected shape: "(type name [required|optional] [allowed: ...] — description)".
/// </summary>
internal static class InputSchemaParameterParser
{
    private const char EmDash = '\u2014';

    private static readonly HashSet<string> KnownTypeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "string",
        "integer",
        "int",
        "long",
        "number",
        "double",
        "float",
        "decimal",
        "boolean",
        "bool",
        "date",
        "datetime",
        "guid",
        "object",
        "array",
        "any"
    };

    public static List<ActionParameter> Parse(string? inputSchema)
    {
        if (string.IsNullOrWhiteSpace(inputSchema) || inputSchema == "()")
            return [];

        var content = inputSchema.Trim();
        if (content.StartsWith('(') && content.EndsWith(')'))
            content = content[1..^1].Trim();

        if (string.IsNullOrWhiteSpace(content))
            return [];

        // Capability profile schemas are JSON and should not be parsed with this legacy parser.
        if (content.StartsWith('{') || content.StartsWith('['))
            return [];

        var segments = SplitParameterSegments(content);
        var result = new List<ActionParameter>(segments.Count);
        foreach (var segment in segments)
        {
            if (TryParseSegment(segment, out var parameter))
                result.Add(parameter);
        }

        return result;
    }

    private static List<string> SplitParameterSegments(string content)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            var atTopLevel = !inSingleQuote && !inDoubleQuote && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0;

            if (c == ',' && atTopLevel && IsLikelyParameterBoundary(content, i + 1))
            {
                AddSegment(segments, current);
                current.Clear();
                continue;
            }

            current.Append(c);

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            switch (c)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0) bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0) braceDepth--;
                    break;
            }
        }

        AddSegment(segments, current);
        return segments;
    }

    private static void AddSegment(ICollection<string> segments, System.Text.StringBuilder current)
    {
        var value = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(value))
            segments.Add(value);
    }

    private static bool IsLikelyParameterBoundary(string content, int startIndex)
    {
        if (startIndex >= content.Length)
            return false;

        var candidate = PeekNextTopLevelChunk(content, startIndex);
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var hasMarker = candidate.Contains("[required]", StringComparison.OrdinalIgnoreCase)
                        || candidate.Contains("[optional]", StringComparison.OrdinalIgnoreCase);

        return TryReadSignature(candidate, requireKnownType: !hasMarker);
    }

    private static string PeekNextTopLevelChunk(string content, int startIndex)
    {
        var chunk = new System.Text.StringBuilder();

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (var i = startIndex; i < content.Length; i++)
        {
            var c = content[i];
            var atTopLevel = !inSingleQuote && !inDoubleQuote && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0;
            if (c == ',' && atTopLevel)
                break;

            chunk.Append(c);

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            switch (c)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0) bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0) braceDepth--;
                    break;
            }
        }

        return chunk.ToString().Trim();
    }

    private static bool TryReadSignature(string candidate, bool requireKnownType)
    {
        var header = candidate.TrimStart();
        if (header.Length == 0)
            return false;

        var emDashIndex = header.IndexOf(EmDash);
        if (emDashIndex >= 0)
            header = header[..emDashIndex];

        var markerIndex = header.IndexOf('[');
        if (markerIndex >= 0)
            header = header[..markerIndex];

        header = header.Trim();
        if (header.Length == 0)
            return false;

        var tokens = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var name = tokens[^1];
        if (!IsValidIdentifier(name))
            return false;

        if (!requireKnownType)
            return true;

        var typeLabel = string.Join(' ', tokens, 0, tokens.Length - 1);
        return LooksLikeTypeLabel(typeLabel);
    }

    private static bool TryParseSegment(string segment, out ActionParameter parameter)
    {
        parameter = default!;
        if (string.IsNullOrWhiteSpace(segment))
            return false;

        var content = segment.Trim();
        var (descriptionIndex, separatorLength) = FindDescriptionSeparator(content);

        var head = descriptionIndex >= 0
            ? content[..descriptionIndex].Trim()
            : content;

        var description = descriptionIndex >= 0
            ? content[(descriptionIndex + separatorLength)..].Trim()
            : null;

        if (string.IsNullOrWhiteSpace(description))
            description = null;

        var required = head.Contains("[required]", StringComparison.OrdinalIgnoreCase);

        var markerIndex = head.IndexOf('[');
        var signature = (markerIndex >= 0 ? head[..markerIndex] : head).Trim().TrimEnd(',');
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        var tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var name = tokens[^1];
        if (!IsValidIdentifier(name))
            return false;

        var type = string.Join(' ', tokens, 0, tokens.Length - 1);
        if (string.IsNullOrWhiteSpace(type))
            return false;

        parameter = new ActionParameter
        {
            Name = name,
            Type = type,
            Required = required,
            Description = description
        };

        return true;
    }

    private static (int Index, int SeparatorLength) FindDescriptionSeparator(string value)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var atTopLevel = !inSingleQuote && !inDoubleQuote && parenDepth == 0 && bracketDepth == 0 && braceDepth == 0;

            if (atTopLevel && c == EmDash)
                return (i, 1);

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
                continue;

            switch (c)
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0) parenDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0) bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0) braceDepth--;
                    break;
            }
        }

        var fallback = value.IndexOf(" - ", StringComparison.Ordinal);
        return fallback >= 0 ? (fallback, 3) : (-1, 0);
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!(char.IsLetter(value[0]) || value[0] == '_'))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }

        return true;
    }

    private static bool LooksLikeTypeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("array of ", StringComparison.OrdinalIgnoreCase))
            return true;

        var firstToken = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (KnownTypeWords.Contains(firstToken))
            return true;

        if (trimmed.Contains('<') || trimmed.Contains('[') || trimmed.Contains('.') || trimmed.EndsWith("?", StringComparison.Ordinal))
            return true;

        return char.IsUpper(trimmed[0]);
    }
}
