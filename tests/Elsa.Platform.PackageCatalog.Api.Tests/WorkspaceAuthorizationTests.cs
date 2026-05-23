using System.Net;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Core.Packages;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using Elsa.Platform.RuntimeBuilder.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

public sealed class WorkspaceAuthorizationTests
{
    [Fact]
    public async Task Reader_can_list_workspace_sources_but_cannot_create_source()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreatePlatformIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        await AddMemberAsync(app, workspaceId, "reader", WorkspaceRole.Reader);

        var reader = app.CreatePlatformIdentityClient(subject: "reader");
        var list = await reader.GetAsync($"/api/workspaces/{workspaceId}/sources");
        var create = await reader.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/sources", SourceRequest());

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reader_can_list_runtime_configurations_but_cannot_mutate_them()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreatePlatformIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        await AddMemberAsync(app, workspaceId, "reader", WorkspaceRole.Reader);

        var reader = app.CreatePlatformIdentityClient(subject: "reader");
        var list = await reader.GetAsync($"/api/workspaces/{workspaceId}/runtime-configurations");
        var create = await reader.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", RuntimeConfigurationRequest());

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Workspace_role_changes_are_enforced_on_subsequent_requests()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreatePlatformIdentityClient(subject: "owner");
        var workspaceId = await GetWorkspaceIdAsync(owner);
        await AddMemberAsync(app, workspaceId, "member", WorkspaceRole.SourceAdmin);
        var member = app.CreatePlatformIdentityClient(subject: "member");

        var sourceAdminResponse = await member.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", RuntimeConfigurationRequest("Before downgrade"));
        await SetRoleAsync(app, workspaceId, "member", WorkspaceRole.Reader);
        var readerResponse = await member.PostCatalogJsonAsync($"/api/workspaces/{workspaceId}/runtime-configurations", RuntimeConfigurationRequest("After downgrade"));

        sourceAdminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> GetWorkspaceIdAsync(HttpClient client) =>
        (await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;

    private static WorkspaceSourceRequest SourceRequest() =>
        new("Private Feed", "https://private.example.test/v3/index.json", true, ["Elsa.*"], [], PackageSourceVersionDiscoveryPolicy.AllVersions);

    private static WorkspaceRuntimeConfigurationRequest RuntimeConfigurationRequest(string name = "Runtime") =>
        new(name, null, new RuntimeBuilderIntent(
            new RuntimeImageSelection("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new LocalPackagesOptions(false, "packages")));

    private static async Task AddMemberAsync(CatalogApiTestApplication app, Guid workspaceId, string subject, WorkspaceRole role)
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
            Issuer = CatalogApiTestApplication.TestPlatformIdentityIssuer,
            Subject = subject,
            DisplayName = subject,
            Email = $"{subject}@example.test"
        });
        account.Memberships.Add(new WorkspaceMembership
        {
            Account = account,
            WorkspaceId = workspaceId,
            Role = role
        });
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
    }

    private static async Task SetRoleAsync(CatalogApiTestApplication app, Guid workspaceId, string subject, WorkspaceRole role)
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
