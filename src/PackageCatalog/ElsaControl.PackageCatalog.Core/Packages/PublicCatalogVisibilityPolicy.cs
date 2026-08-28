namespace ElsaControl.PackageCatalog.Core.Packages;

public sealed class PublicCatalogVisibilityPolicy
{
    public bool IsVisible(Package package, PackageVersion version)
    {
        return package.Listed
               && version.IsListed
               && package.Approved
               && version.ApprovalStatus == PackageApprovalStatus.Approved
               && version.ValidationStatus == ValidationStatus.Valid
               && !version.SuspiciousChangeDetected;
    }
}
