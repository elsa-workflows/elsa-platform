using System.Net;
using Elsa.Platform.PackageCatalog.Api.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Elsa.Platform.PackageCatalog.Api.Tests;

public sealed class AdminApiAuthenticationTests
{
    [Fact]
    public async Task Health_endpoint_is_public()
    {
        await using var app = new CatalogApiTestApplication();
        var response = await app.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_api_rejects_known_development_key_when_api_key_is_not_configured()
    {
        await using var app = new CatalogApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [ApiKeyAuthenticationDefaults.ConfigurationKey] = ""
                }));
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var response = await client.GetAsync("/api/admin/sources");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
