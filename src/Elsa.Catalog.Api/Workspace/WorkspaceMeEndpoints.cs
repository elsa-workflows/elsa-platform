using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Core.Accounts;

namespace Elsa.Catalog.Api.Workspace;

public static class WorkspaceMeEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/workspaces", async (
            HttpContext context,
            IWorkspaceIdentityReader identityReader,
            AccountWorkspaceService accounts,
            CancellationToken cancellationToken) =>
        {
            var identity = identityReader.Read(context);
            if (identity is null)
                return WorkspaceIdentityHttpContextExtensions.UnauthorizedWorkspaceIdentity();

            var accountContext = await accounts.GetOrCreateAsync(identity, cancellationToken);
            return Results.Ok(ToResponse(accountContext));
        }).WithTags("Workspace Identity");

        return endpoints;
    }

    public static MeWorkspacesResponse ToResponse(AccountWorkspaceContext context) =>
        new(
            new AccountContextResponse(context.Account.Id, context.Account.DisplayName, context.Account.Email),
            context.Workspaces.Select(x => new WorkspaceContextResponse(x.Id, x.Name, x.Kind, x.Role)).ToList());
}
