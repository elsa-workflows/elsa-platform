using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Api.ReleaseCatalog;

/// <summary>
/// Safe default for hosts that have not configured a real registry/signature verifier.
/// A release manifest can never be admitted by this implementation.
/// </summary>
internal sealed class FailClosedReleaseManifestSignatureVerifier : IReleaseManifestSignatureVerifier
{
    public ValueTask<ReleaseManifestSignatureVerification> VerifyAsync(
        ReleaseManifestArtifact artifact,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ReleaseManifestSignatureVerification(
            IsValid: false,
            Subject: "",
            SubjectDigest: "",
            EvidenceReference: "",
            EvidenceDigest: ""));
}
