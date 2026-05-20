namespace Elsa.Platform.Deployment.Artifacts;

public enum DeploymentArtifactFormat
{
    Folder,
    Zip
}

public enum DeploymentArtifactEntryKind
{
    Metadata,
    Manifest,
    ChecksumInventory,
    Payload
}

public enum DeploymentArtifactChecksumStatus
{
    Verified,
    Missing,
    Mismatched,
    Unexpected
}
