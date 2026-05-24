using Elsa.Platform.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests;

public sealed class WorkspaceProvisioningTests
{
    [Fact]
    public async Task First_platform_sign_in_creates_account_external_identity_workspace_and_owner_membership()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(subject: "first-user");

        var context = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        context!.Account.Id.Should().NotBeEmpty();
        context.Workspaces.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner && x.Kind == WorkspaceKind.Personal);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().ContainSingle();
        db.ExternalIdentities.Should().ContainSingle(x => x.Subject == "first-user");
        db.Workspaces.Should().ContainSingle();
        db.WorkspaceMemberships.Should().ContainSingle(x => x.Role == WorkspaceRole.Owner);
    }

    [Fact]
    public async Task Repeated_platform_sign_in_is_idempotent()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(subject: "same-user");

        var first = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var second = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        second!.Account.Id.Should().Be(first!.Account.Id);
        second.Workspaces.Single().Id.Should().Be(first.Workspaces.Single().Id);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().ContainSingle();
        db.ExternalIdentities.Should().ContainSingle();
        db.Workspaces.Should().ContainSingle();
        db.WorkspaceMemberships.Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_platform_first_sign_in_returns_same_workspace()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => app.CreatePlatformIdentityClient(subject: "concurrent-user")
                .GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces")));

        responses.Select(x => x!.Account.Id).Distinct().Should().ContainSingle();
        responses.Select(x => x!.Workspaces.Single().Id).Distinct().Should().ContainSingle();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().ContainSingle();
        db.ExternalIdentities.Should().ContainSingle();
        db.Workspaces.Should().ContainSingle();
        db.WorkspaceMemberships.Should().ContainSingle();
    }

    [Fact]
    public async Task Workspace_context_lists_all_active_non_deleted_memberships()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(subject: "multi-workspace");
        var initial = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        await AddWorkspaceAsync(app, initial!.Account.Id, "Shared Workspace", softDeleted: false);
        await AddWorkspaceAsync(app, initial.Account.Id, "Deleted Workspace", softDeleted: true);

        var context = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        context!.Workspaces.Select(x => x.Name).Should().Contain(["Ada Lovelace", "Shared Workspace"]);
        context.Workspaces.Select(x => x.Name).Should().NotContain("Deleted Workspace");
    }

    private static async Task AddWorkspaceAsync(PlatformApiTestApplication app, Guid accountId, string name, bool softDeleted)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.Id == accountId);
        var workspace = new global::Elsa.Platform.PackageCatalog.Core.Accounts.Workspace
        {
            Name = name,
            Kind = WorkspaceKind.Organization,
            SoftDeletedAt = softDeleted ? DateTimeOffset.UtcNow : null
        };
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
