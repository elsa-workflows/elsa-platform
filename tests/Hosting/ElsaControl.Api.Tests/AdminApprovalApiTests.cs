using System.Net;
using ElsaControl.Api.Admin.Packages;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.PackageCatalog.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class AdminApprovalApiTests : IClassFixture<DefaultControlApiTestApplicationFixture>
{
    private readonly ControlApiTestApplication _app;

    public AdminApprovalApiTests(DefaultControlApiTestApplicationFixture fixture) => _app = fixture.Application;

    [Fact]
    public async Task Admin_can_approve_package_and_version()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source, approved: false);
            PublicCatalogSeedData.AddVersion(package, approvalStatus: PackageApprovalStatus.Pending);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        var packageDetails = await client.GetControlJsonAsync<AdminPackageResponse>("/api/admin/packages/Elsa.Email");
        var token = packageDetails!.Versions.Single().VersionStateToken;

        var packageResponse = await client.PostAsync("/api/admin/packages/Elsa.Email/approve", null);
        var versionResponse = await client.PostControlJsonAsync("/api/admin/packages/Elsa.Email/versions/1.0.0/approve", new ApprovalRequest(null, token));

        Assert.Equal(HttpStatusCode.NoContent, packageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, versionResponse.StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var storedPackage = await db.Packages.Include(x => x.Versions).SingleAsync();
        Assert.True(storedPackage.Approved);
        Assert.Equal(PackageApprovalStatus.Approved, storedPackage.Versions[0].ApprovalStatus);
    }

    [Fact]
    public async Task Version_rejection_requires_reason()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package, approvalStatus: PackageApprovalStatus.Pending);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostControlJsonAsync("/api/admin/packages/Elsa.Email/versions/1.0.0/reject", new ApprovalRequest(" ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Version_approval_requires_state_token()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package, approvalStatus: PackageApprovalStatus.Pending);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostAsync("/api/admin/packages/Elsa.Email/versions/1.0.0/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Version_approval_rejects_stale_state_tokens()
    {
        var app = _app;
        await app.SeedAsync(db =>
        {
            var source = PublicCatalogSeedData.CreatePackageSource();
            var package = PublicCatalogSeedData.CreatePackage(source);
            PublicCatalogSeedData.AddVersion(package, approvalStatus: PackageApprovalStatus.Pending);
            db.PackageSources.Add(source);
            return Task.CompletedTask;
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostControlJsonAsync("/api/admin/packages/Elsa.Email/versions/1.0.0/approve", new ApprovalRequest("Reviewed", "stale"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
