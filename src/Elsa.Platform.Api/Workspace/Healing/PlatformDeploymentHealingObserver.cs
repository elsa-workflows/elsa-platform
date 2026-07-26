using System.Security.Cryptography;
using System.Text;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.Healing.Core.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Workspace.Healing;

public sealed class PlatformDeploymentHealingObserver(
    IWorkspaceDeploymentMutationStore mutationStore,
    IWorkspaceDeploymentStore deploymentStore,
    HealingDbContext healingDbContext,
    IDeploymentObservationSink sink,
    HealingKillSwitch killSwitch)
{
    public async ValueTask ObserveCompletedCommandAsync(
        DeploymentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!killSwitch.CanVerify().Allowed)
            return;
        if (command.Status != DeploymentCommandStatus.Completed || command.Action != DeploymentCommandAction.Deploy)
            return;
        var run = await mutationStore.GetRunAsync(command.WorkspaceId, command.RunId, cancellationToken);
        if (run is null || run.Status != WorkspaceDeploymentRunStatus.Succeeded)
            return;
        var healingConfigured = await healingDbContext.HealingConfigurations.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == command.WorkspaceId && x.ApplicationId == run.ApplicationId,
            cancellationToken);
        if (!healingConfigured)
            return;
        var revision = await deploymentStore.GetRevisionAsync(command.WorkspaceId, run.SourceRevisionId, cancellationToken);
        if (revision is null)
            return;
        var deployedRevision = string.IsNullOrWhiteSpace(revision.Commit)
            ? revision.Id.ToString("D")
            : revision.Commit;
        var completedAt = command.CompletedAt ?? command.UpdatedAt;
        var digestMaterial = $"{command.Id:N}:{run.Id:N}:{deployedRevision}:{completedAt:O}";
        var evidenceDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digestMaterial))).ToLowerInvariant()}";
        await sink.AppendAsync(new DeploymentObservationRequest(
            HealingContractVersions.DeploymentProtocol,
            command.WorkspaceId,
            run.ApplicationId,
            command.EnvironmentId,
            deployedRevision,
            completedAt,
            DeploymentObservationSources.PlatformDeployment,
            command.Id.ToString("D"),
            $"platform-engine:{command.EngineId:D}",
            evidenceDigest,
            $"platform-command:{command.Id:N}"), cancellationToken);
    }
}
