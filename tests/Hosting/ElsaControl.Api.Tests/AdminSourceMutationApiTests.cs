using System.Net;
using ElsaControl.Api.Admin.Sources;
using ElsaControl.Api.Authentication;
using ElsaControl.PackageCatalog.Core.Packages;

namespace ElsaControl.Api.Tests;

public sealed class AdminSourceMutationApiTests
{
    [Fact]
    public async Task Can_update_and_delete_source()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var created = await (await client.PostControlJsonAsync("/api/admin/sources", Request("NuGet")))
            .Content.ReadControlJsonAsync<AdminSourceResponse>();

        var updatedResponse = await client.PutControlJsonAsync($"/api/admin/sources/{created!.Id}", Request("Internal NuGet"));
        var updated = await updatedResponse.Content.ReadControlJsonAsync<AdminSourceResponse>();

        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        Assert.Equal("Internal NuGet", updated!.Name);

        var delete = await client.DeleteAsync($"/api/admin/sources/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty((await client.GetControlJsonAsync<List<AdminSourceResponse>>("/api/admin/sources"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/sources/{created.Id}")).StatusCode);
    }

    private static AdminSourceRequest Request(string name) =>
        new(name, "https://example.test/v3/index.json", true, ["Elsa.*"], [], PackageSourceApprovalPolicy.Manual);
}
