namespace Elsa.Platform.Deployment.Manifest;

public static class ManifestDiagnosticCodes
{
    public const string Parse = "manifest.parse";
    public const string ApiVersionRequired = "manifest.apiVersion.required";
    public const string ApiVersionUnsupported = "manifest.apiVersion.unsupported";
    public const string KindRequired = "manifest.kind.required";
    public const string KindUnsupported = "manifest.kind.unsupported";
    public const string MetadataNameRequired = "manifest.metadata.name.required";
    public const string ResourceIdentityRequired = "manifest.resource.identity.required";
    public const string ResourceDuplicate = "manifest.resource.duplicate";
    public const string ResourcePathInvalid = "manifest.resource.path.invalid";
    public const string ResourceUnsupported = "manifest.resource.unsupported";
}
