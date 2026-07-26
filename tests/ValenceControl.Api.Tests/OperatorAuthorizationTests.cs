using System.Net;
using ValenceControl.Api.Authentication;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class OperatorAuthorizationTests
{
    [Fact]
    public async Task Customer_identity_cannot_call_operator_endpoints()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var customer = app.CreateControlIdentityClient();

        var response = await customer.GetAsync("/api/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_api_key_can_call_operator_endpoints()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await admin.GetAsync("/api/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_api_key_does_not_create_customer_workspace_context()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var admin = app.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await admin.GetAsync("/api/me/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        db.Accounts.Should().BeEmpty();
        db.Workspaces.Should().BeEmpty();
        db.ExternalIdentities.Should().BeEmpty();
    }
}
