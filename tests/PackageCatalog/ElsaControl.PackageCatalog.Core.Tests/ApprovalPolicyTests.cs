using ElsaControl.PackageCatalog.Core.Approvals;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Testing;

namespace ElsaControl.PackageCatalog.Core.Tests;

public sealed class ApprovalPolicyTests
{
    private readonly ApprovalPolicy _policy = new();

    [Fact]
    public void Auto_approve_sources_create_approved_package_and_version_state()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;

        Assert.True(_policy.GetInitialPackageApproved(source));
        Assert.Equal(PackageApprovalStatus.Approved, _policy.GetInitialVersionStatus(source));
    }

    [Fact]
    public void Manual_sources_create_pending_version_state()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.Manual;

        Assert.False(_policy.GetInitialPackageApproved(source));
        Assert.Equal(PackageApprovalStatus.Pending, _policy.GetInitialVersionStatus(source));
    }
}
