using AgentBlazor.App;
using AgentBlazor.Attributes;
using AgentBlazor.Core.Runtime.Agents;

namespace AgentBlazor.Demo.Services;

[AgentCapability(
    "file_audit_bundle",
    Name = "File Audit Bundle Workflow",
    Description = "Coordinate file upload policy, remote handoff, and token validation for audit-ready bundles.",
    Category = "Workflow")]
internal sealed class DemoFileWorkflowCapabilities(DemoFileWorkflowService workflow)
{
    [AgentAction("Summarize the current file audit workflow", ActionId = "summarize_workflow")]
    public async Task<CapabilityResult> SummarizeWorkflowAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await workflow.GetOrCreateAsync(sessionId, "Local", cancellationToken);
        return CapabilityResult.Success(BuildSummary(snapshot)) with
        {
            Outputs = BuildOutputs(snapshot),
            Warnings = BuildWarnings(snapshot),
            NextActions =
            [
                "Switch the workflow to remote upload mode",
                "Prepare an audit-ready remote bundle"
            ]
        };
    }

    [AgentAction("Switch the file workflow to remote upload mode", ActionId = "switch_to_remote_mode")]
    public async Task<CapabilityResult> SwitchToRemoteModeAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
    {
        var current = await workflow.GetOrCreateAsync(sessionId, "Remote", cancellationToken);
        var snapshot = await workflow.SyncFilesAsync(sessionId, current.Files, "Remote", cancellationToken);
        return CapabilityResult.Success("Switched the file workflow to Remote mode so audit bundles can be handed off safely.") with
        {
            Outputs = BuildOutputs(snapshot),
            Warnings = BuildWarnings(snapshot),
            NextActions =
            [
                "Prepare an audit-ready remote bundle",
                "Validate the remote storage tokens"
            ]
        };
    }

    [AgentAction("Prepare an audit-ready remote bundle for the current files", ActionId = "prepare_audit_bundle", RequiresApproval = true)]
    public async Task<CapabilityResult> PrepareAuditBundleAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
    {
        var current = await workflow.GetOrCreateAsync(sessionId, "Remote", cancellationToken);
        if (current.Files.Count == 0)
        {
            return CapabilityResult.Blocked("Cannot prepare an audit-ready bundle because no files are attached.") with
            {
                Outputs = BuildOutputs(current),
                Warnings = BuildWarnings(current),
                NextActions =
                [
                    "Attach one or more files to the workflow",
                    "Switch to remote mode and prepare the audit-ready bundle again"
                ]
            };
        }

        var remoteSnapshot = await workflow.SyncFilesAsync(sessionId, current.Files, "Remote", cancellationToken);
        var handoffSnapshot = await workflow.RunRemoteHandoffAsync(sessionId, cancellationToken);
        var validatedSnapshot = await workflow.ValidateRemoteTokensAsync(sessionId, cancellationToken);

        var currentFiles = validatedSnapshot.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var latestJobsByFile = validatedSnapshot.Jobs
            .Where(job => currentFiles.Contains(job.FileName))
            .GroupBy(job => job.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(job => job.UpdatedUtc)
                .First())
            .ToArray();

        var completedJobs = latestJobsByFile.Count(job =>
            string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job.Status, "Verified", StringComparison.OrdinalIgnoreCase));
        var failedJobs = latestJobsByFile.Count(job =>
            string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase));

        if (failedJobs > 0)
        {
            return CapabilityResult.Blocked(
                $"Audit bundle preparation is blocked because {failedJobs} file handoff or validation step(s) failed.") with
            {
                Outputs = new Dictionary<string, object?>(BuildOutputs(validatedSnapshot), StringComparer.OrdinalIgnoreCase)
                {
                    ["remoteModeConfirmed"] = string.Equals(remoteSnapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase),
                    ["handoffJobCount"] = handoffSnapshot.Jobs.Count,
                    ["failedJobCount"] = failedJobs
                },
                Warnings = BuildWarnings(validatedSnapshot),
                NextActions =
                [
                    "Review the failed handoff or token validation events",
                    "Replace or rename rejected files and prepare the bundle again"
                ]
            };
        }

        return CapabilityResult.Success(
            $"Prepared an audit-ready bundle for {validatedSnapshot.Files.Count} files with {completedJobs} completed or verified job records.") with
        {
            Outputs = new Dictionary<string, object?>(BuildOutputs(validatedSnapshot), StringComparer.OrdinalIgnoreCase)
            {
                ["remoteModeConfirmed"] = string.Equals(remoteSnapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase),
                ["handoffJobCount"] = handoffSnapshot.Jobs.Count,
                ["failedJobCount"] = failedJobs
            },
            Warnings = BuildWarnings(validatedSnapshot),
            NextActions =
            [
                "Review the latest handoff and validation events",
                "Explain any failed token validations"
            ]
        };
    }

    [AgentAction("Apply the file audit recovery playbook for the current files", ActionId = "apply_audit_recovery_playbook")]
    public async Task<CapabilityResult> ApplyAuditRecoveryPlaybookAsync(
        [AgentParam(ContextKey = AgentRuntimeContextKeys.SessionId)] string sessionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await workflow.ApplyRecoveryPlaybookAsync(sessionId, cancellationToken);
        var recoveredFiles = snapshot.Files
            .Where(file => file.Contains("-recovered", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var summary = recoveredFiles.Length > 0
            ? $"Applied the file recovery playbook and replaced {recoveredFiles.Length} rejected file(s)."
            : "File recovery playbook ran, but there were no rejected files to replace.";

        return CapabilityResult.Success(summary) with
        {
            Outputs = new Dictionary<string, object?>(BuildOutputs(snapshot), StringComparer.OrdinalIgnoreCase)
            {
                ["recoveredFileCount"] = recoveredFiles.Length,
                ["recoveredFiles"] = recoveredFiles
            },
            Warnings = BuildWarnings(snapshot),
            NextActions =
            [
                "Prepare the audit-ready remote bundle again.",
                "Review the latest handoff and validation events after the retry."
            ]
        };
    }

    private static Dictionary<string, object?> BuildOutputs(DemoFileWorkflowSnapshot snapshot)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["uploadMode"] = snapshot.UploadMode,
            ["fileCount"] = snapshot.Files.Count,
            ["files"] = snapshot.Files.ToArray(),
            ["recentEventCount"] = snapshot.Events.Count,
            ["recentJobCount"] = snapshot.Jobs.Count,
            ["latestEvent"] = snapshot.Events.FirstOrDefault()?.Message,
            ["latestJobStatus"] = snapshot.Jobs.FirstOrDefault()?.Status
        };
    }

    private static string BuildSummary(DemoFileWorkflowSnapshot snapshot)
    {
        var latestEvent = snapshot.Events.FirstOrDefault()?.Message ?? "No workflow events yet.";
        return $"The file audit workflow is in {snapshot.UploadMode} mode with {snapshot.Files.Count} files. Latest event: {latestEvent}";
    }

    private static IReadOnlyList<string> BuildWarnings(DemoFileWorkflowSnapshot snapshot)
    {
        var warnings = new List<string>();

        if (snapshot.Files.Count == 0)
        {
            warnings.Add("No files are currently attached to the workflow.");
        }

        if (!string.Equals(snapshot.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("The workflow is still in Local mode, so remote handoff steps cannot complete yet.");
        }

        foreach (var failedJob in snapshot.Jobs.Where(job => string.Equals(job.Status, "Failed", StringComparison.OrdinalIgnoreCase)).Take(3))
        {
            warnings.Add($"{failedJob.FileName}: {failedJob.Message}");
        }

        return warnings;
    }
}
