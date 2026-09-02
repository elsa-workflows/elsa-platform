using System.Net;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class OperatorAuthorizationTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public OperatorAuthorizationTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Customer_identity_cannot_call_operator_endpoints()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var customer = app.CreateControlIdentityClient();

        var response = await customer.GetAsync("/api/admin/sources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_api_key_can_call_operator_endpoints()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await admin.GetAsync("/api/admin/sources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_api_key_does_not_create_customer_workspace_context()
    {
        var app = _app;
        await app.SeedAsync(_ => Task.CompletedTask);
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await admin.GetAsync("/api/me/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.Empty(db.Accounts);
        Assert.Empty(db.Workspaces);
        Assert.Empty(db.ExternalIdentities);
    }
}
