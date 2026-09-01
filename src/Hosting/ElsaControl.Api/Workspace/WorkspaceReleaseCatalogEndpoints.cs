using ElsaControl.Api.Authentication;
using ElsaControl.Api.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;
using Microsoft.AspNetCore.Mvc;

namespace ElsaControl.Api.Workspace;

public static class WorkspaceReleaseCatalogEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceReleaseCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/release-catalog")
            .WithTags("Workspace Release Catalog");

        group.MapGet("/", async (
            [FromQuery] string? distributionId,
            [FromQuery] string? releaseLine,
            [FromQuery] string? releaseVersion,
            [FromQuery] string? channel,
            [FromQuery] string? lifecycle,
            [FromQuery] string? producerLifecycle,
            [FromQuery] string? registryClass,
            [FromQuery] string? topologyId,
            [FromQuery] string? runtimeKind,
            [FromQuery] string? capability,
            IGovernedReleaseCatalogStore catalog,
            CancellationToken cancellationToken) =>
        {
            var entries = await catalog.QueryAsync(
                new GovernedReleaseCatalogQuery(
                    distributionId,
                    releaseLine,
                    releaseVersion,
                    channel,
                    ProducerLifecycle: producerLifecycle,
                    CatalogLifecycle: lifecycle,
                    RegistryClass: registryClass,
                    TopologyId: topologyId,
                    RuntimeKind: runtimeKind,
                    Capability: capability),
                cancellationToken);
            return Results.Ok(entries.Select(x => ReleaseCatalogApiMappings.ToResponse(x)).ToArray());
        }).RequireWorkspaceAccess();

        return endpoints;
    }
}
