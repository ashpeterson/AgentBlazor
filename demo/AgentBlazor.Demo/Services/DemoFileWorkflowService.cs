using AgentBlazor.Demo.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentBlazor.Demo.Services;

internal sealed class DemoFileWorkflowService(
    IDbContextFactory<DemoWorkflowDbContext> dbContextFactory,
    IDemoRemoteStorageAdapter remoteStorageAdapter,
    IOptions<DemoRemoteStorageOptions> remoteStorageOptions)
{
    private static readonly IReadOnlyList<string> DefaultFiles =
    [
        "vendor-evidence.csv",
        "risk-summary-q1.pdf"
    ];

    public event Action<string>? Changed;

    private readonly DemoRemoteStorageOptions _remoteStorageOptions = remoteStorageOptions.Value;

    public async Task<DemoFileWorkflowSnapshot> GetOrCreateAsync(
        string sessionKey,
        string uploadMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionKey = NormalizeSessionKey(sessionKey);
        var normalizedMode = NormalizeUploadMode(uploadMode);
        var seeded = false;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existingFiles = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        if (existingFiles.Count == 0)
        {
            seeded = true;
            var now = DateTime.UtcNow;
            foreach (var file in DefaultFiles)
            {
                db.FileWorkflowFiles.Add(new DemoFileWorkflowFileEntity
                {
                    SessionKey = normalizedSessionKey,
                    FileName = file,
                    UploadMode = normalizedMode,
                    StorageToken = null,
                    AddedUtc = now,
                    UpdatedUtc = now
                });
                db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                {
                    SessionKey = normalizedSessionKey,
                    TimestampUtc = now,
                    EventType = "Seeded",
                    FileName = file,
                    Message = $"Seeded file '{file}' ({normalizedMode} mode)."
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            existingFiles = await db.FileWorkflowFiles
                .Where(x => x.SessionKey == normalizedSessionKey)
                .OrderBy(x => x.FileName)
                .ToListAsync(cancellationToken);
        }

        var snapshot = await BuildSnapshotAsync(db, normalizedSessionKey, existingFiles, cancellationToken);
        if (seeded)
        {
            NotifyChanged(normalizedSessionKey);
        }
        return snapshot;
    }

    public async Task<DemoFileWorkflowSnapshot> SyncFilesAsync(
        string sessionKey,
        IReadOnlyList<string> files,
        string uploadMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionKey = NormalizeSessionKey(sessionKey);
        var normalizedMode = NormalizeUploadMode(uploadMode);
        var normalizedFiles = NormalizeFiles(files);
        var now = DateTime.UtcNow;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var current = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .ToListAsync(cancellationToken);
        var byName = current.ToDictionary(static x => x.FileName, StringComparer.OrdinalIgnoreCase);

        var removed = current.Where(file => !normalizedFiles.Contains(file.FileName, StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (var file in removed)
        {
            db.FileWorkflowFiles.Remove(file);
            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = now,
                EventType = "Removed",
                FileName = file.FileName,
                Message = $"Removed file '{file.FileName}'."
            });
        }

        foreach (var fileName in normalizedFiles)
        {
            if (!byName.TryGetValue(fileName, out var entity))
            {
                db.FileWorkflowFiles.Add(new DemoFileWorkflowFileEntity
                {
                    SessionKey = normalizedSessionKey,
                    FileName = fileName,
                    UploadMode = normalizedMode,
                    StorageToken = null,
                    AddedUtc = now,
                    UpdatedUtc = now
                });
                db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                {
                    SessionKey = normalizedSessionKey,
                    TimestampUtc = now,
                    EventType = "Attached",
                    FileName = fileName,
                    Message = $"Attached file '{fileName}' ({normalizedMode} mode)."
                });
                continue;
            }

            if (!string.Equals(entity.UploadMode, normalizedMode, StringComparison.OrdinalIgnoreCase))
            {
                entity.UploadMode = normalizedMode;
                entity.StorageToken = null;
                entity.UpdatedUtc = now;
                db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                {
                    SessionKey = normalizedSessionKey,
                    TimestampUtc = now,
                    EventType = "ModeUpdated",
                    FileName = entity.FileName,
                    Message = $"Updated '{entity.FileName}' to {normalizedMode} mode."
                });
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var persisted = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var snapshot = await BuildSnapshotAsync(db, normalizedSessionKey, persisted, cancellationToken);
        NotifyChanged(normalizedSessionKey);
        return snapshot;
    }

    public async Task<DemoFileWorkflowSnapshot> RunRemoteHandoffAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionKey = NormalizeSessionKey(sessionKey);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var files = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        var remoteFiles = files
            .Where(static file => string.Equals(file.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remoteFiles.Length == 0)
        {
            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = DateTime.UtcNow,
                EventType = "RemoteHandoffSkipped",
                FileName = "system",
                Message = "Remote handoff skipped because no files are in Remote mode."
            });
            await db.SaveChangesAsync(cancellationToken);
            var skippedSnapshot = await BuildSnapshotAsync(db, normalizedSessionKey, files, cancellationToken);
            NotifyChanged(normalizedSessionKey);
            return skippedSnapshot;
        }

        var startedAt = DateTime.UtcNow;
        var jobs = new List<DemoFileWorkflowJobEntity>(remoteFiles.Length);
        foreach (var file in remoteFiles)
        {
            var job = new DemoFileWorkflowJobEntity
            {
                SessionKey = normalizedSessionKey,
                JobId = BuildJobId("handoff"),
                Operation = "remote_handoff",
                FileName = file.FileName,
                UploadMode = file.UploadMode,
                Status = "InProgress",
                StorageToken = file.StorageToken,
                Message = $"Queued remote storage handoff via {remoteStorageAdapter.AdapterName}.",
                CreatedUtc = startedAt,
                UpdatedUtc = startedAt,
                CompletedUtc = null
            };
            jobs.Add(job);
            db.FileWorkflowJobs.Add(job);
            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = startedAt,
                EventType = "RemoteHandoffStarted",
                FileName = file.FileName,
                Message = $"Started remote handoff for '{file.FileName}' ({remoteStorageAdapter.AdapterName})."
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var job in jobs)
        {
            var file = remoteFiles.First(x => string.Equals(x.FileName, job.FileName, StringComparison.OrdinalIgnoreCase));
            var (result, attempts) = await ExecuteWithRetryAsync(
                async ct => await remoteStorageAdapter.HandoffAsync(normalizedSessionKey, job.FileName, ct),
                static handoff => !handoff.Succeeded && handoff.IsTransientFailure,
                onRetryAttempt: retry =>
                {
                    db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                    {
                        SessionKey = normalizedSessionKey,
                        TimestampUtc = DateTime.UtcNow,
                        EventType = "RemoteHandoffRetry",
                        FileName = job.FileName,
                        Message = $"Retrying remote handoff for '{job.FileName}' (attempt {retry.AttemptNumber + 1}/{GetMaxAttempts()}): {retry.Result.Message}"
                    });
                    return Task.CompletedTask;
                },
                cancellationToken);

            var completedAt = DateTime.UtcNow;
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.StorageToken))
            {
                file.StorageToken = result.StorageToken.Trim();
                file.UpdatedUtc = completedAt;

                job.StorageToken = file.StorageToken;
                job.Status = "Completed";
                job.Message = $"{result.Message} (attempt {attempts}/{GetMaxAttempts()}).";
                job.UpdatedUtc = completedAt;
                job.CompletedUtc = completedAt;

                db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                {
                    SessionKey = normalizedSessionKey,
                    TimestampUtc = completedAt,
                    EventType = "RemoteHandoffCompleted",
                    FileName = job.FileName,
                    Message = $"Completed remote handoff for '{job.FileName}' ({file.StorageToken}) after {attempts} attempt(s)."
                });
            }
            else
            {
                job.Status = "Failed";
                job.Message = $"{result.Message} (attempts {attempts}/{GetMaxAttempts()}).";
                job.UpdatedUtc = completedAt;
                job.CompletedUtc = completedAt;

                db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                {
                    SessionKey = normalizedSessionKey,
                    TimestampUtc = completedAt,
                    EventType = "RemoteHandoffFailed",
                    FileName = job.FileName,
                    Message = $"Remote handoff failed for '{job.FileName}': {result.Message}"
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var persistedFiles = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var snapshot = await BuildSnapshotAsync(db, normalizedSessionKey, persistedFiles, cancellationToken);
        NotifyChanged(normalizedSessionKey);
        return snapshot;
    }

    public async Task<DemoFileWorkflowSnapshot> ValidateRemoteTokensAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionKey = NormalizeSessionKey(sessionKey);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var files = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        var remoteFiles = files
            .Where(static file => string.Equals(file.UploadMode, "Remote", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remoteFiles.Length == 0)
        {
            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = DateTime.UtcNow,
                EventType = "RemoteTokenValidationSkipped",
                FileName = "system",
                Message = "Token validation skipped because no files are in Remote mode."
            });
            await db.SaveChangesAsync(cancellationToken);
            var skippedSnapshot = await BuildSnapshotAsync(db, normalizedSessionKey, files, cancellationToken);
            NotifyChanged(normalizedSessionKey);
            return skippedSnapshot;
        }

        var now = DateTime.UtcNow;
        foreach (var file in remoteFiles)
        {
            if (string.IsNullOrWhiteSpace(file.StorageToken))
            {
                db.FileWorkflowJobs.Add(new DemoFileWorkflowJobEntity
                {
                    SessionKey = normalizedSessionKey,
                    JobId = BuildJobId("validate"),
                    Operation = "token_validation",
                    FileName = file.FileName,
                    UploadMode = file.UploadMode,
                    Status = "Failed",
                    StorageToken = null,
                    Message = "Remote token missing. Run sync_remote_handoff first.",
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    CompletedUtc = now
                });

                db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                {
                    SessionKey = normalizedSessionKey,
                    TimestampUtc = now,
                    EventType = "RemoteTokenMissing",
                    FileName = file.FileName,
                    Message = $"{file.FileName}: Remote token missing."
                });

                continue;
            }

            var (result, attempts) = await ExecuteWithRetryAsync(
                async ct => await remoteStorageAdapter.ValidateTokenAsync(normalizedSessionKey, file.FileName, file.StorageToken!, ct),
                static validation => !validation.RequestSucceeded && validation.IsTransientFailure,
                onRetryAttempt: retry =>
                {
                    db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
                    {
                        SessionKey = normalizedSessionKey,
                        TimestampUtc = DateTime.UtcNow,
                        EventType = "RemoteTokenValidationRetry",
                        FileName = file.FileName,
                        Message = $"Retrying token validation for '{file.FileName}' (attempt {retry.AttemptNumber + 1}/{GetMaxAttempts()}): {retry.Result.Message}"
                    });
                    return Task.CompletedTask;
                },
                cancellationToken);

            var status = result.RequestSucceeded && result.IsValid
                ? "Verified"
                : "Failed";
            var eventType = result.RequestSucceeded
                ? (result.IsValid ? "RemoteTokenVerified" : "RemoteTokenRejected")
                : "RemoteTokenValidationFailed";

            db.FileWorkflowJobs.Add(new DemoFileWorkflowJobEntity
            {
                SessionKey = normalizedSessionKey,
                JobId = BuildJobId("validate"),
                Operation = "token_validation",
                FileName = file.FileName,
                UploadMode = file.UploadMode,
                Status = status,
                StorageToken = file.StorageToken,
                Message = $"{result.Message} (attempts {attempts}/{GetMaxAttempts()}).",
                CreatedUtc = now,
                UpdatedUtc = now,
                CompletedUtc = now
            });

            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = now,
                EventType = eventType,
                FileName = file.FileName,
                Message = $"{file.FileName}: {result.Message}"
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var persistedFiles = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var snapshot = await BuildSnapshotAsync(db, normalizedSessionKey, persistedFiles, cancellationToken);
        NotifyChanged(normalizedSessionKey);
        return snapshot;
    }

    public async Task<DemoFileWorkflowSnapshot> RecordWorkflowEventAsync(
        string sessionKey,
        string eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionKey = NormalizeSessionKey(sessionKey);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
        {
            SessionKey = normalizedSessionKey,
            TimestampUtc = DateTime.UtcNow,
            EventType = string.IsNullOrWhiteSpace(eventType) ? "Event" : eventType.Trim(),
            FileName = string.IsNullOrWhiteSpace(subject) ? "system" : subject.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? "Workflow event." : message.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);

        var files = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var snapshot = await BuildSnapshotAsync(db, normalizedSessionKey, files, cancellationToken);
        NotifyChanged(normalizedSessionKey);
        return snapshot;
    }

    public async Task<DemoFileWorkflowSnapshot> ApplyRecoveryPlaybookAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionKey = NormalizeSessionKey(sessionKey);
        var now = DateTime.UtcNow;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var files = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);

        var recoveryApplied = false;
        foreach (var file in files)
        {
            if (!file.FileName.Contains("-reject", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var recoveredName = file.FileName.Replace("-reject", "-recovered", StringComparison.OrdinalIgnoreCase);
            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = now,
                EventType = "RecoveryApplied",
                FileName = recoveredName,
                Message = $"Applied the recovery playbook and replaced rejected file '{file.FileName}' with '{recoveredName}'."
            });

            file.FileName = recoveredName;
            file.StorageToken = null;
            file.UploadMode = "Remote";
            file.UpdatedUtc = now;
            recoveryApplied = true;
        }

        if (!recoveryApplied)
        {
            db.FileWorkflowEvents.Add(new DemoFileWorkflowEventEntity
            {
                SessionKey = normalizedSessionKey,
                TimestampUtc = now,
                EventType = "RecoverySkipped",
                FileName = "system",
                Message = "Recovery playbook ran but found no rejected files to replace."
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var persistedFiles = await db.FileWorkflowFiles
            .Where(x => x.SessionKey == normalizedSessionKey)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
        var snapshot = await BuildSnapshotAsync(db, normalizedSessionKey, persistedFiles, cancellationToken);
        NotifyChanged(normalizedSessionKey);
        return snapshot;
    }

    private int GetMaxAttempts()
    {
        return Math.Clamp(_remoteStorageOptions.MaxAttempts, 1, 5);
    }

    private TimeSpan GetRetryDelay()
    {
        var milliseconds = Math.Clamp(_remoteStorageOptions.RetryDelayMilliseconds, 25, 2_000);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private async Task<(T Result, int Attempts)> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<T, bool> shouldRetry,
        Func<RetryContext<T>, Task>? onRetryAttempt,
        CancellationToken cancellationToken)
    {
        var maxAttempts = GetMaxAttempts();
        var attempt = 0;
        T? lastResult = default;

        while (attempt < maxAttempts)
        {
            attempt++;
            lastResult = await operation(cancellationToken);
            if (!shouldRetry(lastResult) || attempt >= maxAttempts)
            {
                return (lastResult, attempt);
            }

            if (onRetryAttempt is not null)
            {
                await onRetryAttempt(new RetryContext<T>(attempt, lastResult));
            }

            await Task.Delay(GetRetryDelay(), cancellationToken);
        }

        return (lastResult!, attempt);
    }

    private static string NormalizeSessionKey(string sessionKey)
    {
        return string.IsNullOrWhiteSpace(sessionKey) ? "global" : sessionKey.Trim();
    }

    private static string NormalizeUploadMode(string uploadMode)
    {
        return string.Equals(uploadMode, "remote", StringComparison.OrdinalIgnoreCase)
            ? "Remote"
            : "Local";
    }

    private static IReadOnlyList<string> NormalizeFiles(IReadOnlyList<string> files)
    {
        return files
            .Where(static file => !string.IsNullOrWhiteSpace(file))
            .Select(static file => file.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildJobId(string prefix)
    {
        return $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
    }

    private void NotifyChanged(string sessionKey)
    {
        Changed?.Invoke(sessionKey);
    }

    private readonly record struct RetryContext<T>(
        int AttemptNumber,
        T Result);

    private static async Task<DemoFileWorkflowSnapshot> BuildSnapshotAsync(
        DemoWorkflowDbContext db,
        string sessionKey,
        IReadOnlyList<DemoFileWorkflowFileEntity> files,
        CancellationToken cancellationToken)
    {
        var events = await db.FileWorkflowEvents
            .Where(x => x.SessionKey == sessionKey)
            .OrderByDescending(x => x.TimestampUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        var jobs = await db.FileWorkflowJobs
            .Where(x => x.SessionKey == sessionKey)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        return new DemoFileWorkflowSnapshot(
            files.Select(static file => file.FileName).ToArray(),
            files.FirstOrDefault()?.UploadMode ?? "Local",
            events.Select(static fileEvent => new DemoFileWorkflowEvent(
                fileEvent.TimestampUtc,
                fileEvent.EventType,
                fileEvent.FileName,
                fileEvent.Message)).ToArray(),
            jobs.Select(static job => new DemoFileWorkflowJob(
                job.CreatedUtc,
                job.UpdatedUtc,
                job.CompletedUtc,
                job.JobId,
                job.Operation,
                job.FileName,
                job.UploadMode,
                job.Status,
                job.StorageToken,
                job.Message)).ToArray());
    }
}

internal sealed record DemoFileWorkflowSnapshot(
    IReadOnlyList<string> Files,
    string UploadMode,
    IReadOnlyList<DemoFileWorkflowEvent> Events,
    IReadOnlyList<DemoFileWorkflowJob> Jobs);

internal sealed record DemoFileWorkflowEvent(
    DateTime TimestampUtc,
    string EventType,
    string FileName,
    string Message);

internal sealed record DemoFileWorkflowJob(
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? CompletedUtc,
    string JobId,
    string Operation,
    string FileName,
    string UploadMode,
    string Status,
    string? StorageToken,
    string Message);
