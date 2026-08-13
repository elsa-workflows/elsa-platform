namespace ValenceControl.Deployment.Artifacts;

public static class ArtifactLayoutConstants
{
    public const string LayoutVersion = "valence-control/deployment-artifact/v1alpha1";
    public const string ChecksumAlgorithm = "sha256";
    public const string MetadataPath = "artifact.json";
    public const string ChecksumInventoryPath = "checksums.json";
    public const string ManifestDirectory = "manifest";
    public const string PayloadDirectory = "payload";
}
