using Elsa.Platform.PackageCatalog.Core.Approvals;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Testing;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class ApprovalPolicyTests
{
    private readonly ApprovalPolicy _policy = new();

    [Fact]
    public void Auto_approve_sources_create_approved_package_and_version_state()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;

        _policy.GetInitialPackageApproved(source).Should().BeTrue();
        _policy.GetInitialVersionStatus(source).Should().Be(PackageApprovalStatus.Approved);
    }

    [Fact]
    public void Manual_sources_create_pending_version_state()
    {
        var source = PublicCatalogSeedData.CreatePackageSource();
        source.ApprovalPolicy = PackageSourceApprovalPolicy.Manual;

        _policy.GetInitialPackageApproved(source).Should().BeFalse();
        _policy.GetInitialVersionStatus(source).Should().Be(PackageApprovalStatus.Pending);
    }
}
