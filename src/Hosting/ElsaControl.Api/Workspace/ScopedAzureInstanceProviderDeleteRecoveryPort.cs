using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Runs an explicitly accepted provider-delete recovery in a child scope. The deletion worker
/// owns the lifecycle lease in its scope; the provider operation store and executor used for the
/// recovery must instead get an independent <see cref="CatalogDbContext"/> lifetime.
/// </summary>
internal sealed class ScopedAzureInstanceProviderDeleteRecoveryPort(IServiceScopeFactory scopeFactory) :
    IElsaInstanceProviderDeleteRecoveryPort
{
    public async Task<ElsaInstanceCleanupObservation> RecoverDeleteAsync(
        ElsaInstanceDeleteRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var scope = scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<AzureElsaInstanceProvider>();
        return await provider.RecoverDeleteAsync(request, cancellationToken);
    }
}
