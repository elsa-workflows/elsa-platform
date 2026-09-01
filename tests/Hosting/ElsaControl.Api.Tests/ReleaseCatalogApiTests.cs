using System.Net;
using System.Security.Cryptography;
using System.Text;
using ElsaControl.Api.Authentication;
using ElsaControl.Api.ReleaseCatalog;
using ElsaControl.Api.Workspace;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElsaControl.Api.Tests;

public sealed class ReleaseCatalogApiTests
{
    private const string ProducerSigner = "https://github.com/valence-works/elsa-production-image/.github/workflows/build-and-push.yml@refs/heads/main";

    [Fact]
    public async Task Admin_ingestion_is_fail_closed_without_a_signature_verifier()
    {
        await using var app = CreateApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = CreateAdminClient(app);

        var response = await client.PostControlJsonAsync(
            "/api/admin/release-catalog/manifests",
            Request());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("releaseCatalog.admission.rejected", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_ingestion_returns_safe_projection_and_replays_idempotently()
    {
        await using var app = CreateApplication(services =>
        {
            services.RemoveAll<IReleaseManifestSignatureVerifier>();
            services.AddSingleton<IReleaseManifestSignatureVerifier, FixtureSignatureVerifier>();
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = CreateAdminClient(app);
        var request = Request();

        var first = await client.PostControlJsonAsync("/api/admin/release-catalog/manifests", request);
        var firstBody = await first.Content.ReadAsStringAsync();
        var firstResult = await first.Content.ReadControlJsonAsync<AdminReleaseCatalogAdmissionResponse>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(GovernedReleaseCatalogWriteStatus.Stored, firstResult!.Status);
        Assert.NotEmpty(firstResult.Entries);
        Assert.DoesNotContain(request.Payload!, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ProducerSigner, firstBody, StringComparison.Ordinal);

        var replay = await client.PostControlJsonAsync("/api/admin/release-catalog/manifests", request);
        var replayResult = await replay.Content.ReadControlJsonAsync<AdminReleaseCatalogAdmissionResponse>();

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(GovernedReleaseCatalogWriteStatus.Unchanged, replayResult!.Status);
        Assert.Equal(firstResult.Entries.Count, replayResult.Entries.Count);
    }

    [Fact]
    public async Task Workspace_catalog_requires_access_and_returns_admitted_entries()
    {
        await using var app = CreateApplication(services =>
        {
            services.RemoveAll<IReleaseManifestSignatureVerifier>();
            services.AddSingleton<IReleaseManifestSignatureVerifier, FixtureSignatureVerifier>();
        });
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateControlIdentityClient(subject: "catalog-owner");
        var workspaceId = (await owner.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces"))!.Workspaces.Single().Id;
        var outsider = app.CreateControlIdentityClient(subject: "catalog-outsider");
        _ = await outsider.GetControlJsonAsync<MeWorkspacesResponse>("/api/me/workspaces");
        var anonymous = app.CreateClient();

        var denied = await anonymous.GetAsync($"/api/workspaces/{workspaceId}/release-catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var forbidden = await outsider.GetAsync($"/api/workspaces/{workspaceId}/release-catalog");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var admin = CreateAdminClient(app);
        var admitted = await admin.PostControlJsonAsync("/api/admin/release-catalog/manifests", Request());
        Assert.Equal(HttpStatusCode.Created, admitted.StatusCode);

        var catalog = await owner.GetAsync($"/api/workspaces/{workspaceId}/release-catalog");
        var entries = await catalog.Content.ReadControlJsonAsync<ReleaseCatalogEntryResponse[]>();
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.NotEmpty(entries!);

        var filtered = await owner.GetControlJsonAsync<ReleaseCatalogEntryResponse[]>(
            $"/api/workspaces/{workspaceId}/release-catalog?releaseLine=3.8&lifecycle=preview&topologyId=server");
        Assert.Equal("server", Assert.Single(filtered!).Topology.Id);
    }

    [Fact]
    public async Task Admin_ingestion_rejects_missing_api_key()
    {
        await using var app = CreateApplication();
        await app.SeedAsync(_ => Task.CompletedTask);

        var response = await app.CreateClient().PostControlJsonAsync(
            "/api/admin/release-catalog/manifests",
            Request());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ingestion_reports_a_request_specific_detail_for_a_missing_body()
    {
        await using var app = CreateApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = CreateAdminClient(app);

        var response = await client.PostAsync(
            "/api/admin/release-catalog/manifests",
            new StringContent("null", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A release-manifest request body is required.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be admitted", body, StringComparison.OrdinalIgnoreCase);
    }

    private static ControlApiTestApplication CreateApplication(Action<IServiceCollection>? configureServices = null) =>
        new(new Dictionary<string, string?>
        {
            [ReleaseCatalogAdmissionOptions.ConfigurationSection + ":ExpectedSignatureSubject"] = ProducerSigner,
            [ReleaseCatalogAdmissionOptions.ConfigurationSection + ":RegistryClass"] = "paid",
            [ReleaseCatalogAdmissionOptions.ConfigurationSection + ":CatalogLifecycle"] = "Preview"
        }, configureServices);

    private static HttpClient CreateAdminClient(ControlApiTestApplication app)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "local-dev-key");
        return client;
    }

    private static AdminReleaseManifestIngestionRequest Request()
    {
        var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "producer-release-manifest-2.0.0.json"));
        var digest = Digest('a');
        return new(
            $"oci://valence-runtime/release-manifests/release-manifest@{digest}",
            digest,
            payload);
    }

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private sealed class FixtureSignatureVerifier : IReleaseManifestSignatureVerifier
    {
        public ValueTask<ReleaseManifestSignatureVerification> VerifyAsync(
            ReleaseManifestArtifact artifact,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ReleaseManifestSignatureVerification(
                true,
                ProducerSigner,
                artifact.Digest,
                $"oci://valence-runtime/signatures/release@{Digest('c')}",
                Digest('c'),
                ReleaseManifestSchema.DefaultOidcIssuer,
                PayloadDigest(artifact.Payload)));

        private static string PayloadDigest(string payload) =>
            $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";
    }
}
