using System.Security.Cryptography;
using System.Text;
using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class WorkspaceDeploymentService(IWorkspaceDeploymentStore store)
{
    public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        store.GetCockpitAsync(workspaceId, cancellationToken);

    public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.CreateApplicationAsync(workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        return store.CreateEnvironmentAsync(workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CredentialProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CredentialReference);

        if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out _))
            throw new ArgumentException("Engine base URL must be an absolute URI.", nameof(request));

        return store.RegisterEngineAsync(workspaceId, request, cancellationToken);
    }

    public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Label);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DesiredStateJson);
        return store.CreateRevisionAsync(
            workspaceId,
            request with { DesiredStateJson = request.DesiredStateJson },
            cancellationToken);
    }

    public static string ComputeDesiredStateHash(string desiredStateJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(desiredStateJson));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
