namespace ValenceControl.Deployment.Manifest;

public interface IManifestReader
{
    ManifestParseResult Read(string text, ManifestFormat format);
}
