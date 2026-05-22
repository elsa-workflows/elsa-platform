using Elsa.Platform.PackageCatalog.Core.Accounts;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public static class WorkspaceAuthorization
{
    public static IResult ToHttpResult(this WorkspaceAccessResult result) =>
        result.Failure switch
        {
            WorkspaceAccessFailure.MissingIdentity => WorkspaceIdentityHttpContextExtensions.UnauthorizedWorkspaceIdentity(),
            WorkspaceAccessFailure.WorkspaceNotAllowed => Results.Problem(
                title: "Access to this workspace is not allowed.",
                statusCode: StatusCodes.Status403Forbidden),
            WorkspaceAccessFailure.RoleNotAllowed => Results.Problem(
                title: "Workspace role does not allow this operation.",
                statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Problem(
                title: "Workspace access could not be resolved.",
                statusCode: StatusCodes.Status403Forbidden)
        };
}
