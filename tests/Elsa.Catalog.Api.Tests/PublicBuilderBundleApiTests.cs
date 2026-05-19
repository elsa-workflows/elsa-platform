using System.Net;
using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Api.Public.Builder;
using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Testing;
using FluentAssertions;

namespace Elsa.Catalog.Api.Tests;

public sealed class PublicBuilderBundleApiTests
{
    [Fact]
    public async Task Trusted_builder_client_can_generate_bundle()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var response = await BuilderClient(app).PostCatalogJsonAsync("/api/builder/bundle", MinimalRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();
        body!.Files.Select(x => x.Path).Should().Contain(["config.json", "packages.lock.json", "docker-compose.yml", ".env.example", "README.md"]);
        body.Findings.Should().NotContain(x => x.Level == "error");
    }

    [Fact]
    public async Task Direct_untrusted_bundle_calls_are_rejected()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostCatalogJsonAsync("/api/builder/bundle", MinimalRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Builder_credentials_do_not_authorize_admin_apis()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = BuilderClient(app);

        var bundle = await client.PostCatalogJsonAsync("/api/builder/bundle", MinimalRequest());
        var admin = await client.GetAsync("/api/admin/application");

        bundle.StatusCode.Should().Be(HttpStatusCode.OK);
        admin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_credentials_do_not_authorize_builder_bundle_generation()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostCatalogJsonAsync("/api/builder/bundle", MinimalRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Blocked_domain_response_returns_empty_files()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await BuilderClient(app).PostCatalogJsonAsync("/api/builder/bundle", MinimalRequest(imageSlug: "missing"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();
        body!.Files.Should().BeEmpty();
        body.Findings.Should().Contain(x => x.Level == "error" && x.Code == "runtimeImage.unknown");
    }

    private static BuilderBundleRequest MinimalRequest(string imageSlug = "elsa-pro-combined") =>
        new(
            new BuilderBundleImageRequest(imageSlug, "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages"));

    private static HttpClient BuilderClient(CatalogApiTestApplication app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "builder-dev-key");
        return client;
    }
}
