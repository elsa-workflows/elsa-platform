using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;

namespace ValenceControl.Api.Tests;

public sealed class WorkspacePermissionManagementApiTests
{
    [Fact]
    public async Task Owner_manages_member_permissions_with_idempotent_audited_mutations()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("permission-owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var workspaceId = ownerContext!.Workspaces.Single().Id;
        var readerId = await app.AddWorkspaceMemberAsync(workspaceId, "permission-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("permission-reader");
        var grantsUri = $"/api/workspaces/{workspaceId:D}/permissions/grants";
        var revocationsUri = $"/api/workspaces/{workspaceId:D}/permissions/revocations";

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(grantsUri)).StatusCode);

        var grantRequest = new WorkspacePermissionGrantRequest(readerId, WorkspaceDeploymentPermissions.Read);
        var granted = await owner.PostControlJsonAsync(grantsUri, grantRequest);
        var replayedGrant = await owner.PostControlJsonAsync(grantsUri, grantRequest);

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayedGrant.StatusCode);
        var grants = await owner.GetFromJsonAsync<JsonElement>($"{grantsUri}?accountId={readerId:D}");
        Assert.Single(grants.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("permission").GetString() == WorkspaceDeploymentPermissions.Read && x.GetProperty("revokedAt").ValueKind == JsonValueKind.Null);

        var revokeRequest = new WorkspacePermissionRevokeRequest(readerId, WorkspaceDeploymentPermissions.Read);
        var revoked = await owner.PostControlJsonAsync(revocationsUri, revokeRequest);
        var replayedRevoke = await owner.PostControlJsonAsync(revocationsUri, revokeRequest);

        Assert.True((await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        Assert.False((await replayedRevoke.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean());
        var audit = await owner.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId:D}/permissions/audit?accountId={readerId:D}");
        Assert.Equal(2, audit.GetProperty("items").EnumerateArray().Count());
        Assert.Equivalent(
            new string?[] { "Granted", "Revoked" },
            audit.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("action").GetString()));

        var outsider = app.CreateTrustedWorkspaceClient("permission-outsider");
        var outsiderContext = await outsider.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var outsiderGrant = await owner.PostControlJsonAsync(
            grantsUri,
            new WorkspacePermissionGrantRequest(outsiderContext!.Account.Id, WorkspaceDeploymentPermissions.Read));

        Assert.Equal(HttpStatusCode.BadRequest, outsiderGrant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(grantsUri)).StatusCode);
    }

    [Fact]
    public async Task Owner_authorization_reads_do_not_restore_a_revoked_default_permission()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("permission-revoked-owner");
        var context = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var workspaceId = context!.Workspaces.Single().Id;
        var permission = WorkspaceDeploymentPermissions.Read;

        var revoked = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/permissions/revocations",
            new WorkspacePermissionRevokeRequest(context.Account.Id, permission));
        revoked.EnsureSuccessStatusCode();

        (await owner.GetAsync("/api/me/workspaces")).EnsureSuccessStatusCode();
        (await owner.GetAsync("/api/me/organizations")).EnsureSuccessStatusCode();
        var firstRead = await owner.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId:D}/deployments/permissions");
        var secondRead = await owner.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId:D}/deployments/permissions");
        var audit = await owner.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId:D}/permissions/audit?accountId={context.Account.Id:D}");

        Assert.DoesNotContain(permission, firstRead.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain(permission, secondRead.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(2, audit.GetProperty("items").EnumerateArray()
            .Count(x => x.GetProperty("permission").GetString() == permission));
    }
}
