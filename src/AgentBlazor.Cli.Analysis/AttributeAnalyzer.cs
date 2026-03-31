using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Analyzes C# code for AgentBlazor attributes ([AgentCapability], [AgentAction], etc.).
/// These attributes override heuristic detection when present.
/// </summary>
public sealed class AttributeAnalyzer
{
    // Known attribute names (without "Attribute" suffix)
    private static readonly HashSet<string> CapabilityAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AgentCapability",
        "AgentCapabilityAttribute"
    };

    private static readonly HashSet<string> ActionAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AgentAction",
        "AgentActionAttribute"
    };

    private static readonly HashSet<string> IgnoreAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AgentIgnore",
        "AgentIgnoreAttribute"
    };

    /// <summary>
    /// Finds all classes marked with [AgentCapability] in a project.
    /// </summary>
    public async Task<IReadOnlyList<CapabilityInfo>> FindCapabilityClassesAsync(
        Project project,
        CancellationToken ct = default)
    {
        var capabilities = new List<CapabilityInfo>();
        var compilation = await project.GetCompilationAsync(ct);
        if (compilation == null) return capabilities;

        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            if (document.FilePath == null) continue;

            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
            if (syntaxTree == null) continue;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(ct);

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var capabilityAttr = FindAttribute(classDecl.AttributeLists, CapabilityAttributes);
                if (capabilityAttr == null) continue;

                var className = classDecl.Identifier.Text;
                var capabilityInfo = new CapabilityInfo
                {
                    ClassName = className,
                    FilePath = document.FilePath,
                    CapabilityId = ExtractAttributeArgument(capabilityAttr, 0) ??
                                  ExtractNamedArgument(capabilityAttr, "CapabilityId"),
                    Name = ExtractNamedArgument(capabilityAttr, "Name"),
                    Description = ExtractNamedArgument(capabilityAttr, "Description"),
                    Category = ExtractNamedArgument(capabilityAttr, "Category"),
                    Actions = FindActionMethods(classDecl, document.FilePath)
                };

                capabilities.Add(capabilityInfo);
            }
        }

        return capabilities;
    }

    /// <summary>
    /// Finds methods marked with [AgentAction] in a class declaration.
    /// </summary>
    private IReadOnlyList<ActionInfo> FindActionMethods(ClassDeclarationSyntax classDecl, string filePath)
    {
        var actions = new List<ActionInfo>();

        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            // Check for [AgentIgnore]
            if (FindAttribute(method.AttributeLists, IgnoreAttributes) != null)
            {
                continue;
            }

            var actionAttr = FindAttribute(method.AttributeLists, ActionAttributes);
            if (actionAttr == null) continue;

            var methodName = method.Identifier.Text;
            var actionInfo = new ActionInfo
            {
                MethodName = methodName,
                FilePath = filePath,
                ActionId = ExtractNamedArgument(actionAttr, "ActionId"),
                Description = ExtractAttributeArgument(actionAttr, 0) ??
                             ExtractNamedArgument(actionAttr, "Description"),
                RequiresApproval = ParseBoolArgument(actionAttr, "RequiresApproval"),
                Instructions = ExtractNamedArgument(actionAttr, "Instructions"),
                IsMarkedWithAttribute = true
            };

            actions.Add(actionInfo);
        }

        return actions;
    }

    /// <summary>
    /// Checks if a method is marked with [AgentIgnore].
    /// </summary>
    public bool IsMethodIgnored(MethodDeclarationSyntax method)
    {
        return FindAttribute(method.AttributeLists, IgnoreAttributes) != null;
    }

    /// <summary>
    /// Checks if a class is marked with [AgentCapability].
    /// </summary>
    public bool IsCapabilityClass(ClassDeclarationSyntax classDecl)
    {
        return FindAttribute(classDecl.AttributeLists, CapabilityAttributes) != null;
    }

    private static AttributeSyntax? FindAttribute(
        SyntaxList<AttributeListSyntax> attributeLists,
        HashSet<string> attributeNames)
    {
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                // Handle both "AgentAction" and "AgentActionAttribute"
                if (attributeNames.Contains(name))
                    return attr;

                // Also check without namespace
                var lastDot = name.LastIndexOf('.');
                if (lastDot >= 0)
                {
                    var shortName = name[(lastDot + 1)..];
                    if (attributeNames.Contains(shortName))
                        return attr;
                }
            }
        }
        return null;
    }

    private static string? ExtractAttributeArgument(AttributeSyntax attr, int index)
    {
        if (attr.ArgumentList == null) return null;

        var args = attr.ArgumentList.Arguments
            .Where(a => a.NameEquals == null && a.NameColon == null)
            .ToList();

        if (index >= args.Count) return null;

        var expr = args[index].Expression;
        return ExtractStringValue(expr);
    }

    private static string? ExtractNamedArgument(AttributeSyntax attr, string name)
    {
        if (attr.ArgumentList == null) return null;

        var arg = attr.ArgumentList.Arguments
            .FirstOrDefault(a =>
                a.NameEquals?.Name.Identifier.Text == name ||
                a.NameColon?.Name.Identifier.Text == name);

        if (arg == null) return null;

        return ExtractStringValue(arg.Expression);
    }

    private static bool ParseBoolArgument(AttributeSyntax attr, string name)
    {
        var value = ExtractNamedArgument(attr, name);
        return value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? ExtractStringValue(ExpressionSyntax expr)
    {
        return expr switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                => literal.Token.ValueText,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression)
                => "true",
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression)
                => "false",
            InterpolatedStringExpressionSyntax interpolated
                => string.Concat(interpolated.Contents.OfType<InterpolatedStringTextSyntax>().Select(t => t.TextToken.ValueText)),
            _ => expr.ToString().Trim('"')
        };
    }
}

/// <summary>
/// Information about a class marked with [AgentCapability].
/// </summary>
public sealed class CapabilityInfo
{
    public string ClassName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string? CapabilityId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<ActionInfo> Actions { get; init; } = [];
}

/// <summary>
/// Information about a method marked with [AgentAction].
/// </summary>
public sealed class ActionInfo
{
    public string MethodName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string? ActionId { get; init; }
    public string? Description { get; init; }
    public bool RequiresApproval { get; init; }
    public string? Instructions { get; init; }
    public bool IsMarkedWithAttribute { get; init; }
}
