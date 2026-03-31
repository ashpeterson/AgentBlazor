using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis;

/// <summary>
/// Analyzes C# files to extract service registrations and service method signatures.
/// Uses Roslyn semantic model for accurate type resolution.
/// </summary>
public sealed class ServiceAnalyzer
{
    private readonly ActionScorer _actionScorer = new();
    private HashSet<string>? _additionalSuffixes;
    private HashSet<string>? _excludePatterns;
    private HashSet<string>? _excludeDirectories;

    /// <summary>
    /// Configures the analyzer with user-defined patterns from config file.
    /// </summary>
    public void Configure(AgentBlazorConfig config)
    {
        if (config.AdditionalServiceSuffixes?.Count > 0)
        {
            _additionalSuffixes = new HashSet<string>(config.AdditionalServiceSuffixes, StringComparer.OrdinalIgnoreCase);
        }
        if (config.ExcludeServicePatterns?.Count > 0)
        {
            _excludePatterns = new HashSet<string>(config.ExcludeServicePatterns, StringComparer.OrdinalIgnoreCase);
        }
        if (config.ExcludeDirectories?.Count > 0)
        {
            _excludeDirectories = new HashSet<string>(config.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Finds DI service registrations in Program.cs or Startup.cs files.
    /// </summary>
    public async Task<IReadOnlyList<ServiceRegistration>> FindServiceRegistrationsAsync(
        Project project,
        CancellationToken ct = default)
    {
        var registrations = new List<ServiceRegistration>();

        // Look for Program.cs, Startup.cs, or files containing service registration
        var candidateFiles = project.Documents
            .Where(d => d.FilePath != null &&
                (d.Name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                 d.Name.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase) ||
                 d.Name.Contains("ServiceCollection", StringComparison.OrdinalIgnoreCase) ||
                 d.Name.Contains("Registration", StringComparison.OrdinalIgnoreCase)));

        foreach (var document in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
            var semanticModel = await document.GetSemanticModelAsync(ct);

            if (syntaxTree == null || semanticModel == null) continue;

            var root = await syntaxTree.GetRootAsync(ct);
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                var registration = TryExtractServiceRegistration(invocation, semanticModel);
                if (registration != null)
                {
                    registrations.Add(registration);
                }
            }
        }

        return registrations;
    }

    private ServiceRegistration? TryExtractServiceRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Get the method name
        var methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };

        if (methodName == null) return null;

        // Check for AddScoped, AddTransient, AddSingleton patterns
        var lifetime = methodName switch
        {
            "AddScoped" => "Scoped",
            "AddTransient" => "Transient",
            "AddSingleton" => "Singleton",
            _ => null
        };

        if (lifetime == null) return null;

        // Extract type arguments or arguments
        var typeArgs = invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } =>
                genericName.TypeArgumentList.Arguments.ToList(),
            _ => []
        };

        string? serviceType = null;
        string? implementationType = null;

        if (typeArgs.Count >= 1)
        {
            serviceType = typeArgs[0].ToString();
        }
        if (typeArgs.Count >= 2)
        {
            implementationType = typeArgs[1].ToString();
        }

        // If no type arguments, check for factory or instance patterns
        if (serviceType == null && invocation.ArgumentList.Arguments.Count > 0)
        {
            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            if (firstArg is TypeOfExpressionSyntax typeOf)
            {
                serviceType = typeOf.Type.ToString();
            }
        }

        if (serviceType == null) return null;

        return new ServiceRegistration
        {
            ServiceType = serviceType,
            ImplementationType = implementationType ?? serviceType,
            Lifetime = lifetime
        };
    }

    /// <summary>
    /// Analyzes a service class to extract its public methods.
    /// </summary>
    public async Task<ServiceModel?> AnalyzeServiceClassAsync(
        Document document,
        string className,
        SemanticModel semanticModel,
        CancellationToken ct = default)
    {
        var syntaxTree = await document.GetSyntaxTreeAsync(ct);
        if (syntaxTree == null) return null;

        var root = await syntaxTree.GetRootAsync(ct);

        // Find the class declaration
        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);

        if (classDecl == null) return null;

        var methods = new List<ServiceMethodModel>();

        foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            // Only public methods
            if (!method.Modifiers.Any(SyntaxKind.PublicKeyword)) continue;

            var parameters = method.ParameterList.Parameters
                .Where(p => !IsCancellationToken(p))
                .Select(p => ExtractParameterModel(p))
                .ToList();

            var returnType = method.ReturnType.ToString();
            var isAsync = method.Modifiers.Any(SyntaxKind.AsyncKeyword) ||
                         returnType.StartsWith("Task") ||
                         returnType.StartsWith("ValueTask");

            // Extract XML doc summary if present
            var xmlDoc = method.GetLeadingTrivia()
                .Select(t => t.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            string? summary = null;
            if (xmlDoc != null)
            {
                var summaryElement = xmlDoc.Content
                    .OfType<XmlElementSyntax>()
                    .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary");

                if (summaryElement != null)
                {
                    summary = string.Join(" ", summaryElement.Content
                        .OfType<XmlTextSyntax>()
                        .SelectMany(t => t.TextTokens)
                        .Select(t => t.Text.Trim())
                        .Where(t => !string.IsNullOrEmpty(t)));
                }
            }

            methods.Add(new ServiceMethodModel
            {
                Name = method.Identifier.Text,
                ReturnType = returnType,
                IsAsync = isAsync,
                IsPublic = true,
                Parameters = parameters,
                XmlDocSummary = summary
            });
        }

        return new ServiceModel
        {
            Id = GenerateId(className),
            TypeName = className,
            FilePath = document.FilePath ?? "",
            Methods = methods
        };
    }

    /// <summary>
    /// Finds all service-like classes in a project (classes ending with Service, Repository, etc.).
    /// </summary>
    public async Task<IReadOnlyList<ServiceModel>> FindServiceClassesAsync(
        Project project,
        CancellationToken ct = default)
    {
        var services = new List<ServiceModel>();
        var compilation = await project.GetCompilationAsync(ct);
        if (compilation == null) return services;

        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();

            if (document.FilePath == null) continue;

            // Skip excluded directories
            if (_excludeDirectories != null)
            {
                var shouldSkip = false;
                foreach (var excludeDir in _excludeDirectories)
                {
                    if (document.FilePath.Contains(Path.DirectorySeparatorChar + excludeDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        document.FilePath.Contains(Path.AltDirectorySeparatorChar + excludeDir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        shouldSkip = true;
                        break;
                    }
                }
                if (shouldSkip) continue;
            }

            // Skip non-service files based on naming conventions
            var fileName = Path.GetFileNameWithoutExtension(document.FilePath);
            if (!IsLikelyServiceClass(fileName)) continue;

            var syntaxTree = await document.GetSyntaxTreeAsync(ct);
            var semanticModel = compilation.GetSemanticModel(syntaxTree!);
            var root = await syntaxTree!.GetRootAsync(ct);

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                // Skip abstract classes and interfaces
                if (classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword)) continue;

                var className = classDecl.Identifier.Text;
                if (!IsLikelyServiceClass(className)) continue;

                var service = await AnalyzeServiceClassAsync(document, className, semanticModel, ct);
                if (service != null && service.Methods.Count > 0)
                {
                    services.Add(service);
                }
            }
        }

        return services;
    }

    private bool IsLikelyServiceClass(string name)
    {
        // Check user-defined exclusions first
        if (_excludePatterns != null)
        {
            foreach (var pattern in _excludePatterns)
            {
                if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // Check user-defined additional suffixes
        if (_additionalSuffixes != null)
        {
            foreach (var suffix in _additionalSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Common service suffixes in .NET applications
        return name.EndsWith("Service", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Repository", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Manager", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Handler", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Provider", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Workflow", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Capabilities", StringComparison.OrdinalIgnoreCase) ||
               // Additional common patterns
               name.EndsWith("Client", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Gateway", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Facade", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Store", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Cache", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Mediator", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Coordinator", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Orchestrator", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Processor", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Engine", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Helper", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Utility", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Factory", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Builder", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Validator", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Notifier", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Dispatcher", StringComparison.OrdinalIgnoreCase) ||
               // API patterns
               name.EndsWith("Api", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
               // State management
               name.EndsWith("State", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Context", StringComparison.OrdinalIgnoreCase);
    }

    private static ParameterModel ExtractParameterModel(ParameterSyntax p)
    {
        var agentParamAttr = FindAttribute(p.AttributeLists, "AgentParam");

        string? description = null;
        bool isContextBound = false;
        bool isRequired = false;
        string? allowedValues = null;

        if (agentParamAttr != null)
        {
            // Extract [AgentParam] properties
            description = ExtractAttributeArgument(agentParamAttr, 0) ??
                         ExtractNamedArgument(agentParamAttr, "Description");
            isContextBound = ExtractNamedArgument(agentParamAttr, "ContextKey") != null;
            isRequired = ExtractNamedArgument(agentParamAttr, "Required")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            allowedValues = ExtractNamedArgument(agentParamAttr, "AllowedValues");
        }

        return new ParameterModel
        {
            Name = p.Identifier.Text,
            TypeName = p.Type?.ToString() ?? "object",
            IsOptional = p.Default != null,
            DefaultValue = p.Default?.Value.ToString(),
            Description = description,
            IsContextBound = isContextBound,
            IsRequired = isRequired,
            AllowedValues = allowedValues
        };
    }

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name == attributeName || name == attributeName + "Attribute")
                    return attr;
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
        return ExtractStringValue(args[index].Expression);
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
            _ => expr.ToString().Trim('"')
        };
    }

    private static bool IsCancellationToken(ParameterSyntax parameter)
    {
        var typeName = parameter.Type?.ToString() ?? "";
        return typeName == "CancellationToken" ||
               typeName.EndsWith(".CancellationToken");
    }

    private static string GenerateId(string name)
    {
        // Convert PascalCase to snake_case
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}

public sealed class ServiceRegistration
{
    public string ServiceType { get; init; } = "";
    public string ImplementationType { get; init; } = "";
    public string Lifetime { get; init; } = "Scoped";
}
