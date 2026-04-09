using System.Xml.Linq;

namespace AgentBlazor.Cli.Analysis;

internal static class TargetFrameworkSupport
{
    internal const string MinimumSupported = "net8.0";
    internal const string MaximumSupported = "net10.0";

    private static readonly string[] SupportedFrameworks = ["net8.0", "net9.0", "net10.0"];

    internal static IReadOnlyList<string> ReadTargetFrameworks(string csprojText)
    {
        try
        {
            var document = XDocument.Parse(csprojText);
            return document.Descendants()
                .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .SelectMany(element => (element.Value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(Normalize)
                .Where(framework => !string.IsNullOrWhiteSpace(framework))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    internal static bool IsSupported(string targetFramework)
        => SupportedFrameworks.Contains(Normalize(targetFramework), StringComparer.OrdinalIgnoreCase);

    internal static string Normalize(string targetFramework)
    {
        var value = targetFramework.Trim();
        if (value.StartsWith("net8.0", StringComparison.OrdinalIgnoreCase))
        {
            return "net8.0";
        }

        if (value.StartsWith("net9.0", StringComparison.OrdinalIgnoreCase))
        {
            return "net9.0";
        }

        if (value.StartsWith("net10.0", StringComparison.OrdinalIgnoreCase))
        {
            return "net10.0";
        }

        return value;
    }

    internal static string DescribeSupportRange()
        => $"{MinimumSupported} through {MaximumSupported}";
}
