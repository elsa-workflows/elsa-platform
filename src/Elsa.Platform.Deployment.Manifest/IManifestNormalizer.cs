namespace Elsa.Platform.Deployment.Manifest;

public interface IManifestNormalizer
{
    NormalizedManifest Normalize(EnvironmentManifest manifest, ManifestResourceMapperRegistry? mapperRegistry = null);
}
