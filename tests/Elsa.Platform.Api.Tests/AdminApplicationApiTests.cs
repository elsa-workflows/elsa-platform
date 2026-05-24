using System.Net;
using System.Reflection;
using Elsa.Platform.Api.Admin.Application;
using Elsa.Platform.Api.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Elsa.Platform.Api.Tests;

public sealed class AdminApplicationApiTests
{
    [Fact]
    public async Task Admin_application_info_requires_api_key()
    {
        await using var app = new PlatformApiTestApplication();

        var response = await app.CreateClient().GetAsync("/api/admin/application");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_application_info_returns_api_build_number()
    {
        await using var app = new PlatformApiTestApplication();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var info = await client.GetPlatformJsonAsync<AdminApplicationResponse>("/api/admin/application");

        info.Should().NotBeNull();
        info!.Name.Should().Be("Elsa.Platform.Api");
        info.BuildNumber.Should().Be(ExpectedBuildNumber());
    }

    [Fact]
    public async Task Admin_application_info_prefers_configured_build_number()
    {
        await using var app = new PlatformApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Application:BuildNumber"] = "12345"
                }));
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var info = await client.GetPlatformJsonAsync<AdminApplicationResponse>("/api/admin/application");

        info.Should().NotBeNull();
        info!.BuildNumber.Should().Be("12345");
    }

    private static string ExpectedBuildNumber()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
