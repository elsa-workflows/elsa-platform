namespace ElsaControl.PackageCatalog.Core.Packages;

public sealed class PackageVersionPolicy
{
    public PackageVersionContentChange CompareManifest(PackageVersion existingVersion, string observedManifestHash)
    {
        if (string.Equals(existingVersion.ManifestHash, observedManifestHash, StringComparison.OrdinalIgnoreCase))
            return new PackageVersionContentChange(false, null);

        return new PackageVersionContentChange(true, observedManifestHash);
    }
}
