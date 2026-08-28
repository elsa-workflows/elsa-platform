using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sync;

namespace ElsaControl.PackageCatalog.Core.Approvals;

public sealed class ApprovalService(IApprovalStore store, IPublicCatalogCacheInvalidator? publicCatalogCache = null)
{
    public Task<IReadOnlyList<Package>> ListPackagesAsync(CancellationToken cancellationToken = default) =>
        store.ListPackagesAsync(cancellationToken);

    public Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) =>
        store.GetPackageAsync(packageId, cancellationToken);

    public async Task<bool> SetPackageApprovalAsync(string packageId, PackageApprovalStatus status, string actor, string? reason = null, CancellationToken cancellationToken = default)
    {
        var package = await store.GetPackageAsync(packageId, cancellationToken);
        if (package is null)
            return false;

        package.Approved = status == PackageApprovalStatus.Approved;
        await store.AddApprovalRecordAsync(new ApprovalRecord
        {
            TargetType = ApprovalTargetType.Package,
            TargetId = package.Id,
            Status = status,
            Actor = actor,
            Reason = reason
        }, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        publicCatalogCache?.Invalidate();
        return true;
    }

    public async Task<bool> SetVersionApprovalAsync(string packageId, string version, PackageApprovalStatus status, string actor, string? reason = null, CancellationToken cancellationToken = default)
    {
        var packageVersion = await store.GetPackageVersionAsync(packageId, version, cancellationToken);
        if (packageVersion is null)
            return false;

        var result = await TrySetLoadedVersionApprovalAsync(packageVersion, status, actor, reason, CreateVersionStateToken(packageVersion), cancellationToken);
        return result == VersionApprovalUpdateResult.Updated;
    }

    public async Task<VersionApprovalUpdateResult> TrySetVersionApprovalAsync(string packageId, string version, PackageApprovalStatus status, string actor, string? reason = null, string? expectedStateToken = null, CancellationToken cancellationToken = default)
    {
        var packageVersion = await store.GetPackageVersionAsync(packageId, version, cancellationToken);
        if (packageVersion is null)
            return VersionApprovalUpdateResult.NotFound;

        if (string.IsNullOrWhiteSpace(expectedStateToken))
            return VersionApprovalUpdateResult.MissingStateToken;

        if (!string.Equals(CreateVersionStateToken(packageVersion), expectedStateToken, StringComparison.Ordinal))
            return VersionApprovalUpdateResult.Conflict;

        return await TrySetLoadedVersionApprovalAsync(packageVersion, status, actor, reason, expectedStateToken, cancellationToken);
    }

    private async Task<VersionApprovalUpdateResult> TrySetLoadedVersionApprovalAsync(PackageVersion packageVersion, PackageApprovalStatus status, string actor, string? reason, string expectedStateToken, CancellationToken cancellationToken)
    {
        if (!string.Equals(CreateVersionStateToken(packageVersion), expectedStateToken, StringComparison.Ordinal))
            return VersionApprovalUpdateResult.Conflict;

        var result = await store.TryUpdateVersionApprovalAsync(packageVersion, status, new ApprovalRecord
        {
            TargetType = ApprovalTargetType.PackageVersion,
            TargetId = packageVersion.Id,
            Status = status,
            Actor = actor,
            Reason = reason
        }, cancellationToken);

        if (result == VersionApprovalUpdateResult.Updated)
            publicCatalogCache?.Invalidate();

        return result;
    }

    public static string CreateVersionStateToken(PackageVersion version) =>
        $"{version.ApprovalStatus}:{version.ValidationStatus}:{version.IsListed}:{version.SuspiciousChangeDetected}:{version.ManifestHash}:{version.SuspiciousManifestHash}";
}

public enum VersionApprovalUpdateResult
{
    Updated,
    NotFound,
    MissingStateToken,
    Conflict
}

public interface IApprovalStore
{
    Task<IReadOnlyList<Package>> ListPackagesAsync(CancellationToken cancellationToken = default);
    Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default);
    Task<PackageVersion?> GetPackageVersionAsync(string packageId, string version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManifestValidationResultRecord>> GetValidationResultsAsync(PackageVersion packageVersion, CancellationToken cancellationToken = default);
    Task<VersionApprovalUpdateResult> TryUpdateVersionApprovalAsync(PackageVersion packageVersion, PackageApprovalStatus status, ApprovalRecord approvalRecord, CancellationToken cancellationToken = default);
    Task AddApprovalRecordAsync(ApprovalRecord record, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
