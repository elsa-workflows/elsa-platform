using System.Net;
using System.Text.Json;
using ElsaControl.Api.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ElsaControl.Api.Tests;

public sealed class AdminApiAuthenticationTests
{
    [Fact]
    public async Task Health_endpoint_is_public()
    {
        await using var app = new ControlApiTestApplication();
        var response = await app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_the_safe_build_and_image_identity()
    {
        await using var app = new ControlApiTestApplication(new Dictionary<string, string?>
        {
            ["Application:BuildNumber"] = "1786839398",
            ["ELSA_CONTROL_IMAGE_ID"] = "abcdef0123456789"
        });

        var response = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("1786839398", payload.RootElement.GetProperty("buildNumber").GetString());
        Assert.Equal("abcdef0123456789", payload.RootElement.GetProperty("imageId").GetString());
    }

    [Fact]
    public async Task Health_endpoint_redacts_unsafe_build_and_image_identity()
    {
        await using var app = new ControlApiTestApplication(new Dictionary<string, string?>
        {
            ["Application:BuildNumber"] = "build number with spaces",
            ["ELSA_CONTROL_IMAGE_ID"] = "https://user:password@example.test/image"
        });

        var response = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unknown", payload.RootElement.GetProperty("buildNumber").GetString());
        Assert.Equal("unknown", payload.RootElement.GetProperty("imageId").GetString());
    }

    [Fact]
    public async Task Admin_api_rejects_known_development_key_when_api_key_is_not_configured()
    {
        await using var app = new ControlApiTestApplication().WithWebHostBuilder(builder =>
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

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
