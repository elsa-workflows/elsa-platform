using ValenceControl.PackageCatalog.Core.Packages;
using FluentAssertions;

namespace ValenceControl.PackageCatalog.Core.Tests;

public sealed class PackageVersionImmutabilityTests
{
    [Fact]
    public void CompareManifest_reports_changed_hash_without_mutating_existing_version()
    {
        var version = new PackageVersion { ManifestHash = "old" };
        var policy = new PackageVersionPolicy();

        var result = policy.CompareManifest(version, "new");

        result.IsSuspicious.Should().BeTrue();
        result.ObservedHash.Should().Be("new");
        version.ManifestHash.Should().Be("old");
        version.SuspiciousChangeDetected.Should().BeFalse();
        version.SuspiciousManifestHash.Should().BeNull();
    }
}
