using ValenceControl.PackageCatalog.Core.Packages;

namespace ValenceControl.PackageCatalog.Core.Tests;

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

        Assert.True(_policy.IsVisible(package, version));

        version.SuspiciousChangeDetected = true;
        Assert.False(_policy.IsVisible(package, version));
    }
}
