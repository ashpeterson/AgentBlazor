using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Analyzes Program.cs and Startup.cs to detect DI service registrations.
/// This enables discovery of services without relying on naming conventions.
/// </summary>
public sealed class DiRegistrationAnalyzer
{
    // Common DI registration method names
    private static readonly HashSet<string> RegistrationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "AddScoped", "AddSingleton", "AddTransient",
        "AddKeyedScoped", "AddKeyedSingleton", "AddKeyedTransient",
        "TryAddScoped", "TryAddSingleton", "TryAddTransient"
    };

    /// <summary>
    /// Finds all DI service registrations in a project.
    /// </summary>
    public async Task<IReadOnlyList<DiRegistrationModel>> FindRegistrationsAsync(
        Project project,
        CancellationToken ct = default)
    {
        var registrations = new List<DiRegistrationModel>();

        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(document.FilePath ?? "");

            // Focus on files likely to contain DI registrations
            if (!IsLikelyRegistrationFile(fileName))
                continue;

            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
            if (syntaxTree == null) continue;

            var root = await syntaxTree.GetRootAsync(ct);
            var semanticModel = await document.GetSemanticModelAsync(ct);
            if (semanticModel == null) continue;

            var fileRegistrations = AnalyzeFile(root, semanticModel, document.FilePath ?? "");
            registrations.AddRange(fileRegistrations);
        }

        return registrations;
    }

    private static bool IsLikelyRegistrationFile(string fileName)
    {
        return fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("ServiceCollection", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Extensions", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("Registration", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DiRegistrationModel> AnalyzeFile(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath)
    {
        var registrations = new List<DiRegistrationModel>();

        // Find all method invocations
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var registration = TryParseRegistration(invocation, semanticModel, filePath);
            if (registration != null)
            {
                registrations.Add(registration);
            }
        }

        return registrations;
    }

    private static DiRegistrationModel? TryParseRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string filePath)
    {
        // Get the method name
        string? methodName = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
        }
        else if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            methodName = identifier.Identifier.Text;
        }

        if (methodName == null || !RegistrationMethods.Contains(methodName))
            return null;

        // Parse the registration pattern
        var lifetime = ParseLifetime(methodName);
        string? serviceType = null;
        string? implementationType = null;

        // Check for generic type arguments: AddScoped<IService, Implementation>()
        if (invocation.Expression is MemberAccessExpressionSyntax ma &&
            ma.Name is GenericNameSyntax genericName)
        {
            var typeArgs = genericName.TypeArgumentList.Arguments;

            if (typeArgs.Count == 1)
            {
                // AddScoped<ServiceType>() - service is its own implementation
                serviceType = typeArgs[0].ToString();
                implementationType = serviceType;
            }
            else if (typeArgs.Count == 2)
            {
                // AddScoped<IService, Implementation>()
                serviceType = typeArgs[0].ToString();
                implementationType = typeArgs[1].ToString();
            }
        }

        // Check for argument-based registration: AddScoped(typeof(IService), typeof(Implementation))
        if (serviceType == null && invocation.ArgumentList.Arguments.Count >= 1)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;

            if (firstArg is TypeOfExpressionSyntax typeOfExpr)
            {
                serviceType = typeOfExpr.Type.ToString();

                if (invocation.ArgumentList.Arguments.Count >= 2)
                {
                    var secondArg = invocation.ArgumentList.Arguments[1].Expression;
                    if (secondArg is TypeOfExpressionSyntax implTypeOf)
                    {
                        implementationType = implTypeOf.Type.ToString();
                    }
                }
                else
                {
                    implementationType = serviceType;
                }
            }
        }

        if (serviceType == null)
            return null;

        var lineSpan = invocation.GetLocation().GetLineSpan();

        return new DiRegistrationModel
        {
            ServiceType = CleanTypeName(serviceType),
            ImplementationType = CleanTypeName(implementationType ?? serviceType),
            Lifetime = lifetime,
            FilePath = filePath,
            LineNumber = lineSpan.StartLinePosition.Line + 1
        };
    }

    private static ServiceLifetime ParseLifetime(string methodName)
    {
        if (methodName.Contains("Singleton", StringComparison.OrdinalIgnoreCase))
            return ServiceLifetime.Singleton;
        if (methodName.Contains("Transient", StringComparison.OrdinalIgnoreCase))
            return ServiceLifetime.Transient;
        return ServiceLifetime.Scoped;
    }

    private static string CleanTypeName(string typeName)
    {
        // Remove generic arity markers and simplify
        return typeName
            .Replace("global::", "")
            .Trim();
    }
}
