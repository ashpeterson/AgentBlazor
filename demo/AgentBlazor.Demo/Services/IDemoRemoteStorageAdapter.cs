namespace AgentBlazor.Demo.Services;

internal interface IDemoRemoteStorageAdapter
{
    string AdapterName { get; }

    Task<DemoRemoteStorageHandoffResult> HandoffAsync(
        string sessionKey,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<DemoRemoteStorageValidationResult> ValidateTokenAsync(
        string sessionKey,
        string fileName,
        string storageToken,
        CancellationToken cancellationToken = default);
}

internal sealed record DemoRemoteStorageHandoffResult(
    bool Succeeded,
    bool IsTransientFailure,
    string? StorageToken,
    string Message);

internal sealed record DemoRemoteStorageValidationResult(
    bool RequestSucceeded,
    bool IsTransientFailure,
    bool IsValid,
    string Message);

