using Elsa.Platform.PackageCatalog.Core.Packages;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class PublicCatalogVisibilityTests
{
    private readonly PublicCatalogVisibilityPolicy _policy = new();

    [Fact]
    public void IsVisible_requires_package_version_to_be_listed_approved_valid_and_not_suspicious()
    {
        var package = new Package { Approved = true, Listed = true };
        var version = new PackageVersion
        {
            ApprovalStatus = PackageApprovalStatus.Approved,
            IsListed = true,
            ValidationStatus = ValidationStatus.Valid
        };

        _policy.IsVisible(package, version).Should().BeTrue();

        version.SuspiciousChangeDetected = true;
        _policy.IsVisible(package, version).Should().BeFalse();
    }
}
