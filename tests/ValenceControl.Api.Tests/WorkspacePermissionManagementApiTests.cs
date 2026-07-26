using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.PackageCatalog.Core.Accounts;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

public sealed class WorkspacePermissionManagementApiTests
{
    [Fact]
    public async Task Owner_manages_contributed_permissions_with_idempotent_audited_mutations()
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

        (await reader.GetAsync(grantsUri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var grantRequest = new WorkspacePermissionGrantRequest(readerId, HealingPermissions.Read);
        var granted = await owner.PostControlJsonAsync(grantsUri, grantRequest);
        var replayedGrant = await owner.PostControlJsonAsync(grantsUri, grantRequest);

        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        replayedGrant.StatusCode.Should().Be(HttpStatusCode.OK);
        var grants = await owner.GetFromJsonAsync<JsonElement>($"{grantsUri}?accountId={readerId:D}");
        grants.GetProperty("items").EnumerateArray()
            .Should().ContainSingle(x => x.GetProperty("permission").GetString() == HealingPermissions.Read && x.GetProperty("revokedAt").ValueKind == JsonValueKind.Null);

        var revokeRequest = new WorkspacePermissionRevokeRequest(readerId, HealingPermissions.Read);
        var revoked = await owner.PostControlJsonAsync(revocationsUri, revokeRequest);
        var replayedRevoke = await owner.PostControlJsonAsync(revocationsUri, revokeRequest);

        (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean().Should().BeTrue();
        (await replayedRevoke.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetBoolean().Should().BeFalse();
        var audit = await owner.GetFromJsonAsync<JsonElement>($"/api/workspaces/{workspaceId:D}/permissions/audit?accountId={readerId:D}");
        audit.GetProperty("items").EnumerateArray().Should().HaveCount(2);
        audit.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("action").GetString())
            .Should().BeEquivalentTo("Granted", "Revoked");

        var outsider = app.CreateTrustedWorkspaceClient("permission-outsider");
        var outsiderContext = await outsider.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var outsiderGrant = await owner.PostControlJsonAsync(
            grantsUri,
            new WorkspacePermissionGrantRequest(outsiderContext!.Account.Id, HealingPermissions.Read));

        outsiderGrant.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await outsider.GetAsync(grantsUri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

        firstRead.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).Should().NotContain(permission);
        secondRead.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).Should().NotContain(permission);
        audit.GetProperty("items").EnumerateArray()
            .Count(x => x.GetProperty("permission").GetString() == permission)
            .Should().Be(2);
    }
}
