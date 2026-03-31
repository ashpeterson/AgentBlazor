using System.Text.RegularExpressions;
using AgentBlazor.Cli.Analysis.Generation;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests.Generation;

public partial class MarkdownGeneratorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MarkdownGenerator _generator;

    public MarkdownGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agentblazor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _generator = new MarkdownGenerator();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ProjectModel CreateMinimalModel(string appName = "TestApp") => new()
    {
        AppName = appName,
        Description = "Test application",
        GeneratedAtUtc = DateTime.UtcNow,
        Routes = [],
        Actions = [],
        Services = []
    };

    private string GetAgentMdPath() => Path.Combine(_tempDir, ".agentblazor", "AGENT.md");

    /// <summary>
    /// Replaces a section's content in the markdown file.
    /// </summary>
    private static string ReplaceSectionContent(string markdown, string sectionName, string newContent)
    {
        // Match the section header and everything until the next ## or ---
        var pattern = $@"(## {Regex.Escape(sectionName)}\r?\n\r?\n)[\s\S]*?(?=\r?\n## |\r?\n---)";
        return Regex.Replace(markdown, pattern, $"$1{newContent}\n");
    }

    /// <summary>
    /// Inserts a custom section before the footer.
    /// </summary>
    private static string InsertSectionBeforeFooter(string markdown, string sectionName, string content)
    {
        var footerPattern = @"(\r?\n---\r?\n)";
        var newSection = $"\n## {sectionName}\n\n{content}\n";
        return Regex.Replace(markdown, footerPattern, newSection + "$1");
    }

    #region Initial Generation Tests

    [Fact]
    public async Task GenerateAsync_CreatesAgentMd_WhenFileDoesNotExist()
    {
        // Arrange
        var model = CreateMinimalModel();

        // Act
        await _generator.GenerateAsync(model, _tempDir);

        // Assert
        Assert.True(File.Exists(GetAgentMdPath()));
    }

    [Fact]
    public async Task GenerateAsync_IncludesPlaceholders_OnInitialGeneration()
    {
        // Arrange
        var model = CreateMinimalModel();

        // Act
        await _generator.GenerateAsync(model, _tempDir);
        var content = await File.ReadAllTextAsync(GetAgentMdPath());

        // Assert
        Assert.Contains("## Agent Instructions", content);
        Assert.Contains("<!-- Add guidelines for agents", content);
        Assert.Contains("## Priority Workflows", content);
        Assert.Contains("<!-- Document key business flows", content);
    }

    #endregion

    #region Section Preservation Tests

    [Fact]
    public async Task GenerateAsync_PreservesAgentInstructions_WhenEdited()
    {
        // Arrange
        var model = CreateMinimalModel();
        await _generator.GenerateAsync(model, _tempDir);

        // Simulate developer editing
        var agentMdPath = GetAgentMdPath();
        var content = await File.ReadAllTextAsync(agentMdPath);
        var editedContent = ReplaceSectionContent(content, "Agent Instructions",
            "- Always use recovery playbooks first\n- Never reset workflows without confirmation");
        await File.WriteAllTextAsync(agentMdPath, editedContent);

        // Act - regenerate
        var newModel = CreateMinimalModel();
        newModel = newModel with { GeneratedAtUtc = DateTime.UtcNow.AddMinutes(1) };
        await _generator.GenerateAsync(newModel, _tempDir);

        // Assert
        var finalContent = await File.ReadAllTextAsync(agentMdPath);
        Assert.Contains("Always use recovery playbooks first", finalContent);
        Assert.Contains("Never reset workflows without confirmation", finalContent);
        Assert.DoesNotContain("<!-- Add guidelines for agents", finalContent);
    }

    [Fact]
    public async Task GenerateAsync_PreservesPriorityWorkflows_WhenEdited()
    {
        // Arrange
        var model = CreateMinimalModel();
        await _generator.GenerateAsync(model, _tempDir);

        // Simulate developer editing by directly writing a modified file
        var agentMdPath = GetAgentMdPath();
        var content = await File.ReadAllTextAsync(agentMdPath);

        // Manually replace the placeholder with custom content
        // The placeholder spans multiple lines, so we need to match it properly
        var placeholderStart = content.IndexOf("<!-- Document key business flows", StringComparison.Ordinal);
        var placeholderEnd = content.IndexOf("-->", placeholderStart, StringComparison.Ordinal) + 3;

        var customContent = "1. **Custom Flow**: Step A -> Step B -> Step C";
        var editedContent = content[..placeholderStart] + customContent + content[placeholderEnd..];

        await File.WriteAllTextAsync(agentMdPath, editedContent);

        // Verify the edit was applied
        var savedContent = await File.ReadAllTextAsync(agentMdPath);
        Assert.Contains("**Custom Flow**", savedContent);
        Assert.DoesNotContain("<!-- Document key business flows", savedContent);

        // Act - regenerate
        await _generator.GenerateAsync(model, _tempDir);

        // Assert
        var finalContent = await File.ReadAllTextAsync(agentMdPath);
        Assert.Contains("**Custom Flow**", finalContent);
        Assert.DoesNotContain("<!-- Document key business flows", finalContent);
    }

    [Fact]
    public async Task GenerateAsync_PreservesCustomSections_WhenAdded()
    {
        // Arrange
        var model = CreateMinimalModel();
        await _generator.GenerateAsync(model, _tempDir);

        // Simulate developer adding custom section
        var agentMdPath = GetAgentMdPath();
        var content = await File.ReadAllTextAsync(agentMdPath);
        var editedContent = InsertSectionBeforeFooter(content, "Custom Domain Notes",
            "This section contains domain-specific information.\n\n- Item 1\n- Item 2");
        await File.WriteAllTextAsync(agentMdPath, editedContent);

        // Act - regenerate
        await _generator.GenerateAsync(model, _tempDir);

        // Assert
        var finalContent = await File.ReadAllTextAsync(agentMdPath);
        Assert.Contains("## Custom Domain Notes", finalContent);
        Assert.Contains("domain-specific information", finalContent);
    }

    [Fact]
    public async Task GenerateAsync_RegeneratesAutoSections_OnUpdate()
    {
        // Arrange
        var model = CreateMinimalModel();
        model = model with
        {
            Routes =
            [
                new RouteModel { Template = "/initial", ComponentName = "Initial", ComponentFile = "Initial.razor" }
            ]
        };
        await _generator.GenerateAsync(model, _tempDir);

        // Act - regenerate with different routes
        var newModel = model with
        {
            Routes =
            [
                new RouteModel { Template = "/updated", ComponentName = "Updated", ComponentFile = "Updated.razor" }
            ]
        };
        await _generator.GenerateAsync(newModel, _tempDir);

        // Assert
        var content = await File.ReadAllTextAsync(GetAgentMdPath());
        Assert.Contains("/updated", content);
        Assert.DoesNotContain("/initial", content);
    }

    [Fact]
    public async Task GenerateAsync_UpdatesTimestamp_OnRegeneration()
    {
        // Arrange
        var model = CreateMinimalModel();
        model = model with { GeneratedAtUtc = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        await _generator.GenerateAsync(model, _tempDir);

        var initialContent = await File.ReadAllTextAsync(GetAgentMdPath());
        Assert.Contains("2024-01-01 12:00:00 UTC", initialContent);

        // Act - regenerate with new timestamp
        var newModel = model with { GeneratedAtUtc = new DateTime(2024, 6, 15, 18, 30, 0, DateTimeKind.Utc) };
        await _generator.GenerateAsync(newModel, _tempDir);

        // Assert
        var finalContent = await File.ReadAllTextAsync(GetAgentMdPath());
        Assert.Contains("2024-06-15 18:30:00 UTC", finalContent);
        Assert.DoesNotContain("2024-01-01", finalContent);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GenerateAsync_HandlesEmptyDeveloperSections_Gracefully()
    {
        // Arrange
        var model = CreateMinimalModel();
        await _generator.GenerateAsync(model, _tempDir);

        // Simulate developer clearing section content entirely
        var agentMdPath = GetAgentMdPath();
        var content = await File.ReadAllTextAsync(agentMdPath);
        var editedContent = ReplaceSectionContent(content, "Agent Instructions", "");
        await File.WriteAllTextAsync(agentMdPath, editedContent);

        // Act - regenerate
        await _generator.GenerateAsync(model, _tempDir);

        // Assert - should restore placeholder since section is empty
        var finalContent = await File.ReadAllTextAsync(GetAgentMdPath());
        Assert.Contains("<!-- Add guidelines for agents", finalContent);
    }

    [Fact]
    public async Task GenerateAsync_PreservesMultipleCustomSections()
    {
        // Arrange
        var model = CreateMinimalModel();
        await _generator.GenerateAsync(model, _tempDir);

        // Simulate developer adding multiple custom sections
        var agentMdPath = GetAgentMdPath();
        var content = await File.ReadAllTextAsync(agentMdPath);
        var editedContent = InsertSectionBeforeFooter(content, "API Guidelines",
            "Use REST conventions for all endpoints.");
        editedContent = InsertSectionBeforeFooter(editedContent, "Testing Notes",
            "Run integration tests before deployment.");
        await File.WriteAllTextAsync(agentMdPath, editedContent);

        // Act - regenerate
        await _generator.GenerateAsync(model, _tempDir);

        // Assert
        var finalContent = await File.ReadAllTextAsync(GetAgentMdPath());
        Assert.Contains("## API Guidelines", finalContent);
        Assert.Contains("REST conventions", finalContent);
        Assert.Contains("## Testing Notes", finalContent);
        Assert.Contains("integration tests", finalContent);
    }

    #endregion
}
