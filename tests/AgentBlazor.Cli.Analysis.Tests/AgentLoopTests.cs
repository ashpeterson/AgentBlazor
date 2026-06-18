namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class AgentLoopTests
{
    [Fact]
    public async Task ReadFileAsync_ReadsOnlyInsideSolutionRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "Program.cs");
            await File.WriteAllTextAsync(filePath, "Console.WriteLine(\"hello\");");

            var loop = new AgentLoop(root);
            var read = await loop.ReadFileAsync("Program.cs");

            Assert.Equal("Program.cs", read.RelativePath);
            Assert.Contains("hello", read.Content);
            await Assert.ThrowsAsync<InvalidOperationException>(() => loop.ReadFileAsync("../outside.txt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProposePatch_RendersPreviewAndDoesNotWriteBeforeApproval()
    {
        var root = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(root, "Services", "Orders.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, "old");

            var loop = new AgentLoop(root);
            var proposal = loop.ProposePatch(
                "Update order workflow",
                [
                    new AgentProposedFileChange
                    {
                        Path = "Services/Orders.cs",
                        OriginalContent = "old",
                        UpdatedContent = "new"
                    }
                ]);

            var diff = loop.RenderDiff(proposal);
            var preview = loop.ToPreview(proposal);

            Assert.Equal("old", await File.ReadAllTextAsync(filePath));
            Assert.Contains("--- Services/Orders.cs", diff);
            Assert.Contains("-old", diff);
            Assert.Contains("+new", diff);
            var change = Assert.Single(preview.Changes);
            Assert.Equal(filePath, change.Path);
            Assert.Equal("Update order workflow", change.Summary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyApprovedPatchAsync_WritesOnlyAfterApprovalAndRecordsTranscript()
    {
        var root = CreateTempDirectory();
        try
        {
            var loop = new AgentLoop(root);
            var proposal = loop.ProposePatch(
                "Create workflow notes",
                [
                    new AgentProposedFileChange
                    {
                        Path = ".agentblazor/notes.md",
                        UpdatedContent = "# Notes"
                    }
                ]);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                loop.ApplyApprovedPatchAsync(proposal.Id, ""));

            var result = await loop.ApplyApprovedPatchAsync(proposal.Id, "Riley Reviewer");

            Assert.Equal("Riley Reviewer", result.ApprovedBy);
            Assert.Equal("# Notes", await File.ReadAllTextAsync(Path.Combine(root, ".agentblazor", "notes.md")));
            Assert.Contains(loop.Transcript, item => item.Tool == "propose_patch");
            Assert.Contains(loop.Transcript, item => item.Tool == "apply_approved_patch");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAuditAsync_WritesRelativeAuditRecord()
    {
        var root = CreateTempDirectory();
        try
        {
            var loop = new AgentLoop(root);
            var proposal = loop.ProposePatch(
                "Create workflow notes",
                [
                    new AgentProposedFileChange
                    {
                        Path = ".agentblazor/notes.md",
                        UpdatedContent = "# Notes"
                    }
                ]);
            _ = loop.ToPreview(proposal);
            var result = await loop.ApplyApprovedPatchAsync(proposal.Id, "Riley Reviewer");

            var audit = await loop.WriteAuditAsync(
                "workflow-onboarding",
                "Riley Reviewer",
                proposal,
                result,
                new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));

            Assert.Equal(".agentblazor/audit/workflow-onboarding-20260618120000.json", audit.Path);
            var auditJson = await File.ReadAllTextAsync(Path.Combine(root, audit.Path));
            Assert.Contains("\"kind\": \"workflow-onboarding\"", auditJson);
            Assert.Contains("\"reviewedBy\": \"Riley Reviewer\"", auditJson);
            Assert.Contains("\"proposedFiles\"", auditJson);
            Assert.Contains(".agentblazor/notes.md", auditJson);
            Assert.Contains("apply_approved_patch", auditJson);
            Assert.DoesNotContain(root, auditJson);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProposePatch_RejectsOutsideRootAndStaleOriginalContent()
    {
        var root = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "existing.txt"), "current");
            var loop = new AgentLoop(root);

            Assert.Throws<InvalidOperationException>(() => loop.ProposePatch(
                "Outside root",
                [
                    new AgentProposedFileChange
                    {
                        Path = "../outside.txt",
                        UpdatedContent = "bad"
                    }
                ]));

            Assert.Throws<InvalidOperationException>(() => loop.ProposePatch(
                "Stale patch",
                [
                    new AgentProposedFileChange
                    {
                        Path = "existing.txt",
                        OriginalContent = "old",
                        UpdatedContent = "new"
                    }
                ]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentblazor-agent-loop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
