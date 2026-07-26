using ValenceControl.Api.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class WorkspaceProvisioningTests
{
    [Fact]
    public async Task First_control_sign_in_creates_account_external_identity_workspace_and_owner_membership()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "first-user");

        var context = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        context!.Account.Id.Should().NotBeEmpty();
        context.Workspaces.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner && x.Kind == WorkspaceKind.Personal);
        context.Organizations.Should().ContainSingle(x => x.Role == OrganizationRole.Owner);
        context.Workspaces.Single().OrganizationId.Should().Be(context.Organizations.Single().Id);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().ContainSingle();
        db.ExternalIdentities.Should().ContainSingle(x => x.Subject == "first-user");
        db.Organizations.Should().ContainSingle();
        db.OrganizationMemberships.Should().ContainSingle(x => x.Role == OrganizationRole.Owner);
        db.Workspaces.Should().ContainSingle();
        db.WorkspaceMemberships.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner);
    }

    [Fact]
    public async Task Repeated_control_sign_in_is_idempotent()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "same-user");

        var first = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var second = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        second!.Account.Id.Should().Be(first!.Account.Id);
        second.Workspaces.Single().Id.Should().Be(first.Workspaces.Single().Id);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().ContainSingle();
        db.ExternalIdentities.Should().ContainSingle();
        db.Organizations.Should().ContainSingle();
        db.OrganizationMemberships.Should().ContainSingle();
        db.Workspaces.Should().ContainSingle();
        db.WorkspaceMemberships.Should().ContainSingle();
    }

    [Fact]
    public async Task Organization_context_endpoint_returns_organizations_and_workspaces()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "organization-context");

        var context = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/organizations");

        context!.Organizations.Should().ContainSingle(x => x.Role == OrganizationRole.Owner);
        context.Workspaces.Should().ContainSingle(x => x.OrganizationId == context.Organizations.Single().Id);
    }

    [Fact]
    public async Task Concurrent_control_first_sign_in_returns_same_workspace()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => app.CreateControlIdentityClient(subject: "concurrent-user")
                .GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces")));

        responses.Select(x => x!.Account.Id).Distinct().Should().ContainSingle();
        responses.Select(x => x!.Workspaces.Single().Id).Distinct().Should().ContainSingle();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().ContainSingle();
        db.ExternalIdentities.Should().ContainSingle();
        db.Organizations.Should().ContainSingle();
        db.OrganizationMemberships.Should().ContainSingle();
        db.Workspaces.Should().ContainSingle();
        db.WorkspaceMemberships.Should().ContainSingle();
    }

    [Fact]
    public async Task Workspace_context_lists_all_active_non_deleted_memberships()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "multi-workspace");
        var initial = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddWorkspaceAsync(app, initial!.Account.Id, "Shared Workspace", softDeleted: false);
        await AddWorkspaceAsync(app, initial.Account.Id, "Deleted Workspace", softDeleted: true);

        var context = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        context!.Workspaces.Select(x => x.Name).Should().Contain(["Ada Lovelace", "Shared Workspace"]);
        context.Workspaces.Select(x => x.Name).Should().NotContain("Deleted Workspace");
        context.Organizations.Select(x => x.Name).Should().Contain(["Ada Lovelace", "Shared Workspace"]);
    }

    [Fact]
    public async Task Workspace_membership_without_organization_membership_is_not_returned()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "workspace-only");
        var initial = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddWorkspaceAsync(app, initial!.Account.Id, "Hidden Workspace", softDeleted: false, addOrganizationMembership: false);

        var context = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        context!.Workspaces.Select(x => x.Name).Should().NotContain("Hidden Workspace");
    }

    private static async Task AddWorkspaceAsync(ControlApiTestApplication app, Guid accountId, string name, bool softDeleted, bool addOrganizationMembership = true)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId);
        var organization = new Organization { Name = name };
        var workspace = new global::ValenceControl.PackageCatalog.Core.Accounts.Workspace
        {
            Name = name,
            Kind = WorkspaceKind.Shared,
            Organization = organization,
            SoftDeletedAt = softDeleted ? DateTimeOffset.UtcNow : null
        };
        if (addOrganizationMembership)
        {
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                Account = account,
                Organization = organization,
                Role = OrganizationRole.Member
            });
        }

        db.Workspaces.Add(workspace);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Account = account,
            Workspace = workspace,
            Role = WorkspaceRole.Reader
        });
        await db.SaveChangesAsync();
    }
}
