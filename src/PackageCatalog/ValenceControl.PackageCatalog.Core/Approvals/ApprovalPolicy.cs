using ValenceControl.PackageCatalog.Core.Packages;

namespace ValenceControl.PackageCatalog.Core.Approvals;

public sealed class ApprovalPolicy
{
    public PackageApprovalStatus GetInitialVersionStatus(PackageSource source) =>
        source.ApprovalPolicy == PackageSourceApprovalPolicy.AutoApprove
            ? PackageApprovalStatus.Approved
            : PackageApprovalStatus.Pending;

    public bool GetInitialPackageApproved(PackageSource source) =>
        source.ApprovalPolicy == PackageSourceApprovalPolicy.AutoApprove;
}
