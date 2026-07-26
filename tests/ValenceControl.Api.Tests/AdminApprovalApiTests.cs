using System.Net;
using ValenceControl.Api.Admin.Packages;
using ValenceControl.Api.Authentication;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests;

public sealed class AdminApprovalApiTests
{
    [Fact]
    public async Task Admin_can_approve_package_and_version()
    {
        await using var app = new ControlApiTestApplication();
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

        packageResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        versionResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var storedPackage = await db.Packages.Include(x => x.Versions).SingleAsync();
        storedPackage.Approved.Should().BeTrue();
        storedPackage.Versions[0].ApprovalStatus.Should().Be(PackageApprovalStatus.Approved);
    }

    [Fact]
    public async Task Version_rejection_requires_reason()
    {
        await using var app = new ControlApiTestApplication();
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Version_approval_requires_state_token()
    {
        await using var app = new ControlApiTestApplication();
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

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Version_approval_rejects_stale_state_tokens()
    {
        await using var app = new ControlApiTestApplication();
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

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
