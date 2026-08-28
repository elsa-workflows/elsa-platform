using System.Net;
using System.Reflection;
using ElsaControl.Api.Admin.Application;
using ElsaControl.Api.Authentication;
using Microsoft.Extensions.Configuration;

namespace ElsaControl.Api.Tests;

public sealed class AdminApplicationApiTests
{
    [Fact]
    public async Task Admin_application_info_requires_api_key()
    {
        await using var app = new ControlApiTestApplication();

        var response = await app.CreateClient().GetAsync("/api/admin/application");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_application_info_returns_api_build_number()
    {
        await using var app = new ControlApiTestApplication();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var info = await client.GetControlJsonAsync<AdminApplicationResponse>("/api/admin/application");

        Assert.NotNull(info);
        Assert.Equal("ElsaControl.Api", info!.Name);
        Assert.Equal(ExpectedBuildNumber(), info.BuildNumber);
    }

    [Fact]
    public async Task Admin_application_info_prefers_configured_build_number()
    {
        await using var app = new ControlApiTestApplication().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Application:BuildNumber"] = "12345"
                }));
        });
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var info = await client.GetControlJsonAsync<AdminApplicationResponse>("/api/admin/application");

        Assert.NotNull(info);
        Assert.Equal("12345", info!.BuildNumber);
    }

    private static string ExpectedBuildNumber()
    {
        var assembly = typeof(Program).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
