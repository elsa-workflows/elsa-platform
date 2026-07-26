using System.Text.Json.Serialization;

namespace ValenceControl.Deployment.Artifacts;

public enum DeploymentArtifactFormat
{
    Folder,
    Zip
}

[JsonConverter(typeof(JsonStringEnumConverter))]
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
