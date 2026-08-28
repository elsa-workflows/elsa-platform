using ElsaControl.Deployment.Core.Cockpit;

namespace ElsaControl.Deployment.Core.Workspace;

public interface IWorkspaceDeploymentStore
{
    Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(
        Guid workspaceId,
        Guid applicationId,
        UpdateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(
        Guid workspaceId,
        Guid environmentId,
        UpdateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceWorkflowEngine> UpdateEngineAsync(
        Guid workspaceId,
        Guid engineId,
        UpdateWorkflowEngineRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceDeploymentSecretStore>> ListSecretStoresAsync(
        Guid workspaceId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment secret store listing is not supported by this store.");

    Task<WorkspaceDeploymentSecretStore> CreateSecretStoreAsync(
        Guid workspaceId,
        CreateDeploymentSecretStoreRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment secret store creation is not supported by this store.");

    Task<WorkspaceDeploymentSecretStore> UpdateSecretStoreAsync(
        Guid workspaceId,
        Guid secretStoreId,
        UpdateDeploymentSecretStoreRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment secret store updates are not supported by this store.");

    Task<WorkspaceDeploymentSecretStore> ArchiveSecretStoreAsync(
        Guid workspaceId,
        Guid secretStoreId,
        Guid? actorAccountId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment secret store archival is not supported by this store.");

    Task<IReadOnlyList<WorkspaceDeploymentCredentialReference>> ListCredentialReferencesAsync(
        Guid workspaceId,
        Guid? secretStoreId = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential reference listing is not supported by this store.");

    Task<WorkspaceDeploymentCredentialReference> CreateCredentialReferenceAsync(
        Guid workspaceId,
        CreateDeploymentCredentialReferenceRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential reference creation is not supported by this store.");

    Task<WorkspaceDeploymentCredentialReference> UpdateCredentialReferenceAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        UpdateDeploymentCredentialReferenceRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential reference updates are not supported by this store.");

    Task<WorkspaceDeploymentCredentialReference> RotateCredentialReferenceAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        RotateDeploymentCredentialReferenceRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential reference rotation is not supported by this store.");

    Task<WorkspaceDeploymentCredentialReference> ArchiveCredentialReferenceAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        Guid? actorAccountId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential reference archival is not supported by this store.");

    Task<IReadOnlyList<WorkspaceDeploymentCredentialUsage>> ListCredentialReferenceUsageAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential reference usage is not supported by this store.");

    Task<WorkspaceEngineCredentialSecret?> GetEngineCredentialSecretAsync(
        Guid workspaceId,
        Guid engineId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine credential secret lookup is not supported by this store.");

    Task<WorkspaceDeploymentCredentialSecret?> GetCredentialSecretAsync(
        Guid workspaceId,
        Guid credentialReferenceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Deployment credential secret lookup is not supported by this store.");

    Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(
        Guid workspaceId,
        Guid environmentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceDesiredStateRevisionSummary>> ListApplicationRevisionsAsync(
        Guid workspaceId,
        Guid applicationId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Application revision listing is not supported by this store.");

    Task<WorkspaceDesiredStateRevisionDetail?> GetRevisionDetailAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Revision detail lookup is not supported by this store.");

    Task<WorkspaceWorkflowEngine?> GetEngineAsync(
        Guid workspaceId,
        Guid engineId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceWorkflowEngine>> ListEnginesDueForVerificationAsync(
        DateTimeOffset verifyBefore,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine verification scans are not supported by this store.");

    Task<EngineHealthResult> UpdateEngineHealthAsync(
        Guid workspaceId,
        EngineHealthUpdate update,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine health updates are not supported by this store.");

    Task<EngineHealthResult> ApplyEngineHeartbeatAsync(
        Guid workspaceId,
        EngineHealthUpdate update,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Engine heartbeats are not supported by this store.");
}
