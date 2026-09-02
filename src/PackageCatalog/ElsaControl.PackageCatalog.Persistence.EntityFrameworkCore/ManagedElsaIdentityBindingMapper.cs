using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Validates that a persisted identity binding is still derived from the
/// instance's current verified endpoint. All handoff consumers use this single
/// check so an old or malformed binding cannot become authoritative in one path.
/// </summary>
internal static class ManagedElsaIdentityBindingMapper
{
    public static bool TryMapCurrent(
        ElsaInstanceEntity entity,
        out ElsaInstanceIdentityBinding? binding,
        out Uri? callbackUri)
    {
        binding = null;
        callbackUri = null;
        var persisted = entity.IdentityBinding;
        if (persisted is null ||
            !ElsaManagedEndpointOrigin.TryCreate(entity.CurrentDeploymentEndpointUri, out var endpointOrigin))
            return false;

        try
        {
            var current = ElsaInstanceIdentityBinding.Hydrate(
                entity.Id,
                endpointOrigin.Value,
                persisted.BindingVersion,
                persisted.ChangedAt);
            if (!string.Equals(persisted.Audience, current.Audience, StringComparison.Ordinal) ||
                !string.Equals(persisted.CanonicalCallbackUri, current.CanonicalCallbackUri, StringComparison.Ordinal) ||
                !string.Equals(persisted.VerifiedEndpointOrigin, current.VerifiedEndpointOrigin, StringComparison.Ordinal) ||
                !Uri.TryCreate(current.CanonicalCallbackUri, UriKind.Absolute, out callbackUri))
                return false;

            binding = current;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
