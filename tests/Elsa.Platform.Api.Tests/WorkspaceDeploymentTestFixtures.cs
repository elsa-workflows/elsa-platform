using Elsa.Platform.Api.Authentication;

namespace Elsa.Platform.Api.Tests;

internal static class WorkspaceDeploymentTestFixtures
{
    public const string DefaultIssuer = "https://elsaworkflows.io";
    public const string DefaultSubject = "deployment-owner";
    public const string OtherSubject = "deployment-other";

    public static HttpClient CreateTrustedWorkspaceClient(this PlatformApiTestApplication app, string subject = DefaultSubject)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.IssuerHeader, DefaultIssuer);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.EmailHeader, $"{subject}@example.test");
        client.DefaultRequestHeaders.Add(TrustedHeaderWorkspaceIdentityReader.NameHeader, subject);
        return client;
    }

    public static async Task<Guid> GetDefaultWorkspaceIdAsync(this HttpClient client, CancellationToken cancellationToken = default)
    {
        var response = await client.GetPlatformJsonAsync<MeWorkspacesResponse>("/api/me/workspaces", cancellationToken);
        return response!.Workspaces.Single().Id;
    }
}
