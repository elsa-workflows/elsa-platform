using System.Security.Cryptography;
using System.Text;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Manifests;
using Elsa.Platform.Healing.Core.Ownership;

namespace Elsa.Platform.Api.Healing;

internal sealed class PlatformManagedComponentManifestAttestationAuthority(
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
                ManifestTrustMethod.PlatformManagedBuildAttestation,
                HealingActorTypes.Platform,
                "platform-builder",
                HealingOwnershipReasonCodes.Succeeded)
            : new ComponentManifestAttestationDecision(
                false,
                ManifestTrustMethod.PlatformManagedBuildAttestation,
                HealingActorTypes.Platform,
                "platform-builder",
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
