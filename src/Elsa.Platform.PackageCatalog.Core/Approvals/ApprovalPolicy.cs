using Elsa.Platform.PackageCatalog.Core.Packages;

namespace Elsa.Platform.PackageCatalog.Core.Approvals;

public sealed class ApprovalPolicy
{
    public PackageApprovalStatus GetInitialVersionStatus(PackageSource source) =>
        source.ApprovalPolicy == PackageSourceApprovalPolicy.AutoApprove
            ? PackageApprovalStatus.Approved
            : PackageApprovalStatus.Pending;

    public bool GetInitialPackageApproved(PackageSource source) =>
        source.ApprovalPolicy == PackageSourceApprovalPolicy.AutoApprove;
}
