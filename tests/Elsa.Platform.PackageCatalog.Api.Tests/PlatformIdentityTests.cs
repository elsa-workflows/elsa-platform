using System.Net;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Elsa.Platform.PackageCatalog.Api.Workspace;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

public sealed class PlatformIdentityTests
{
    [Fact]
    public async Task Me_workspaces_accepts_valid_platform_jwt_identity()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient();

        var response = await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        response.Should().NotBeNull();
        response!.Account.Email.Should().Be("ada@example.test");
        response.Account.DisplayName.Should().Be("Ada Lovelace");
        response.Workspaces.Should().ContainSingle();
    }

    [Fact]
    public async Task Me_workspaces_rejects_wrong_issuer_platform_jwt_identity()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(issuer: "https://evil.example.test");

        var response = await client.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_workspaces_rejects_platform_jwt_when_signing_key_has_no_configured_issuer()
    {
        await using var app = new CatalogApiTestApplication(new Dictionary<string, string?>
        {
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Issuer"] = null
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient();

        var response = await client.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_workspaces_rejects_wrong_audience_platform_jwt_identity()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(audience: "wrong-audience");

        var response = await client.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_workspaces_rejects_expired_platform_jwt_identity()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(expires: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await client.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_workspaces_rejects_platform_jwt_without_subject()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(subject: "");

        var response = await client.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_workspaces_uses_configured_platform_claim_mapping()
    {
        await using var app = new CatalogApiTestApplication(new Dictionary<string, string?>
        {
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Claims:Subject"] = "oid",
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Claims:DisplayName:0"] = "preferred_username",
            [$"{PlatformIdentityDefaults.ConfigurationSection}:Claims:Email:0"] = "upn"
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(
            subject: "",
            claims: new Dictionary<string, string>
            {
                ["oid"] = "entra-object-id",
                ["preferred_username"] = "Ada",
                ["upn"] = "ada@contoso.test"
            });

        var response = await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        response.Should().NotBeNull();
        response!.Account.DisplayName.Should().Be("Ada");
        response.Account.Email.Should().Be("ada@contoso.test");
    }

    [Fact]
    public async Task Me_workspaces_ignores_browser_supplied_identity_authority()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(subject: "real-subject");
        client.DefaultRequestHeaders.Add("X-Account-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Role", "Owner");

        var response = await client.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        response.Should().NotBeNull();
        response!.Workspaces.Should().ContainSingle(x => x.Role == Elsa.Platform.PackageCatalog.Core.Accounts.WorkspaceRole.Owner);
    }

    [Fact]
    public async Task Me_workspaces_updates_profile_metadata_for_same_platform_identity()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var firstClient = app.CreatePlatformIdentityClient(subject: "same-subject");
        var secondClient = app.CreatePlatformIdentityClient(
            subject: "same-subject",
            claims: new Dictionary<string, string>
            {
                ["name"] = "Ada Byron",
                ["email"] = "ada.byron@example.test"
            });

        var first = await firstClient.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var second = await secondClient.GetCatalogJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        second.Should().NotBeNull();
        second!.Account.Id.Should().Be(first!.Account.Id);
        second.Account.DisplayName.Should().Be("Ada Byron");
        second.Account.Email.Should().Be("ada.byron@example.test");
    }

    [Fact]
    public async Task Workspace_endpoint_does_not_provision_unknown_identity_on_denied_access()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreatePlatformIdentityClient(subject: "unknown-user");

        var response = await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        dbContext.Accounts.Should().BeEmpty();
        dbContext.Workspaces.Should().BeEmpty();
        dbContext.ExternalIdentities.Should().BeEmpty();
    }
}
