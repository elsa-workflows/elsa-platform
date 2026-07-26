using System.Net;
using ValenceControl.Api.Admin.Sources;
using ValenceControl.Api.Authentication;
using ValenceControl.PackageCatalog.Core.Packages;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

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

        updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Name.Should().Be("Internal NuGet");

        var delete = await client.DeleteAsync($"/api/admin/sources/{created.Id}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetControlJsonAsync<List<AdminSourceResponse>>("/api/admin/sources")).Should().BeEmpty();
        (await client.GetAsync($"/api/admin/sources/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static AdminSourceRequest Request(string name) =>
        new(name, "https://example.test/v3/index.json", true, ["Elsa.*"], [], PackageSourceApprovalPolicy.Manual);
}
