namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class DeploymentCommandService(
    IWorkspaceDeploymentCommandStore store,
    TimeProvider? timeProvider = null)
{
    private static readonly string[] UnsafeTerms =
    [
        "authorization",
        "bearer",
        "connection string",
        "connectionstring",
        "password",
        "private key",
        "secret",
        "token"
    ];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<IReadOnlyList<DeploymentCommand>> PollPendingCommandsAsync(
        Guid workspaceId,
        Guid engineId,
        int limit,
        CancellationToken cancellationToken = default) =>
        store.PollPendingCommandsAsync(workspaceId, engineId, Math.Clamp(limit, 1, 100), _timeProvider.GetUtcNow(), cancellationToken);

    public async Task<DeploymentCommandClaim> ClaimCommandAsync(
        Guid workspaceId,
        Guid commandId,
        ClaimDeploymentCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.WorkerId))
            throw new InvalidOperationException("Runtime worker identity is required.");
        if (request.LeaseDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("Command lease duration must be positive.");

        var leaseToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
        var command = await store.ClaimCommandAsync(workspaceId, commandId, request, leaseToken, _timeProvider.GetUtcNow(), cancellationToken);
        return new DeploymentCommandClaim(command, leaseToken);
    }

    public Task<DeploymentCommand> HeartbeatAsync(
        Guid workspaceId,
        Guid commandId,
        DeploymentCommandHeartbeatRequest request,
        CancellationToken cancellationToken = default) =>
        store.HeartbeatCommandAsync(workspaceId, commandId, request, _timeProvider.GetUtcNow(), cancellationToken);

    public Task<DeploymentCommand> ProgressAsync(
        Guid workspaceId,
        Guid commandId,
        DeploymentCommandProgressRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PercentComplete is < 0 or > 100)
            throw new InvalidOperationException("Command progress percent must be between 0 and 100.");
        return store.RecordCommandProgressAsync(
            workspaceId,
            commandId,
            request with { Message = SafeMessage(request.Message) },
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<DeploymentCommand> CompleteAsync(
        Guid workspaceId,
        Guid commandId,
        CompleteDeploymentCommandRequest request,
        CancellationToken cancellationToken = default) =>
        store.CompleteCommandAsync(
            workspaceId,
            commandId,
            request with { Diagnostics = (request.Diagnostics ?? []).Select(SafeDiagnostic).ToList() },
            _timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<DeploymentCommand> FailAsync(
        Guid workspaceId,
        Guid commandId,
        FailDeploymentCommandRequest request,
        CancellationToken cancellationToken = default) =>
        store.FailCommandAsync(
            workspaceId,
            commandId,
            request with { Diagnostics = (request.Diagnostics ?? []).Select(SafeDiagnostic).ToList() },
            _timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<DeploymentCommand> RejectAsync(
        Guid workspaceId,
        Guid commandId,
        RejectDeploymentCommandRequest request,
        CancellationToken cancellationToken = default) =>
        store.RejectCommandAsync(
            workspaceId,
            commandId,
            request with { Diagnostics = (request.Diagnostics ?? []).Select(SafeDiagnostic).ToList() },
            _timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<int> RecoverStaleCommandsAsync(TimeSpan staleAfter, CancellationToken cancellationToken = default) =>
        store.MarkStaleCommandsRecoveryRequiredAsync(_timeProvider.GetUtcNow(), staleAfter, cancellationToken);

    public Task<DeploymentCommandWebhookNotification> CreateWebhookNotificationAsync(
        Guid workspaceId,
        Guid engineId,
        Guid commandId,
        CancellationToken cancellationToken = default) =>
        store.CreateWebhookNotificationAsync(
            workspaceId,
            engineId,
            commandId,
            $$"""{"workspaceId":"{{workspaceId:D}}","engineId":"{{engineId:D}}","commandHint":"{{commandId:D}}","reason":"command-available"}""",
            _timeProvider.GetUtcNow(),
            cancellationToken);

    private static DeploymentCommandDiagnostic SafeDiagnostic(DeploymentCommandDiagnostic diagnostic) =>
        diagnostic with { Message = SafeMessage(diagnostic.Message) };

    private static string SafeMessage(string? value)
    {
        var safe = (value ?? "").Trim();
        foreach (var term in UnsafeTerms)
            safe = safe.Replace(term, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return safe.Length <= 512 ? safe : safe[..512];
    }
}
