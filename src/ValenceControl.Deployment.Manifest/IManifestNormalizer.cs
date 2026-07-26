namespace ValenceControl.Deployment.Manifest;

public interface IManifestNormalizer
{
    NormalizedManifest Normalize(EnvironmentManifest manifest, ManifestResourceMapperRegistry? mapperRegistry = null);
}
