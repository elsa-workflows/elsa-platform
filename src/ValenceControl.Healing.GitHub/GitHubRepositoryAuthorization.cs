namespace ValenceControl.Healing.GitHub;

public sealed record GitHubRepositoryAuthorization(
    Guid ProviderConnectionId,
    string RepositoryProviderId,
    string Owner,
    string Name,
    string InstallationId,
    GitHubAppCredential Credential,
    IReadOnlyDictionary<string, GitHubApprovedWorkflow> ApprovedWorkflows);

public sealed record GitHubApprovedWorkflow(string Identity, string Reference, string Revision);

public interface IGitHubRepositoryAuthorizationResolver
{
    ValueTask<GitHubRepositoryAuthorization?> ResolveAsync(
        Guid providerConnectionId,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubSecurityException(string reasonCode) : InvalidOperationException(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}

public static class GitHubSecurityReasonCodes
{
    public const string InvalidRequest = "github-invalid-request";
    public const string RepositoryNotAuthorized = "github-repository-not-authorized";
    public const string WorkflowNotAuthorized = "github-workflow-not-authorized";
    public const string TokenUnavailable = "github-installation-token-unavailable";
    public const string IdempotencyConflict = "github-idempotency-conflict";
    public const string OperationInProgress = "github-operation-in-progress";
    public const string ProviderRejected = "github-provider-rejected-operation";
    public const string IdentityInvalid = "github-workload-identity-invalid";
    public const string IdentityReplay = "github-workload-identity-replay";
    public const string WebhookInvalid = "github-webhook-invalid";
    public const string WebhookReplay = "github-webhook-replay";
    public const string PatchInvalid = "github-patch-invalid";
    public const string TargetRevisionStale = "github-target-revision-stale";
    public const string PublicationDenied = "github-publication-denied";
}
