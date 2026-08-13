using System.Net;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Public.Builder;
using ValenceControl.PackageCatalog.Core.Packages;
using ValenceControl.PackageCatalog.Testing;

namespace ValenceControl.Api.Tests;

public sealed class PublicBuilderBundleApiTests
{
    [Fact]
    public async Task Trusted_builder_client_can_generate_bundle()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var response = await BuilderClient(app).PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        var paths = body!.Files.Select(x => x.Path);
        Assert.Contains("config.json", paths);
        Assert.Contains("packages.lock.json", paths);
        Assert.Contains("docker-compose.yml", paths);
        Assert.Contains(".env.example", paths);
        Assert.Contains("README.md", paths);
        Assert.DoesNotContain(body.Findings, x => x.Level == "error");
    }

    [Fact]
    public async Task Direct_untrusted_bundle_calls_are_rejected()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Builder_credentials_do_not_authorize_admin_apis()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = BuilderClient(app);

        var bundle = await client.PostControlJsonAsync("/api/builder/bundle", MinimalRequest());
        var admin = await client.GetAsync("/api/admin/application");

        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
    }

    [Fact]
    public async Task Admin_credentials_do_not_authorize_builder_bundle_generation()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.PostControlJsonAsync("/api/builder/bundle", MinimalRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Blocked_domain_response_returns_empty_files()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await BuilderClient(app).PostControlJsonAsync("/api/builder/bundle", MinimalRequest(imageSlug: "missing"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadControlJsonAsync<BuilderBundleResponse>();
        Assert.Empty(body!.Files);
        Assert.Contains(body.Findings, x => x.Level == "error" && x.Code == "runtimeImage.unknown");
    }

    private static BuilderBundleRequest MinimalRequest(string imageSlug = "elsa-pro-combined") =>
        new(
            new BuilderBundleImageRequest(imageSlug, "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages"));

    private static HttpClient BuilderClient(ControlApiTestApplication app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "builder-dev-key");
        return client;
    }
}
