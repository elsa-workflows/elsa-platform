using System.Net;
using System.Net.Http.Headers;
using ElsaControl.Api.Authentication;
using ElsaControl.Api.Workspace;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class ControlIdentityTests
{
    [Fact]
    public async Task Me_workspaces_accepts_valid_control_jwt_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient();

        var response = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        Assert.NotNull(response);
        Assert.Equal("ada@example.test", response!.Account.Email);
        Assert.Equal("Ada Lovelace", response.Account.DisplayName);
        Assert.Single(response.Workspaces);
    }

    [Fact]
    public async Task Me_workspaces_rejects_wrong_issuer_control_jwt_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(issuer: "https://evil.example.test");

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_control_jwt_when_signing_key_has_no_configured_issuer()
    {
        await using var app = new ControlApiTestApplication(new Dictionary<string, string?>
        {
            [$"{ControlIdentityDefaults.ConfigurationSection}:Issuer"] = null
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient();

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_control_jwt_with_empty_issuer_when_signing_key_has_no_configured_issuer()
    {
        await using var app = new ControlApiTestApplication(new Dictionary<string, string?>
        {
            [$"{ControlIdentityDefaults.ConfigurationSection}:Issuer"] = null,
            [$"{ControlIdentityDefaults.ConfigurationSection}:Authority"] = null
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(issuer: "");

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_wrong_audience_control_jwt_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(audience: "wrong-audience");

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_control_jwt_when_audience_is_not_configured()
    {
        await using var app = new ControlApiTestApplication(new Dictionary<string, string?>
        {
            [$"{ControlIdentityDefaults.ConfigurationSection}:Audience"] = null
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient();

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_invalid_bearer_token_without_falling_back_to_trusted_headers()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-token");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, "https://trusted.example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, "trusted-subject");

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Empty(dbContext.Accounts);
    }

    [Fact]
    public async Task Me_workspaces_rejects_expired_control_jwt_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(expires: DateTimeOffset.UtcNow.AddMinutes(-5));

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_rejects_control_jwt_without_subject()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "");

        var response = await client.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_workspaces_uses_configured_control_claim_mapping()
    {
        await using var app = new ControlApiTestApplication(new Dictionary<string, string?>
        {
            [$"{ControlIdentityDefaults.ConfigurationSection}:Claims:Subject"] = "oid",
            [$"{ControlIdentityDefaults.ConfigurationSection}:Claims:DisplayName:0"] = "preferred_username",
            [$"{ControlIdentityDefaults.ConfigurationSection}:Claims:Email:0"] = "upn"
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(
            subject: "",
            claims: new Dictionary<string, string>
            {
                ["oid"] = "entra-object-id",
                ["preferred_username"] = "Ada",
                ["upn"] = "ada@contoso.test"
            });

        var response = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        Assert.NotNull(response);
        Assert.Equal("Ada", response!.Account.DisplayName);
        Assert.Equal("ada@contoso.test", response.Account.Email);
    }

    [Fact]
    public async Task Me_workspaces_ignores_browser_supplied_identity_authority()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "real-subject");
        client.DefaultRequestHeaders.Add("X-Account-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Workspace-Role", "Owner");

        var response = await client.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        Assert.NotNull(response);
        Assert.Single(response!.Workspaces, x => x.Role == ElsaControl.PackageCatalog.Core.Accounts.WorkspaceRole.Owner);
    }

    [Fact]
    public async Task Me_workspaces_updates_profile_metadata_for_same_control_identity()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var firstClient = app.CreateControlIdentityClient(subject: "same-subject");
        var secondClient = app.CreateControlIdentityClient(
            subject: "same-subject",
            claims: new Dictionary<string, string>
            {
                ["name"] = "Ada Byron",
                ["email"] = "ada.byron@example.test"
            });

        var first = await firstClient.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var second = await secondClient.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");

        Assert.NotNull(second);
        Assert.Equal(first!.Account.Id, second!.Account.Id);
        Assert.Equal("Ada Byron", second.Account.DisplayName);
        Assert.Equal("ada.byron@example.test", second.Account.Email);
    }

    [Fact]
    public async Task Workspace_endpoint_does_not_provision_unknown_identity_on_denied_access()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateControlIdentityClient(subject: "unknown-user");

        var response = await client.GetAsync($"/api/workspaces/{Guid.NewGuid()}/sources");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Empty(dbContext.Accounts);
        Assert.Empty(dbContext.Workspaces);
        Assert.Empty(dbContext.ExternalIdentities);
    }

    [Fact]
    public async Task Workspace_endpoint_does_not_update_profile_metadata_on_denied_access()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var ownerClient = app.CreateControlIdentityClient(subject: "owner");
        var workspaceId = (await ownerClient.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var otherClient = app.CreateControlIdentityClient(
            subject: "other-user",
            claims: new Dictionary<string, string>
            {
                ["name"] = "Other User",
                ["email"] = "other@example.test"
            });
        await otherClient.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var deniedClient = app.CreateControlIdentityClient(
            subject: "other-user",
            claims: new Dictionary<string, string>
            {
                ["name"] = "Changed User",
                ["email"] = "changed@example.test"
            });

        var response = await deniedClient.GetAsync($"/api/workspaces/{workspaceId}/sources");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var account = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(x => x.ExternalIdentities.Any(identity => identity.Subject == "other-user"));
        Assert.Equal("Other User", account.DisplayName);
        Assert.Equal("other@example.test", account.Email);
    }
}
