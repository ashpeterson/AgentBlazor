using System.Text.Json;
using AgentBlazor.Cli.Analysis.Models;

namespace AgentBlazor.Cli.Analysis.Tests;

public sealed class WorkflowOnboardingTests
{
    [Fact]
    public void CorpusBuilder_CreatesRetrievableWorkflowEvidence()
    {
        var model = CreateWorkflowModel();
        var corpus = new AnalysisCorpusBuilder().Build(model);
        var results = new LexicalAnalysisRetrieval().Search(corpus, "revision promote package", maxResults: 3);

        Assert.Contains(corpus.Chunks, chunk => chunk.Kind == AnalysisCorpusChunkKind.WorkflowCluster);
        Assert.Contains(results, result => result.Chunk.Title == "Revision Submission Pipeline");
        Assert.Contains("RevisionSubmissionService.PromoteAsync", corpus.Chunks.SelectMany(chunk => chunk.RelatedMethods));
    }

    [Fact]
    public void ArtifactWriter_GeneratesDeterministicSoulAndSkillFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var model = CreateWorkflowModel() with
            {
                Corpus = new AnalysisCorpusBuilder().Build(CreateWorkflowModel())
            };
            var plan = new WorkflowOnboardingPlanner().Plan(model, root);
            var selected = Assert.Single(plan.Candidates);
            var writer = new WorkflowOnboardingArtifactWriter();
            var first = writer.Preview(plan, [selected], new DateOnly(2026, 6, 12));
            var second = writer.Preview(plan, [selected], new DateOnly(2026, 6, 12));

            Assert.Equal(
                first.Select(change => (change.Path, change.UpdatedContent)),
                second.Select(change => (change.Path, change.UpdatedContent)));
            Assert.Contains(first, change => change.Path.EndsWith(Path.Combine(".agentblazor", "SOUL.md"), StringComparison.Ordinal));
            Assert.Contains("help release managers promote revisions", first.Single(change => change.Path.EndsWith("SOUL.md", StringComparison.Ordinal)).UpdatedContent);
            Assert.Contains(first, change => change.Path.EndsWith(Path.Combine(".agentblazor", "workflow-onboarding.json"), StringComparison.Ordinal));
            Assert.Contains(first, change => change.Path.EndsWith(Path.Combine(".agentblazor", "workflow-onboarding.md"), StringComparison.Ordinal));
            Assert.Contains(first, change => change.Path.EndsWith(Path.Combine(".agentblazor", "workflow-onboarding.html"), StringComparison.Ordinal));
            Assert.Contains("\"status\": \"approved\"", first.Single(change => change.Path.EndsWith("workflow-onboarding.json", StringComparison.Ordinal)).UpdatedContent);
            Assert.Contains("AgentBlazor Workflow Onboarding Review", first.Single(change => change.Path.EndsWith("workflow-onboarding.html", StringComparison.Ordinal)).UpdatedContent);
            Assert.Contains(first, change => change.Path.EndsWith(Path.Combine("skills", selected.Slug, "SKILL.md"), StringComparison.Ordinal));
            Assert.Contains("requiredApprovals", first.Single(change => change.Path.EndsWith("SKILL.md", StringComparison.Ordinal)).UpdatedContent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArtifactWriter_WithNoSelectedWorkflow_GeneratesOnlyReviewArtifacts()
    {
        var root = CreateTempDirectory();
        try
        {
            var model = CreateWorkflowModel();
            var plan = new WorkflowOnboardingPlanner().Plan(model, root);
            var changes = new WorkflowOnboardingArtifactWriter()
                .Preview(plan, [], new DateOnly(2026, 6, 12));

            Assert.Equal(3, changes.Count);
            Assert.Contains(changes, change => change.Path.EndsWith(Path.Combine(".agentblazor", "workflow-onboarding.json"), StringComparison.Ordinal));
            Assert.Contains(changes, change => change.Path.EndsWith(Path.Combine(".agentblazor", "workflow-onboarding.md"), StringComparison.Ordinal));
            Assert.Contains(changes, change => change.Path.EndsWith(Path.Combine(".agentblazor", "workflow-onboarding.html"), StringComparison.Ordinal));
            Assert.Contains("\"status\": \"proposed\"", changes.Single(change => change.Path.EndsWith("workflow-onboarding.json", StringComparison.Ordinal)).UpdatedContent);
            Assert.Contains("Require Approval", changes.Single(change => change.Path.EndsWith("workflow-onboarding.html", StringComparison.Ordinal)).UpdatedContent);
            Assert.DoesNotContain(changes, change => change.Path.EndsWith("SOUL.md", StringComparison.Ordinal));
            Assert.DoesNotContain(changes, change => change.Path.EndsWith("SKILL.md", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ArtifactWriter_PreservesAndAppliesReviewDecisions()
    {
        var root = CreateTempDirectory();
        try
        {
            var model = CreateWorkflowModel();
            var plan = new WorkflowOnboardingPlanner().Plan(model, root);
            var writer = new WorkflowOnboardingArtifactWriter();
            var decisions = new WorkflowReviewDecisions
            {
                RejectedIds = new HashSet<string>(["revision-submission-pipeline"], StringComparer.OrdinalIgnoreCase),
                PinnedIds = new HashSet<string>(["revision-submission-pipeline"], StringComparer.OrdinalIgnoreCase),
                ReviewedBy = "Riley Reviewer"
            };

            var changes = writer.Preview(plan, [], new DateOnly(2026, 6, 12), decisions);
            var review = changes.Single(change => change.Path.EndsWith("workflow-onboarding.json", StringComparison.Ordinal)).UpdatedContent;

            Assert.Contains("\"reviewedBy\": \"Riley Reviewer\"", review);
            Assert.Contains("\"rejectedCandidateIds\"", review);
            Assert.Contains("\"status\": \"rejected\"", review);
            Assert.Contains("\"pinned\": true", review);
            Assert.Contains("Reviewed by: Riley Reviewer", changes.Single(change => change.Path.EndsWith("workflow-onboarding.md", StringComparison.Ordinal)).UpdatedContent);
            Assert.Contains("Reviewed by: Riley Reviewer", changes.Single(change => change.Path.EndsWith("workflow-onboarding.html", StringComparison.Ordinal)).UpdatedContent);
            Assert.Contains("rejected, pinned", changes.Single(change => change.Path.EndsWith("workflow-onboarding.html", StringComparison.Ordinal)).UpdatedContent);
            Assert.DoesNotContain(changes, change => change.Path.EndsWith("SOUL.md", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SkillViewStore_LoadsIndexSkillAndReferenceWithinSkillRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var model = CreateWorkflowModel() with
            {
                Corpus = new AnalysisCorpusBuilder().Build(CreateWorkflowModel())
            };
            var plan = new WorkflowOnboardingPlanner().Plan(model, root);
            var selected = Assert.Single(plan.Candidates);
            await new WorkflowOnboardingArtifactWriter().ApplyAsync(plan, [selected], new DateOnly(2026, 6, 12));

            var store = new SkillViewStore();
            var index = await store.ViewAsync(Path.Combine(root, ".agentblazor"));
            var skill = await store.ViewAsync(Path.Combine(root, ".agentblazor"), selected.Slug);
            var reference = await store.ViewAsync(Path.Combine(root, ".agentblazor"), selected.Slug, "references/evidence.md");

            Assert.Contains(selected.Slug, index);
            Assert.Contains($"# {selected.Name}", skill);
            Assert.Contains("Evidence for", reference);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ViewAsync(Path.Combine(root, ".agentblazor"), selected.Slug, "../index.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SkillCurator_MarksStalePreservesPinnedAndArchivesEligibleSkills()
    {
        var root = CreateTempDirectory();
        try
        {
            var skillsDir = Path.Combine(root, ".agentblazor", "skills");
            Directory.CreateDirectory(Path.Combine(skillsDir, "stale-skill"));
            Directory.CreateDirectory(Path.Combine(skillsDir, "pinned-skill"));
            Directory.CreateDirectory(Path.Combine(skillsDir, "archive-skill"));
            await File.WriteAllTextAsync(Path.Combine(skillsDir, ".metadata.json"), """
            {
              "schemaVersion": 1,
              "skills": [
                { "name": "stale-skill", "pinned": false, "readCount": 0, "executionCount": 0, "lastReviewed": "2026-05-01", "state": "active" },
                { "name": "pinned-skill", "pinned": true, "readCount": 0, "executionCount": 0, "lastReviewed": "2026-02-01", "state": "active" },
                { "name": "archive-skill", "pinned": false, "readCount": 0, "executionCount": 0, "lastReviewed": "2026-02-01", "state": "stale" }
              ]
            }
            """);

            var result = await new SkillCurator().CurateAsync(Path.Combine(root, ".agentblazor"), new DateOnly(2026, 6, 12));

            Assert.Contains("stale-skill", result.MarkedStale);
            Assert.DoesNotContain("pinned-skill", result.Archived);
            Assert.Contains("archive-skill", result.Archived);
            Assert.True(Directory.Exists(Path.Combine(skillsDir, ".archive", "archive-skill")));

            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(skillsDir, ".metadata.json")));
            var pinned = metadata.RootElement.GetProperty("skills").EnumerateArray()
                .Single(skill => skill.GetProperty("name").GetString() == "pinned-skill");
            Assert.Equal("active", pinned.GetProperty("state").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProjectModel CreateWorkflowModel()
    {
        var cluster = new WorkflowClusterModel
        {
            Id = "revision-submission-pipeline",
            Name = "Revision Submission Pipeline",
            SourceService = "RevisionSubmissionService",
            FilePath = "Services/RevisionSubmissionService.cs",
            Summary = "Revision submission appears to be a package lifecycle.",
            Risk = "approval required",
            RequiresApproval = true,
            Evidence = ["same service contains 4 lifecycle methods"],
            RouteHints = ["/revisions"],
            DomainTerms = ["Revision", "Submission"],
            RelatedServices = ["RevisionSubmissionService"],
            Methods =
            [
                CreateClusterMethod("GeneratePackageAsync", "generate"),
                CreateClusterMethod("SubmitAsync", "submit"),
                CreateClusterMethod("CheckStatusAsync", "status"),
                CreateClusterMethod("PromoteAsync", "promote")
            ]
        };

        var model = new ProjectModel
        {
            AppName = "RevisionApp",
            Description = "Revision workflow app",
            DesiredAgentWorkflows = ["help release managers promote revisions"],
            BlazorHostProject = "RevisionApp",
            Routes =
            [
                new RouteModel
                {
                    Id = "revisions",
                    Template = "/revisions",
                    ComponentName = "RevisionPage",
                    ComponentFile = "Pages/Revisions.razor"
                }
            ],
            Pages =
            [
                new PageModel
                {
                    Id = "revisions",
                    Route = "/revisions",
                    ComponentName = "RevisionPage",
                    FilePath = "Pages/Revisions.razor",
                    InjectedServices = ["RevisionSubmissionService"]
                }
            ],
            Services =
            [
                new ServiceModel
                {
                    TypeName = "RevisionSubmissionService",
                    FilePath = "Services/RevisionSubmissionService.cs",
                    Methods =
                    [
                        CreateMethod("GeneratePackageAsync"),
                        CreateMethod("SubmitAsync"),
                        CreateMethod("CheckStatusAsync"),
                        CreateMethod("PromoteAsync")
                    ]
                }
            ],
            Actions =
            [
                CreateAction("GeneratePackageAsync", true),
                CreateAction("SubmitAsync", true),
                CreateAction("CheckStatusAsync", false),
                CreateAction("PromoteAsync", true)
            ],
            WorkflowClusters = [cluster]
        };

        return model with { Corpus = new AnalysisCorpusBuilder().Build(model) };
    }

    private static WorkflowClusterMethodModel CreateClusterMethod(string method, string role) => new()
    {
        Service = "RevisionSubmissionService",
        Method = method,
        Role = role,
        Classification = ActionClassification.Workflow,
        Risk = "approval required"
    };

    private static ServiceMethodModel CreateMethod(string name) => new()
    {
        Name = name,
        ReturnType = "Task",
        IsPublic = true,
        IsAsync = true
    };

    private static ActionModel CreateAction(string method, bool mutation) => new()
    {
        Id = "revision-submission-service-" + WorkflowOnboardingPlanner.ToSlug(method),
        Name = method,
        SourceService = "RevisionSubmissionService",
        MethodName = method,
        FilePath = "Services/RevisionSubmissionService.cs",
        Classification = mutation ? ActionClassification.Workflow : ActionClassification.Query,
        ExposureMode = ActionExposureMode.Suggested,
        IsMutationLikely = mutation,
        RequiresApproval = mutation,
        Score = 0.8,
        RelevantRoutes = ["/revisions"]
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentblazor-workflow-onboarding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
