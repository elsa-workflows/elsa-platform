namespace Elsa.Platform.Deployment.Artifacts;

public static class ArtifactDiagnosticCodes
{
    public const string LayoutUnsupported = "artifact.layout.unsupported";
    public const string MetadataRequired = "artifact.metadata.required";
    public const string ManifestRequired = "artifact.manifest.required";
    public const string PathInvalid = "artifact.path.invalid";
    public const string PathDuplicate = "artifact.path.duplicate";
    public const string PayloadMissing = "artifact.payload.missing";
    public const string PayloadUnexpected = "artifact.payload.unexpected";
    public const string ChecksumMissing = "artifact.checksum.missing";
    public const string ChecksumMismatch = "artifact.checksum.mismatch";
    public const string IdentityMismatch = "artifact.identity.mismatch";
    public const string ArchiveInvalid = "artifact.archive.invalid";
    public const string ReadFailed = "artifact.read.failed";
    public const string BuildFailed = "artifact.build.failed";
}
