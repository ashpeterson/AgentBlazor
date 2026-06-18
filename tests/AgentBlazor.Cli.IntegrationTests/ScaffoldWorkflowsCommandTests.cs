using System.Diagnostics;
using System.Text.Json;

namespace AgentBlazor.Cli.IntegrationTests;

public sealed class ScaffoldWorkflowsCommandTests
{
    [Fact]
    public async Task ScaffoldWorkflowsApproveNonInteractive_WithoutWorkflowSelection_RefusesApply()
    {
        var repoRoot = FindRepoRoot();
        var tempDir = CreateTempDirectory();
        try
        {
            CopyDirectory(
                Path.Combine(repoRoot, "tests", "cli-targets", "realistic-blazor-app"),
                tempDir);

            var result = await RunCliAsync(
                repoRoot,
                "scaffold",
                "workflows",
                Path.Combine(tempDir, "RealisticBlazorApp.csproj"),
                "--approve",
                "--non-interactive");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("requires --workflow", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(tempDir, ".agentblazor", "SOUL.md")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldWorkflowsApproveNonInteractive_WithWorkflowSelection_WritesSoulAndSkillArtifacts()
    {
        var repoRoot = FindRepoRoot();
        var tempDir = CreateTempDirectory();
        try
        {
            CopyDirectory(
                Path.Combine(repoRoot, "tests", "cli-targets", "realistic-blazor-app"),
                tempDir);

            var result = await RunCliAsync(
                repoRoot,
                "scaffold",
                "workflows",
                Path.Combine(tempDir, "RealisticBlazorApp.csproj"),
                "--workflow",
                "same-service-lifecycle-inventory-pipeline",
                "--description",
                "Inventory operations app for warehouse managers.",
                "--agent-goals",
                "approve supplier transfers;prepare restock plans",
                "--save-config",
                "--reviewed-by",
                "Riley Reviewer",
                "--approve",
                "--non-interactive");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Workflow artifacts applied", result.Output, StringComparison.OrdinalIgnoreCase);

            var agentDir = Path.Combine(tempDir, ".agentblazor");
            var skillDir = Path.Combine(agentDir, "skills", "inventory-pipeline");
            var reviewJsonPath = Path.Combine(agentDir, "workflow-onboarding.json");
            var reviewMarkdownPath = Path.Combine(agentDir, "workflow-onboarding.md");
            var reviewHtmlPath = Path.Combine(agentDir, "workflow-onboarding.html");
            var soulPath = Path.Combine(agentDir, "SOUL.md");
            var indexPath = Path.Combine(agentDir, "skills", "index.json");
            var metadataPath = Path.Combine(agentDir, "skills", ".metadata.json");
            var skillPath = Path.Combine(skillDir, "SKILL.md");
            var evidencePath = Path.Combine(skillDir, "references", "evidence.md");
            var configPath = Path.Combine(tempDir, ".agentblazorc");
            var auditDir = Path.Combine(agentDir, "audit");

            Assert.True(File.Exists(reviewJsonPath));
            Assert.True(File.Exists(reviewMarkdownPath));
            Assert.True(File.Exists(reviewHtmlPath));
            Assert.True(File.Exists(soulPath));
            Assert.True(File.Exists(indexPath));
            Assert.True(File.Exists(metadataPath));
            Assert.True(File.Exists(skillPath));
            Assert.True(File.Exists(evidencePath));
            Assert.True(File.Exists(configPath));
            var auditPath = Assert.Single(Directory.GetFiles(auditDir, "workflow-onboarding-*.json"));

            Assert.Contains("Inventory Pipeline", await File.ReadAllTextAsync(soulPath));
            Assert.Contains("Inventory operations app for warehouse managers.", await File.ReadAllTextAsync(soulPath));
            Assert.Contains("approve supplier transfers", await File.ReadAllTextAsync(soulPath));
            Assert.Contains("Workflow Onboarding Review", await File.ReadAllTextAsync(reviewMarkdownPath));
            var reviewMarkdown = await File.ReadAllTextAsync(reviewMarkdownPath);
            var reviewHtml = await File.ReadAllTextAsync(reviewHtmlPath);
            Assert.Contains("Reviewed by: Riley Reviewer", reviewMarkdown);
            Assert.Contains("Inventory Pipeline", reviewHtml);
            Assert.Contains("Reviewed by: Riley Reviewer", reviewHtml);
            var reviewJson = await File.ReadAllTextAsync(reviewJsonPath);
            Assert.Contains("\"reviewedBy\": \"Riley Reviewer\"", reviewJson);
            Assert.Contains("\"approvedCandidateIds\"", reviewJson);
            Assert.Contains("\"status\": \"approved\"", reviewJson);
            Assert.Contains("Services/InventoryWorkflowService.cs", reviewJson);
            Assert.DoesNotContain(tempDir, reviewJson);
            Assert.Contains("requiredApprovals", await File.ReadAllTextAsync(skillPath));
            Assert.Contains("InventoryWorkflowService.PrepareRestockPlanAsync", await File.ReadAllTextAsync(evidencePath));
            var auditJson = await File.ReadAllTextAsync(auditPath);
            Assert.Contains("\"kind\": \"workflow-onboarding\"", auditJson);
            Assert.Contains("\"reviewedBy\": \"Riley Reviewer\"", auditJson);
            Assert.Contains("propose_patch", auditJson);
            Assert.Contains("apply_approved_patch", auditJson);
            Assert.DoesNotContain(tempDir, auditJson);

            using var index = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
            var skill = Assert.Single(index.RootElement.GetProperty("skills").EnumerateArray());
            Assert.Equal("inventory-pipeline", skill.GetProperty("name").GetString());
            Assert.Equal("inventory-pipeline/SKILL.md", skill.GetProperty("skillPath").GetString());

            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            Assert.Equal("Inventory operations app for warehouse managers.", config.RootElement.GetProperty("description").GetString());
            Assert.Contains(config.RootElement.GetProperty("desiredAgentWorkflows").EnumerateArray(), goal =>
                goal.GetString() == "prepare restock plans");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldWorkflowsApproveNonInteractive_WithReviewActions_UpdatesReviewOnly()
    {
        var repoRoot = FindRepoRoot();
        var tempDir = CreateTempDirectory();
        try
        {
            CopyDirectory(
                Path.Combine(repoRoot, "tests", "cli-targets", "realistic-blazor-app"),
                tempDir);

            var result = await RunCliAsync(
                repoRoot,
                "scaffold",
                "workflows",
                Path.Combine(tempDir, "RealisticBlazorApp.csproj"),
                "--reject",
                "existing-support-queue-capabilities",
                "--pin",
                "existing-support-queue-capabilities",
                "--reviewed-by",
                "Riley Reviewer",
                "--approve",
                "--non-interactive");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Workflow artifacts applied", result.Output, StringComparison.OrdinalIgnoreCase);

            var agentDir = Path.Combine(tempDir, ".agentblazor");
            var reviewJsonPath = Path.Combine(agentDir, "workflow-onboarding.json");
            var reviewMarkdownPath = Path.Combine(agentDir, "workflow-onboarding.md");
            var reviewHtmlPath = Path.Combine(agentDir, "workflow-onboarding.html");
            var soulPath = Path.Combine(agentDir, "SOUL.md");
            var auditDir = Path.Combine(agentDir, "audit");

            Assert.True(File.Exists(reviewJsonPath));
            Assert.True(File.Exists(reviewMarkdownPath));
            Assert.True(File.Exists(reviewHtmlPath));
            Assert.False(File.Exists(soulPath));
            var auditPath = Assert.Single(Directory.GetFiles(auditDir, "workflow-onboarding-*.json"));

            var reviewJson = await File.ReadAllTextAsync(reviewJsonPath);
            Assert.Contains("\"reviewedBy\": \"Riley Reviewer\"", reviewJson);
            Assert.Contains("\"id\": \"existing-support-queue-capabilities\"", reviewJson);
            Assert.Contains("\"status\": \"rejected\"", reviewJson);
            Assert.Contains("\"pinned\": true", reviewJson);
            Assert.Contains("rejected, pinned", await File.ReadAllTextAsync(reviewMarkdownPath));
            Assert.Contains("rejected, pinned", await File.ReadAllTextAsync(reviewHtmlPath));
            Assert.Contains("existing-support-queue-capabilities", await File.ReadAllTextAsync(auditPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldWorkflowsApplyApproved_UsesExistingReviewArtifact()
    {
        var repoRoot = FindRepoRoot();
        var tempDir = CreateTempDirectory();
        try
        {
            CopyDirectory(
                Path.Combine(repoRoot, "tests", "cli-targets", "realistic-blazor-app"),
                tempDir);
            var agentDir = Path.Combine(tempDir, ".agentblazor");
            Directory.CreateDirectory(agentDir);
            await File.WriteAllTextAsync(Path.Combine(agentDir, "workflow-onboarding.json"), """
            {
              "schemaVersion": 1,
              "candidates": [
                {
                  "id": "same-service-lifecycle-inventory-pipeline",
                  "slug": "inventory-pipeline",
                  "status": "approved",
                  "pinned": true
                }
              ]
            }
            """);

            var result = await RunCliAsync(
                repoRoot,
                "scaffold",
                "workflows",
                Path.Combine(tempDir, "RealisticBlazorApp.csproj"),
                "--apply-approved",
                "--reviewed-by",
                "Riley Reviewer",
                "--approve",
                "--non-interactive");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Workflow artifacts applied", result.Output, StringComparison.OrdinalIgnoreCase);

            var soulPath = Path.Combine(agentDir, "SOUL.md");
            var skillPath = Path.Combine(agentDir, "skills", "inventory-pipeline", "SKILL.md");
            var reviewJson = await File.ReadAllTextAsync(Path.Combine(agentDir, "workflow-onboarding.json"));

            Assert.True(File.Exists(soulPath));
            Assert.True(File.Exists(skillPath));
            Assert.Contains("Inventory Pipeline", await File.ReadAllTextAsync(soulPath));
            Assert.Contains("\"status\": \"approved\"", reviewJson);
            Assert.Contains("\"pinned\": true", reviewJson);
            Assert.Contains("\"reviewedBy\": \"Riley Reviewer\"", reviewJson);
            Assert.Single(Directory.GetFiles(Path.Combine(agentDir, "audit"), "workflow-onboarding-*.json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ScaffoldWorkflowsApproveNonInteractive_WithWorkflowSelectionWithoutReviewer_RefusesApply()
    {
        var repoRoot = FindRepoRoot();
        var tempDir = CreateTempDirectory();
        try
        {
            CopyDirectory(
                Path.Combine(repoRoot, "tests", "cli-targets", "realistic-blazor-app"),
                tempDir);

            var result = await RunCliAsync(
                repoRoot,
                "scaffold",
                "workflows",
                Path.Combine(tempDir, "RealisticBlazorApp.csproj"),
                "--workflow",
                "same-service-lifecycle-inventory-pipeline",
                "--approve",
                "--non-interactive");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("requires --reviewed-by", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(tempDir, ".agentblazor", "SOUL.md")));
            Assert.False(Directory.Exists(Path.Combine(tempDir, ".agentblazor", "audit")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<CliResult> RunCliAsync(string repoRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(repoRoot, "src", "AgentBlazor.Cli", "AgentBlazor.Cli.csproj"));
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start CLI process.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(90)));
        if (completed != exitTask)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("CLI process did not exit within 90 seconds.");
        }

        var output = await outputTask;
        var error = await errorTask;
        return new CliResult(process.ExitCode, output + error);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentBlazor.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find AgentBlazor repository root.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentblazor-cli-workflows-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            File.Copy(file, destination, overwrite: true);
        }
    }

    private sealed record CliResult(int ExitCode, string Output);
}
