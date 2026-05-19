using System.Net;
using Elsa.Catalog.Api.Authentication;
using Elsa.Catalog.Api.Public.Builder;
using FluentAssertions;

namespace Elsa.Catalog.Api.Tests;

public sealed class DeploymentTemplateBundleApiTests
{
    [Theory]
    [InlineData("docker-compose", "docker-compose.yml")]
    [InlineData("azure-container-apps", "azure-container-app.bicep")]
    [InlineData("kubernetes-helm", "helm/Chart.yaml")]
    public async Task Builder_bundle_supports_deployment_targets(string target, string expectedFile)
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await BuilderClient(app).PostCatalogJsonAsync("/api/builder/bundle", Request(target));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();
        body!.Files.Should().Contain(x => x.Path == expectedFile);
    }

    [Fact]
    public async Task Unsupported_deployment_target_returns_findings_without_files()
    {
        await using var app = new CatalogApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await BuilderClient(app).PostCatalogJsonAsync("/api/builder/bundle", Request("terraform"));

        var body = await response.Content.ReadCatalogJsonAsync<BuilderBundleResponse>();
        body!.Files.Should().BeEmpty();
        body.Findings.Should().ContainSingle(x => x.Code == "deploymentTarget.unsupported");
    }

    private static BuilderBundleRequest Request(string target) =>
        new(
            new BuilderBundleImageRequest("elsa-pro-combined", "latest", 8080, new Dictionary<string, string>()),
            [],
            [],
            [],
            new BuilderBundleLocalPackagesRequest(false, "packages"),
            target);

    private static HttpClient BuilderClient(CatalogApiTestApplication app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "builder-dev-key");
        return client;
    }
}
