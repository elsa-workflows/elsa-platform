using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Reads only the safe instance projection needed by the Control console. A
/// malformed or stale identity binding is intentionally returned without an
/// audience/callback so callers cannot invent one from an endpoint URL.
/// </summary>
public sealed class EfCoreManagedElsaInstanceCatalog(CatalogDbContext dbContext) : IManagedElsaInstanceCatalog
{
    public async Task<IReadOnlyList<ManagedElsaInstanceSummary>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            return [];

        var instances = await dbContext.ElsaInstances
            .AsNoTracking()
            .Include(x => x.IdentityBinding)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return instances.Select(Map).ToList();
    }

    private static ManagedElsaInstanceSummary Map(ElsaInstanceEntity entity)
    {
        string? audience = null;
        Uri? callbackUri = null;
        int? bindingVersion = null;
        var binding = entity.IdentityBinding;
        if (binding is not null &&
            Uri.TryCreate(entity.CurrentDeploymentEndpointUri, UriKind.Absolute, out var endpoint))
        {
            try
            {
                var current = ElsaInstanceIdentityBinding.Hydrate(
                    entity.Id,
                    endpoint.GetLeftPart(UriPartial.Authority),
                    binding.BindingVersion,
                    binding.ChangedAt);
                if (string.Equals(binding.Audience, current.Audience, StringComparison.Ordinal) &&
                    string.Equals(binding.CanonicalCallbackUri, current.CanonicalCallbackUri, StringComparison.Ordinal) &&
                    string.Equals(binding.VerifiedEndpointOrigin, current.VerifiedEndpointOrigin, StringComparison.Ordinal) &&
                    Uri.TryCreate(current.CanonicalCallbackUri, UriKind.Absolute, out callbackUri))
                {
                    audience = current.Audience;
                    bindingVersion = current.BindingVersion;
                }
                else
                {
                    callbackUri = null;
                }
            }
            catch (ArgumentException)
            {
                // Keep the status visible for diagnostics, but never expose a
                // caller-controlled binding from malformed persistence.
            }
        }

        return new ManagedElsaInstanceSummary(
            entity.OrganizationId,
            entity.WorkspaceId,
            entity.Id,
            entity.Name,
            entity.Slug,
            entity.DesiredLifecycle,
            entity.ObservedLifecycle,
            entity.Health,
            audience,
            callbackUri,
            bindingVersion);
    }
}
