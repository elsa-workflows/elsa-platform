using ValenceControl.Deployment.Core.Cockpit;

namespace ValenceControl.Deployment.Core.Workspace;

public sealed class DeploymentPromotionService(
    IWorkspaceDeploymentStore store,
    DeploymentValidationService validation)
{
    public async Task<WorkspacePromotionResult> PromoteAsync(
        Guid workspaceId,
        WorkspacePromotionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Label);

        var source = await store.GetRevisionAsync(workspaceId, request.SourceRevisionId, cancellationToken)
            ?? throw new InvalidOperationException("Source revision is not visible in this workspace.");
        if (source.EnvironmentId != request.SourceEnvironmentId)
            throw new InvalidOperationException("Source revision does not belong to the requested source environment.");

        var comparison = await validation.PreviewPromotionAsync(
            workspaceId,
            new WorkspacePromotionPreviewRequest(
                request.SourceEnvironmentId,
                request.TargetEnvironmentId,
                request.SourceRevisionId,
                request.TargetEngineId),
            cancellationToken);
        var blocker = comparison.Validations.FirstOrDefault(x => x.Severity == ValidationSeverity.Blocker)
            ?? comparison.Artifacts.SelectMany(x => x.RuntimeCompatibility).FirstOrDefault(x => x.Severity == ValidationSeverity.Blocker);
        if (blocker is not null)
            throw new InvalidOperationException(blocker.Message);

        var target = await store.CreateRevisionAsync(
            workspaceId,
            new CreateDesiredStateRevisionRequest(
                source.ApplicationId,
                request.TargetEnvironmentId,
                request.Label,
                request.Commit ?? source.Commit,
                source.DesiredStateJson,
                request.ActorAccountId),
            cancellationToken);

        return new WorkspacePromotionResult(source, target, comparison);
    }
}
