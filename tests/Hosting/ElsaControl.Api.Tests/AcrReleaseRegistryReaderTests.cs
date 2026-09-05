using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using ElsaControl.Api.ReleaseCatalog;

namespace ElsaControl.Api.Tests;

public sealed class AcrReleaseRegistryReaderTests
{
    private const string Registry = "demo123.azurecr.io";
    private const string Repository = "release-manifests/release-manifest";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Client = "22222222-2222-2222-2222-222222222222";
    private const string RedirectHost = "becmanaged36.blob.core.windows.net";
    private const string BundleType = ReleaseRegistryProtocol.BundleMediaType;
    private static readonly string SubjectDigest = Digest(Encoding.UTF8.GetBytes("subject"));

    [Fact]
    public async Task Registry_and_blob_sends_suppress_ambient_telemetry()
    {
        var body = Encoding.UTF8.GetBytes("retained bundle");
        var handler = new RecordingHandler(request =>
        {
            Assert.True(OpenTelemetry.Sdk.SuppressInstrumentation);
            if (request.RequestUri!.AbsolutePath == "/oauth2/exchange")
                return Task.FromResult(JsonResponse("{\"refresh_token\":\"refresh-token\"}"));
            if (request.RequestUri.AbsolutePath == "/oauth2/token")
                return Task.FromResult(JsonResponse("{\"access_token\":\"acr-token\"}"));
            if (request.RequestUri.Host == Registry)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                {
                    Headers = { Location = new Uri($"https://{RedirectHost}/blob?sig=sensitive-query") }
                });
            return Task.FromResult(BytesResponse(body));
        });
        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        Assert.Equal(body, await session.ReadBlobAsync(Digest(body), body.Length));
        Assert.Equal(4, handler.Requests.Count);
        Assert.False(OpenTelemetry.Sdk.SuppressInstrumentation);
    }

    [Fact]
    public async Task Uses_the_fixed_audience_and_repository_pull_scope()
    {
        var manifest = Encoding.UTF8.GetBytes("{\"mediaType\":\"application/vnd.valence.release-manifest.v2+json\"}");
        var manifestDigest = Digest(manifest);
        var handler = new RecordingHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/exchange")
            {
                var form = await ReadFormAsync(request);
                Assert.Equal("access_token", form["grant_type"]);
                Assert.Equal(Registry, form["service"]);
                Assert.Equal(Tenant, form["tenant"]);
                Assert.Equal("aad-token", form["access_token"]);
                return JsonResponse("{\"refresh_token\":\"refresh-token\"}");
            }

            if (request.RequestUri.AbsolutePath == "/oauth2/token")
            {
                var form = await ReadFormAsync(request);
                Assert.Equal("refresh_token", form["grant_type"]);
                Assert.Equal(Registry, form["service"]);
                Assert.Equal($"repository:{Repository}:pull", form["scope"]);
                Assert.Equal("refresh-token", form["refresh_token"]);
                return JsonResponse("{\"access_token\":\"acr-token\"}");
            }

            Assert.Equal($"/v2/{Repository}/manifests/{manifestDigest}", request.RequestUri.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("acr-token", request.Headers.Authorization?.Parameter);
            Assert.Equal(ReleaseRegistryProtocol.ManifestMediaType, request.Headers.Accept.Single().MediaType);
            return BytesResponse(manifest, ReleaseRegistryProtocol.ManifestMediaType);
        });
        var credential = new RecordingCredential("aad-token");

        await using var session = await CreateReader(handler, credential).OpenAsync();
        var actual = await session.ReadManifestAsync(manifestDigest);

        Assert.Equal(manifest, actual);
        var context = Assert.Single(credential.Contexts);
        Assert.Equal("https://containerregistry.azure.net/.default", Assert.Single(context.Scopes));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Strips_credentials_before_following_one_allowlisted_blob_redirect()
    {
        var blob = Encoding.UTF8.GetBytes("bundle");
        var blobDigest = Digest(blob);
        var handler = OAuthThen(request =>
        {
            var uri = request.RequestUri!;
            if (uri.Host == Registry)
            {
                Assert.Equal($"/v2/{Repository}/blobs/{blobDigest}", uri.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
                {
                    Headers = { Location = new Uri($"https://{RedirectHost}/blob?sig=secret") }
                });
            }

            Assert.Equal(RedirectHost, uri.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(BytesResponse(blob));
        });

        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        Assert.Equal(blob, await session.ReadBlobAsync(blobDigest, ReleaseRegistryProtocol.MaximumBundleBytes));
        Assert.Equal(4, handler.Requests.Count);
        Assert.DoesNotContain("secret", handler.Requests[^1].Headers.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_an_unallowlisted_blob_redirect_without_following_it()
    {
        var blobDigest = Digest(Encoding.UTF8.GetBytes("bundle"));
        var handler = OAuthThen(request =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://attacker.example/blob") }
            });
        });

        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        await Assert.ThrowsAsync<ReleaseRegistryReadException>(async () =>
            await session.ReadBlobAsync(blobDigest, ReleaseRegistryProtocol.MaximumBundleBytes));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Rejects_an_invalid_digest_before_network_access()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException());
        var credential = new RecordingCredential();
        await using var session = await CreateReader(handler, credential).OpenAsync();

        await Assert.ThrowsAsync<ReleaseRegistryReadException>(async () =>
            await session.ReadBlobAsync("not-a-digest", ReleaseRegistryProtocol.MaximumBundleBytes));

        Assert.Empty(handler.Requests);
        Assert.Empty(credential.Contexts);
    }

    [Fact]
    public async Task Rejects_chunked_blob_that_exceeds_the_caller_cap()
    {
        var blob = Encoding.UTF8.GetBytes("bundle");
        var blobDigest = Digest(blob);
        var handler = OAuthThen(request => Task.FromResult(ContentResponse(new ChunkedContent(blob), null)));

        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        await Assert.ThrowsAsync<ReleaseRegistryReadException>(async () =>
            await session.ReadBlobAsync(blobDigest, maximumBytes: 2));
    }

    [Fact]
    public async Task Reads_paginated_referrers_and_requires_same_subject_path()
    {
        var firstDescriptorDigest = Digest(Encoding.UTF8.GetBytes("bundle-a"));
        var secondDescriptorDigest = Digest(Encoding.UTF8.GetBytes("bundle-b"));
        var page = 0;
        var handler = OAuthThen(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/referrers/", StringComparison.Ordinal))
            {
                page++;
                var descriptor = page == 1
                    ? DescriptorJson(firstDescriptorDigest)
                    : DescriptorJson(secondDescriptorDigest);
                var response = JsonResponse(IndexJson(descriptor), ReleaseRegistryProtocol.IndexMediaType);
                if (page == 1)
                    response.Headers.TryAddWithoutValidation(
                        "Link",
                        $"</v2/{Repository}/referrers/{SubjectDigest}?page=2>; rel=\"next\"");
                return Task.FromResult(response);
            }

            throw new InvalidOperationException();
        });

        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        var descriptors = await session.ReadReferrersAsync(SubjectDigest);

        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal(ReleaseRegistryProtocol.ManifestMediaType, descriptor.MediaType);
            Assert.Equal(BundleType, descriptor.ArtifactType);
        });
        Assert.Equal(2, handler.Requests.Count(request => request.RequestUri!.AbsolutePath.Contains("/referrers/", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("https://attacker.example/v2/release-manifests/release-manifest/referrers/sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa?page=2")]
    [InlineData("/v2/other/referrers/sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa?page=2")]
    [InlineData("http://demo123.azurecr.io/v2/release-manifests/release-manifest/referrers/sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa?page=2")]
    public async Task Rejects_referrer_continuation_that_leaves_the_exact_subject_path(string continuation)
    {
        var handler = OAuthThen(request =>
        {
            var response = JsonResponse(IndexJson(DescriptorJson(Digest(Encoding.UTF8.GetBytes("bundle")))), ReleaseRegistryProtocol.IndexMediaType);
            response.Headers.TryAddWithoutValidation("Link", $"<{continuation}>; rel=\"next\"");
            return Task.FromResult(response);
        });

        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        await Assert.ThrowsAsync<ReleaseRegistryReadException>(async () =>
            await session.ReadReferrersAsync(SubjectDigest));
    }

    [Fact]
    public async Task Rejects_a_referrer_page_cycle()
    {
        var handler = OAuthThen(request =>
        {
            var response = JsonResponse(IndexJson(DescriptorJson(Digest(Encoding.UTF8.GetBytes("bundle")))), ReleaseRegistryProtocol.IndexMediaType);
            response.Headers.TryAddWithoutValidation(
                "Link",
                $"</v2/{Repository}/referrers/{SubjectDigest}?artifactType={Uri.EscapeDataString(BundleType)}>; rel=\"next\"");
            return Task.FromResult(response);
        });

        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        await Assert.ThrowsAsync<ReleaseRegistryReadException>(async () =>
            await session.ReadReferrersAsync(SubjectDigest));
    }

    [Fact]
    public void Rejects_invalid_authority_and_copies_redirect_host_options()
    {
        var invalid = new AcrReleaseRegistryAuthority(
            "demo123.azurecr.io:443",
            Repository,
            Tenant,
            Client,
            [],
            TimeSpan.FromSeconds(1));
        Assert.Throws<ArgumentException>(() => new AcrReleaseRegistryReader(invalid, new RecordingCredential(), new HttpClient()));

        var hosts = new List<string> { RedirectHost };
        var authority = Authority(hosts);
        var reader = new AcrReleaseRegistryReader(authority, new RecordingCredential(), new HttpClient());
        hosts.Clear();
        Assert.NotNull(reader);
    }

    [Fact]
    public async Task Propagates_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var session = await CreateReader(handler, new RecordingCredential()).OpenAsync();
        var running = session.ReadBlobAsync(Digest(Encoding.UTF8.GetBytes("bundle")), 64, cancellation.Token).AsTask();

        await Task.Delay(50);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    private static AcrReleaseRegistryAuthority Authority(IReadOnlyList<string>? redirectHosts = null) =>
        new(Registry, Repository, Tenant, Client, redirectHosts ?? [RedirectHost], TimeSpan.FromSeconds(5));

    private static AcrReleaseRegistryReader CreateReader(RecordingHandler handler, RecordingCredential credential) =>
        new(Authority(), credential, new HttpClient(handler));

    private static RecordingHandler OAuthThen(Func<HttpRequestMessage, Task<HttpResponseMessage>> next) =>
        new(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/exchange")
                return JsonResponse("{\"refresh_token\":\"refresh-token\"}");
            if (request.RequestUri.AbsolutePath == "/oauth2/token")
                return JsonResponse("{\"access_token\":\"acr-token\"}");
            return await next(request);
        });

    private static string Digest(byte[] value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static HttpResponseMessage JsonResponse(string json, string? mediaType = "application/json") =>
        BytesResponse(Encoding.UTF8.GetBytes(json), mediaType);

    private static HttpResponseMessage BytesResponse(byte[] bytes, string? mediaType = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        if (mediaType is not null)
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private static HttpResponseMessage ContentResponse(HttpContent content, string? mediaType = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        };
        if (mediaType is not null)
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private static string IndexJson(string descriptor) =>
        $"{{\"mediaType\":\"{ReleaseRegistryProtocol.IndexMediaType}\",\"manifests\":[{descriptor}]}}";

    private static string DescriptorJson(string digest) =>
        $"{{\"mediaType\":\"{ReleaseRegistryProtocol.ManifestMediaType}\",\"size\":7,\"digest\":\"{digest}\",\"artifactType\":\"{BundleType}\"}}";

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpRequestMessage request)
    {
        var content = await request.Content!.ReadAsStringAsync();
        return content.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(item => Uri.UnescapeDataString(item[0]), item => Uri.UnescapeDataString(item[1]));
    }

    private sealed class RecordingCredential(string token = "aad-token") : TokenCredential
    {
        public List<TokenRequestContext> Contexts { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(token, DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts.Add(requestContext);
            return ValueTask.FromResult(new AccessToken(token, DateTimeOffset.UtcNow.AddMinutes(5)));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        public List<HttpRequestMessage> Requests { get; } = [];

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            : this((request, _) => handler(request)) { }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await _handler(request, cancellationToken);
        }
    }

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
