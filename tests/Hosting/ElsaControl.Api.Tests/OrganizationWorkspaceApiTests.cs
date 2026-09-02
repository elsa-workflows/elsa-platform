using System.Net;
using System.Net.Http.Json;
using ElsaControl.Api.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class OrganizationWorkspaceApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public OrganizationWorkspaceApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Owner_can_create_and_list_organization_workspaces()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var context = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddEntitlementAsync(app, context!.Organizations.Single().Id, maxWorkspaces: 3);

        var create = await owner.PostControlJsonAsync(
            OrganizationWorkspacesPath(context.Organizations.Single().Id),
            new OrganizationWorkspaceCreateRequest("Automation"));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var workspace = await create.Content.ReadControlJsonAsync<WorkspaceContextResponse>();
        Assert.Equal(WorkspaceKind.Shared, workspace!.Kind);
        Assert.Equal(WorkspaceRole.Owner, workspace.Role);

        var list = await owner.GetControlJsonAsync<OrganizationWorkspacesResponse>(OrganizationWorkspacesPath(context.Organizations.Single().Id));
        var names = list!.Workspaces.Select(x => x.Name);
        Assert.All(["Ada Lovelace", "Automation"], name => Assert.Contains(name, names));
    }

    [Fact]
    public async Task Duplicate_active_workspace_name_is_rejected()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 3);
        await owner.PostControlJsonAsync(OrganizationWorkspacesPath(organizationId), new OrganizationWorkspaceCreateRequest("Automation"));

        var duplicate = await owner.PostControlJsonAsync(OrganizationWorkspacesPath(organizationId), new OrganizationWorkspaceCreateRequest("Automation"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Concurrent_workspace_creates_cannot_exceed_organization_workspace_limit()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);

        var responses = await Task.WhenAll(Enumerable.Range(1, 4)
            .Select(index => owner.PostControlJsonAsync(
                OrganizationWorkspacesPath(organizationId),
                new OrganizationWorkspaceCreateRequest($"Automation {index}"))));

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(x => x.StatusCode == HttpStatusCode.Forbidden));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var activeWorkspaces = await db.Workspaces.CountAsync(x => x.OrganizationId == organizationId && x.SoftDeletedAt == null);
        Assert.Equal(2, activeWorkspaces);
    }

    [Fact]
    public async Task Concurrent_workspace_creates_cannot_create_duplicate_active_names()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => owner.PostControlJsonAsync(
                OrganizationWorkspacesPath(organizationId),
                new OrganizationWorkspaceCreateRequest("Automation"))));

        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var matchingWorkspaces = await db.Workspaces.CountAsync(x =>
            x.OrganizationId == organizationId &&
            x.SoftDeletedAt == null &&
            x.Name == "Automation");
        Assert.Equal(1, matchingWorkspaces);
    }

    [Fact]
    public async Task Owner_can_rename_and_archive_workspace()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);
        var workspace = await CreateWorkspaceAsync(owner, organizationId, "Automation");

        var rename = await PatchControlJsonAsync(
            owner,
            $"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}",
            new OrganizationWorkspaceUpdateRequest("Operations", WorkspaceLifecycleStatus.Active));
        var archive = await PatchControlJsonAsync(
            owner,
            $"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}",
            new OrganizationWorkspaceUpdateRequest("Operations", WorkspaceLifecycleStatus.Archived));
        var list = await owner.GetControlJsonAsync<OrganizationWorkspacesResponse>(OrganizationWorkspacesPath(organizationId));

        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.DoesNotContain("Operations", list!.Workspaces.Select(x => x.Name));
    }

    [Fact]
    public async Task Organization_member_lists_only_assigned_workspaces()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var organizationId = ownerContext!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 4);
        var memberAccountId = await AddOrganizationMemberAsync(app, organizationId, "member", OrganizationRole.Member);

        await owner.PostControlJsonAsync(
            OrganizationWorkspacesPath(organizationId),
            new OrganizationWorkspaceCreateRequest("Visible", [new OrganizationWorkspaceInitialMemberRequest(memberAccountId, WorkspaceRole.Reader)]));
        await owner.PostControlJsonAsync(
            OrganizationWorkspacesPath(organizationId),
            new OrganizationWorkspaceCreateRequest("Hidden"));

        var member = app.CreateControlIdentityClient(subject: "member");
        var list = await member.GetControlJsonAsync<OrganizationWorkspacesResponse>(OrganizationWorkspacesPath(organizationId));

        Assert.Equal("Visible", Assert.Single(list!.Workspaces).Name);
    }

    [Fact]
    public async Task Other_organization_owner_cannot_list_workspaces()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var other = app.CreateControlIdentityClient(subject: "other");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await other.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        var response = await other.GetAsync(OrganizationWorkspacesPath(organizationId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Organization_membership_alone_does_not_authorize_workspace_resources()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddOrganizationMemberAsync(app, ownerContext!.Organizations.Single().Id, "member", OrganizationRole.Member);

        var member = app.CreateControlIdentityClient(subject: "member");
        var response = await member.GetAsync($"/api/workspaces/{ownerContext.Workspaces.Single().Id}/sources");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_membership_target_must_be_organization_member()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);
        var workspace = await CreateWorkspaceAsync(owner, organizationId, "Automation");
        var outsideAccountId = await AddAccountAsync(app, "outside");

        var response = await owner.PutControlJsonAsync(
            $"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}/members/{outsideAccountId}",
            new OrganizationWorkspaceMembershipRequest(WorkspaceRole.Reader));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_owner_can_be_removed_when_another_owner_exists()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var organizationId = ownerContext!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);
        var workspace = await CreateWorkspaceAsync(owner, organizationId, "Automation");
        var secondOwnerAccountId = await AddOrganizationMemberAsync(app, organizationId, "second-owner", OrganizationRole.Member);
        await owner.PutControlJsonAsync(
            $"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}/members/{secondOwnerAccountId}",
            new OrganizationWorkspaceMembershipRequest(WorkspaceRole.Owner));

        var response = await owner.DeleteAsync($"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}/members/{ownerContext.Account.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Removing_the_only_workspace_owner_is_rejected()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var organizationId = ownerContext!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);
        var workspace = await CreateWorkspaceAsync(owner, organizationId, "Automation");

        var response = await owner.DeleteAsync($"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}/members/{ownerContext.Account.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static string OrganizationWorkspacesPath(Guid organizationId) =>
        $"/api/organizations/{organizationId}/workspaces";

    private static async Task<WorkspaceContextResponse> CreateWorkspaceAsync(HttpClient owner, Guid organizationId, string name)
    {
        var response = await owner.PostControlJsonAsync(OrganizationWorkspacesPath(organizationId), new OrganizationWorkspaceCreateRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceContextResponse>())!;
    }

    private static Task<HttpResponseMessage> PatchControlJsonAsync<T>(HttpClient client, string requestUri, T value) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, requestUri)
        {
            Content = JsonContent.Create(value, options: ControlApiTestApplication.JsonOptions)
        });

    private static async Task AddEntitlementAsync(ControlApiTestApplication app, Guid organizationId, int maxWorkspaces)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.OrganizationEntitlementSnapshots.Add(new OrganizationEntitlementSnapshot
        {
            OrganizationId = organizationId,
            MaxWorkspaces = maxWorkspaces,
            MaxSources = 5
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddOrganizationMemberAsync(ControlApiTestApplication app, Guid organizationId, string subject, OrganizationRole role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = await CreateAccountAsync(db, subject);
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Account = account,
            OrganizationId = organizationId,
            Role = role
        });
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<Guid> AddAccountAsync(ControlApiTestApplication app, string subject)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = await CreateAccountAsync(db, subject);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<Account> CreateAccountAsync(CatalogDbContext db, string subject)
    {
        var existing = await db.Accounts.SingleOrDefaultAsync(x => x.ExternalIdentities.Any(identity => identity.Subject == subject));
        if (existing is not null)
            return existing;

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
        db.Accounts.Add(account);
        return account;
    }
}
