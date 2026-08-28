using ElsaControl.PackageCatalog.Core.Packages;

namespace ElsaControl.PackageCatalog.Core.Tests;

public sealed class PackageVersionImmutabilityTests
{
    [Fact]
    public void CompareManifest_reports_changed_hash_without_mutating_existing_version()
    {
        var version = new PackageVersion { ManifestHash = "old" };
        var policy = new PackageVersionPolicy();

        var result = policy.CompareManifest(version, "new");

        Assert.True(result.IsSuspicious);
        Assert.Equal("new", result.ObservedHash);
        Assert.Equal("old", version.ManifestHash);
        Assert.False(version.SuspiciousChangeDetected);
        Assert.Null(version.SuspiciousManifestHash);
    }
}
