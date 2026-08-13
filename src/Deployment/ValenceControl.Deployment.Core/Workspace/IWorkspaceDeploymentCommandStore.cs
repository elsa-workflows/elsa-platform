namespace ValenceControl.Deployment.Core.Workspace;

public interface IWorkspaceDeploymentCommandStore
{
    Task<DeploymentCommand> CreateCommandAsync(
        Guid workspaceId,
        CreateDeploymentCommandRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentCommand>> PollPendingCommandsAsync(
        Guid workspaceId,
        Guid engineId,
        int limit,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand?> GetCommandAsync(
        Guid workspaceId,
        Guid commandId,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand> ClaimCommandAsync(
        Guid workspaceId,
        Guid commandId,
        ClaimDeploymentCommandRequest request,
        string leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand> HeartbeatCommandAsync(
        Guid workspaceId,
        Guid commandId,
        DeploymentCommandHeartbeatRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand> RecordCommandProgressAsync(
        Guid workspaceId,
        Guid commandId,
        DeploymentCommandProgressRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand> CompleteCommandAsync(
        Guid workspaceId,
        Guid commandId,
        CompleteDeploymentCommandRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand> FailCommandAsync(
        Guid workspaceId,
        Guid commandId,
        FailDeploymentCommandRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommand> RejectCommandAsync(
        Guid workspaceId,
        Guid commandId,
        RejectDeploymentCommandRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<int> MarkStaleCommandsRecoveryRequiredAsync(
        DateTimeOffset now,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommandWebhookNotification> CreateWebhookNotificationAsync(
        Guid workspaceId,
        Guid engineId,
        Guid commandId,
        string safePayloadJson,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeploymentWebhookNotificationDispatchTarget>> ListPendingWebhookNotificationTargetsAsync(
        int limit,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommandWebhookNotification> MarkWebhookNotificationSentAsync(
        Guid workspaceId,
        Guid notificationId,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommandWebhookNotification> MarkWebhookNotificationFailedAsync(
        Guid workspaceId,
        Guid notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DeploymentCommandWebhookNotification> MarkWebhookNotificationSkippedAsync(
        Guid workspaceId,
        Guid notificationId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
