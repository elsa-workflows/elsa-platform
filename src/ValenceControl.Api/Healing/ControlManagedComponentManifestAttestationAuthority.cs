using System.Security.Cryptography;
using System.Text;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Manifests;
using ValenceControl.Healing.Core.Ownership;

namespace ValenceControl.Api.Healing;

internal sealed class ControlManagedComponentManifestAttestationAuthority(
    IWorkspaceDeploymentStore deployments) : IComponentManifestAttestationAuthority
{
    public async ValueTask<ComponentManifestAttestationDecision> VerifyAsync(
        ComponentManifestAttestationRequest request,
        ComponentManifestAttestationEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var revision = await deployments.GetRevisionAsync(
            request.WorkspaceId, request.RevisionId, cancellationToken);
        var valid = revision is not null &&
                    revision.ApplicationId == request.ApplicationId &&
                    !string.IsNullOrWhiteSpace(revision.Commit) &&
                    string.Equals(revision.Commit, request.SourceRevision, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(request.BuildId) &&
                    FixedTimeEquals(request.ManifestDigest, evidence.ExpectedManifestDigest) &&
                    FixedTimeEquals(request.BuildId, evidence.ExpectedBuildId);
        return valid
            ? new ComponentManifestAttestationDecision(
                true,
                ManifestTrustMethod.ControlManagedBuildAttestation,
                HealingActorTypes.Control,
                "control-builder",
                HealingOwnershipReasonCodes.Succeeded)
            : new ComponentManifestAttestationDecision(
                false,
                ManifestTrustMethod.ControlManagedBuildAttestation,
                HealingActorTypes.Control,
                "control-builder",
                HealingOwnershipReasonCodes.AttestationRejected);
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
