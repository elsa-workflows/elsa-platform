namespace ElsaControl.Deployment.Manifest;

public interface IManifestReader
{
    ManifestParseResult Read(string text, ManifestFormat format);
}
