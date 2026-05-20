using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Core.Sources;

namespace Elsa.Platform.PackageCatalog.Core.Accounts;

public sealed class WorkspaceSourceService(IAccountWorkspaceStore store, PackageSourceValidator validator)
{
    public async Task<WorkspaceSourceResult> CreateSourceAsync(WorkspaceAccess access, WorkspaceSourceCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (!access.CanAdministerSources)
            return WorkspaceSourceResult.Forbidden("Workspace source administrator role is required.");

        var entitlement = await store.GetLatestEntitlementAsync(access.WorkspaceId, cancellationToken);
        if (entitlement is null || !entitlement.CanCreateCustomSources)
            return WorkspaceSourceResult.Forbidden("Workspace is not entitled to create custom sources.");

        if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) && HasUnsupportedCredentials(uri))
            return WorkspaceSourceResult.Invalid(["Private feed credentials in source URLs are not supported yet."]);

        var source = new PackageSource
        {
            Name = request.Name,
            Url = request.Url,
            Enabled = request.Enabled,
            Browseable = true,
            Visibility = PackageSourceVisibility.Workspace,
            OwnerWorkspaceId = access.WorkspaceId,
            IncludePatterns = request.IncludePatterns.ToList(),
            ExcludePatterns = request.ExcludePatterns.ToList(),
            ApprovalPolicy = PackageSourceApprovalPolicy.Manual,
            VersionDiscoveryPolicy = request.VersionDiscoveryPolicy
        };
        var validation = validator.Validate(source);
        if (!validation.IsValid)
            return WorkspaceSourceResult.Invalid(validation.Errors);

        source.CreatedAt = DateTimeOffset.UtcNow;
        source.UpdatedAt = source.CreatedAt;
        var addResult = await store.TryAddWorkspaceSourceAsync(source, entitlement.MaxSources, cancellationToken);
        return addResult.Status switch
        {
            WorkspaceSourceAddStatus.Created => WorkspaceSourceResult.Success(source),
            WorkspaceSourceAddStatus.LimitReached => WorkspaceSourceResult.Forbidden("Workspace custom source limit has been reached."),
            WorkspaceSourceAddStatus.DuplicateUrl => WorkspaceSourceResult.Invalid(["A source with this URL already exists in the workspace."]),
            _ => throw new InvalidOperationException($"Unsupported workspace source add status '{addResult.Status}'.")
        };
    }

    private static bool HasUnsupportedCredentials(Uri uri) =>
        !string.IsNullOrWhiteSpace(uri.UserInfo) ||
        !string.IsNullOrWhiteSpace(uri.Query);
}

public sealed record WorkspaceSourceCreateRequest(
    string Name,
    string Url,
    bool Enabled,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns,
    PackageSourceVersionDiscoveryPolicy VersionDiscoveryPolicy);

public sealed record WorkspaceSourceResult(PackageSource? Source, IReadOnlyList<string> Errors, bool ForbiddenResult)
{
    public bool Succeeded => Source is not null && Errors.Count == 0 && !ForbiddenResult;
    public static WorkspaceSourceResult Success(PackageSource source) => new(source, [], false);
    public static WorkspaceSourceResult Invalid(IReadOnlyList<string> errors) => new(null, errors, false);
    public static WorkspaceSourceResult Forbidden(string error) => new(null, [error], true);
}
