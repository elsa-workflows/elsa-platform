using System.Net;
using ElsaControl.Api.Authentication;
using ElsaControl.Api.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.RuntimeBuilder.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceAuthorizationTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public WorkspaceAuthorizationTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Reader_can_list_workspace_sources_but_cannot_create_source()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        await AddMemberAsync(app, workspaceId, "reader", WorkspaceRole.Reader);

        var reader = app.CreateControlIdentityClient(subject: "reader");
        var list = await reader.GetAsync($"/api/workspaces/{workspaceId}/sources");
        var create = await reader.PostControlJsonAsync($"/api/workspaces/{workspaceId}/sources", SourceRequest());

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Reader_can_list_runtime_configurations_but_cannot_mutate_them()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        await AddMemberAsync(app, workspaceId, "reader", WorkspaceRole.Reader);

        var reader = app.CreateControlIdentityClient(subject: "reader");
        var list = await reader.GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations");
        var create = await reader.PostControlJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", RuntimeConfigurationRequest());

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Workspace_role_changes_are_enforced_on_subsequent_requests()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        await AddMemberAsync(app, workspaceId, "member", WorkspaceRole.SourceAdmin);
        var member = app.CreateControlIdentityClient(subject: "member");

        var sourceAdminResponse = await member.PostControlJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", RuntimeConfigurationRequest("Before downgrade"));
        await SetRoleAsync(app, workspaceId, "member", WorkspaceRole.Reader);
        var readerResponse = await member.PostControlJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", RuntimeConfigurationRequest("After downgrade"));

        Assert.Equal(HttpStatusCode.OK, sourceAdminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readerResponse.StatusCode);
    }

    private static async Task<Guid> GetWorkspaceIdAsync(HttpClient client) =>
        (await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

    private static WorkspaceSourceRequest SourceRequest() =>
        new("Private Feed", "https://private.example.test/v3/index.json", true, ["Elsa.*"], [], PackageSourceVersionDiscoveryPolicy.AllVersions);

    private static WorkspaceRuntimeConfigurationRequest RuntimeConfigurationRequest(string name = "Runtime") =>
        new(name, null, new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages")));

    private static async Task AddMemberAsync(ControlApiTestApplication app, Guid workspaceId, string subject, WorkspaceRole role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = new Account
        {
            DisplayName = subject,
            Email = $"{subject}@example.test"
        };
        account.ExternalIdentities.Add(new ExternalIdentity
        {
            Account = account,
            Issuer = ControlApiTestApplication.TestControlIdentityIssuer,
            Subject = subject,
            DisplayName = subject,
            Email = $"{subject}@example.test"
        });
        var workspace = await db.Workspaces.SingleAsync(x => x.Id == workspaceId);
        account.OrganizationMemberships.Add(new OrganizationMembership
        {
            Account = account,
            OrganizationId = workspace.OrganizationId,
            Role = OrganizationRole.Member
        });
        account.Memberships.Add(new WorkspaceMembership
        {
            Account = account,
            Workspace = workspace,
            Role = role
        });
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
    }

    private static async Task SetRoleAsync(ControlApiTestApplication app, Guid workspaceId, string subject, WorkspaceRole role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var membership = db.WorkspaceMemberships.Single(x =>
            x.WorkspaceId == workspaceId &&
            x.Account!.ExternalIdentities.Any(identity => identity.Subject == subject));
        membership.Role = role;
        await db.SaveChangesAsync();
    }
}
