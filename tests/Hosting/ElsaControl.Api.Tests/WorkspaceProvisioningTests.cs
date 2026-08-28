using ElsaControl.Api.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceProvisioningTests
{
    [Fact]
    public async Task First_control_sign_in_creates_account_external_identity_workspace_and_owner_membership()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "first-user");

        var context = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        Assert.NotEqual(Guid.Empty, context!.Account.Id);
        Assert.Single(context.Workspaces, x => x.Role == WorkspaceRole.Owner && x.Kind == WorkspaceKind.Personal);
        Assert.Single(context.Organizations, x => x.Role == OrganizationRole.Owner);
        Assert.Equal(context.Organizations.Single().Id, context.Workspaces.Single().OrganizationId);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Single(db.Accounts);
        Assert.Single(db.ExternalIdentities, x => x.Subject == "first-user");
        Assert.Single(db.Organizations);
        Assert.Single(db.OrganizationMemberships, x => x.Role == OrganizationRole.Owner);
        Assert.Single(db.Workspaces);
        Assert.Single(db.WorkspaceMemberships, x => x.Role == WorkspaceRole.Owner);
    }

    [Fact]
    public async Task Repeated_control_sign_in_is_idempotent()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "same-user");

        var first = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var second = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        Assert.Equal(first!.Account.Id, second!.Account.Id);
        Assert.Equal(first.Workspaces.Single().Id, second.Workspaces.Single().Id);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Single(db.Accounts);
        Assert.Single(db.ExternalIdentities);
        Assert.Single(db.Organizations);
        Assert.Single(db.OrganizationMemberships);
        Assert.Single(db.Workspaces);
        Assert.Single(db.WorkspaceMemberships);
    }

    [Fact]
    public async Task Organization_context_endpoint_returns_organizations_and_workspaces()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "organization-context");

        var context = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/organizations");

        Assert.Single(context!.Organizations, x => x.Role == OrganizationRole.Owner);
        Assert.Single(context.Workspaces, x => x.OrganizationId == context.Organizations.Single().Id);
    }

    [Fact]
    public async Task Concurrent_control_first_sign_in_returns_same_workspace()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => app.CreateControlIdentityClient(subject: "concurrent-user")
                .GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces")));

        Assert.Single(responses.Select(x => x!.Account.Id).Distinct());
        Assert.Single(responses.Select(x => x!.Workspaces.Single().Id).Distinct());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Single(db.Accounts);
        Assert.Single(db.ExternalIdentities);
        Assert.Single(db.Organizations);
        Assert.Single(db.OrganizationMemberships);
        Assert.Single(db.Workspaces);
        Assert.Single(db.WorkspaceMemberships);
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

        var workspaceNames = context!.Workspaces.Select(x => x.Name);
        Assert.Contains("Ada Lovelace", workspaceNames);
        Assert.Contains("Shared Workspace", workspaceNames);
        Assert.DoesNotContain("Deleted Workspace", context.Workspaces.Select(x => x.Name));
        var organizationNames = context.Organizations.Select(x => x.Name);
        Assert.Contains("Ada Lovelace", organizationNames);
        Assert.Contains("Shared Workspace", organizationNames);
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

        Assert.DoesNotContain("Hidden Workspace", context!.Workspaces.Select(x => x.Name));
    }

    private static async Task AddWorkspaceAsync(ControlApiTestApplication app, Guid accountId, string name, bool softDeleted, bool addOrganizationMembership = true)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId);
        var organization = new Organization { Name = name };
        var workspace = new global::ElsaControl.PackageCatalog.Core.Accounts.Workspace
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
