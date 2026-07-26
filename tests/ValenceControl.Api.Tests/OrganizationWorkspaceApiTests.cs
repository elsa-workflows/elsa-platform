using System.Net;
using System.Net.Http.Json;
using ValenceControl.Api.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class OrganizationWorkspaceApiTests
{
    [Fact]
    public async Task Owner_can_create_and_list_organization_workspaces()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var context = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddEntitlementAsync(app, context!.Organizations.Single().Id, maxWorkspaces: 3);

        var create = await owner.PostControlJsonAsync(
            OrganizationWorkspacesPath(context.Organizations.Single().Id),
            new OrganizationWorkspaceCreateRequest("Automation"));

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var workspace = await create.Content.ReadControlJsonAsync<WorkspaceContextResponse>();
        workspace!.Kind.Should().Be(WorkspaceKind.Shared);
        workspace.Role.Should().Be(WorkspaceRole.Owner);

        var list = await owner.GetControlJsonAsync<OrganizationWorkspacesResponse>(OrganizationWorkspacesPath(context.Organizations.Single().Id));
        list!.Workspaces.Select(x => x.Name).Should().Contain(["Ada Lovelace", "Automation"]);
    }

    [Fact]
    public async Task Duplicate_active_workspace_name_is_rejected()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 3);
        await owner.PostControlJsonAsync(OrganizationWorkspacesPath(organizationId), new OrganizationWorkspaceCreateRequest("Automation"));

        var duplicate = await owner.PostControlJsonAsync(OrganizationWorkspacesPath(organizationId), new OrganizationWorkspaceCreateRequest("Automation"));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_workspace_creates_cannot_exceed_organization_workspace_limit()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);

        var responses = await Task.WhenAll(Enumerable.Range(1, 4)
            .Select(index => owner.PostControlJsonAsync(
                OrganizationWorkspacesPath(organizationId),
                new OrganizationWorkspaceCreateRequest($"Automation {index}"))));

        responses.Count(x => x.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Forbidden).Should().Be(3);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var activeWorkspaces = await db.Workspaces.CountAsync(x => x.OrganizationId == organizationId && x.SoftDeletedAt == null);
        activeWorkspaces.Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_workspace_creates_cannot_create_duplicate_active_names()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => owner.PostControlJsonAsync(
                OrganizationWorkspacesPath(organizationId),
                new OrganizationWorkspaceCreateRequest("Automation"))));

        responses.Count(x => x.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(x => x.StatusCode == HttpStatusCode.Conflict).Should().Be(3);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var matchingWorkspaces = await db.Workspaces.CountAsync(x =>
            x.OrganizationId == organizationId &&
            x.SoftDeletedAt == null &&
            x.Name == "Automation");
        matchingWorkspaces.Should().Be(1);
    }

    [Fact]
    public async Task Owner_can_rename_and_archive_workspace()
    {
        await using var app = new ControlApiTestApplication();
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

        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        archive.StatusCode.Should().Be(HttpStatusCode.OK);
        list!.Workspaces.Select(x => x.Name).Should().NotContain("Operations");
    }

    [Fact]
    public async Task Organization_member_lists_only_assigned_workspaces()
    {
        await using var app = new ControlApiTestApplication();
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

        list!.Workspaces.Select(x => x.Name).Should().ContainSingle().Which.Should().Be("Visible");
    }

    [Fact]
    public async Task Other_organization_owner_cannot_list_workspaces()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var other = app.CreateControlIdentityClient(subject: "other");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await other.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        var response = await other.GetAsync(OrganizationWorkspacesPath(organizationId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Organization_membership_alone_does_not_authorize_workspace_resources()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddOrganizationMemberAsync(app, ownerContext!.Organizations.Single().Id, "member", OrganizationRole.Member);

        var member = app.CreateControlIdentityClient(subject: "member");
        var response = await member.GetAsync($"/api/workspaces/{ownerContext.Workspaces.Single().Id}/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_membership_target_must_be_organization_member()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var organizationId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);
        var workspace = await CreateWorkspaceAsync(owner, organizationId, "Automation");
        var outsideAccountId = await AddAccountAsync(app, "outside");

        var response = await owner.PutControlJsonAsync(
            $"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}/members/{outsideAccountId}",
            new OrganizationWorkspaceMembershipRequest(WorkspaceRole.Reader));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Workspace_owner_can_be_removed_when_another_owner_exists()
    {
        await using var app = new ControlApiTestApplication();
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

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Removing_the_only_workspace_owner_is_rejected()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "owner");
        var ownerContext = await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var organizationId = ownerContext!.Organizations.Single().Id;
        await AddEntitlementAsync(app, organizationId, maxWorkspaces: 2);
        var workspace = await CreateWorkspaceAsync(owner, organizationId, "Automation");

        var response = await owner.DeleteAsync($"{OrganizationWorkspacesPath(organizationId)}/{workspace.Id}/members/{ownerContext.Account.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static string OrganizationWorkspacesPath(Guid organizationId) =>
        $"/api/organizations/{organizationId}/workspaces";

    private static async Task<WorkspaceContextResponse> CreateWorkspaceAsync(HttpClient owner, Guid organizationId, string name)
    {
        var response = await owner.PostControlJsonAsync(OrganizationWorkspacesPath(organizationId), new OrganizationWorkspaceCreateRequest(name));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
