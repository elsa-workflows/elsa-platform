using System.Net;
using Elsa.Platform.Api.Admin.Sources;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.PackageCatalog.Core.Packages;
using FluentAssertions;

namespace Elsa.Platform.Api.Tests;

public sealed class AdminSourceMutationApiTests
{
    [Fact]
    public async Task Can_update_and_delete_source()
    {
        await using var app = new PlatformApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");

        var created = await (await client.PostPlatformJsonAsync("/api/admin/sources", Request("NuGet")))
            .Content.ReadPlatformJsonAsync<AdminSourceResponse>();

        var updatedResponse = await client.PutPlatformJsonAsync($"/api/admin/sources/{created!.Id}", Request("Internal NuGet"));
        var updated = await updatedResponse.Content.ReadPlatformJsonAsync<AdminSourceResponse>();

        updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.Name.Should().Be("Internal NuGet");

        var delete = await client.DeleteAsync($"/api/admin/sources/{created.Id}");

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetPlatformJsonAsync<List<AdminSourceResponse>>("/api/admin/sources")).Should().BeEmpty();
        (await client.GetAsync($"/api/admin/sources/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static AdminSourceRequest Request(string name) =>
        new(name, "https://example.test/v3/index.json", true, ["Elsa.*"], [], PackageSourceApprovalPolicy.Manual);
}
