using System.Net;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Xunit;
using ElsaControl.Api.Catalog;

namespace ElsaControl.Api.Tests;

public sealed class CatalogManagedIdentityCredentialPipelineTests
{
    [Fact]
    public async Task Actual_credential_reaches_token_after_unsupported_capability_without_probe_retries()
    {
        using var handler = new ImdsHandler();
        using var client = new HttpClient(handler);
        var options = new ManagedIdentityCredentialOptions(
            ManagedIdentityId.FromUserAssignedClientId("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"))
        {
            RetryPolicy = new ManagedIdentityProbeRetryPolicy(),
            Transport = new HttpClientTransport(client)
        };
        var credential = new ManagedIdentityCredential(options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://database.windows.net/.default"]), timeout.Token);

        Assert.Equal("synthetic-test-token", token.Token);
        Assert.Equal("capability", handler.Stages[0]);
        Assert.Equal("token", handler.Stages[^1]);
        Assert.Equal(1, handler.Stages.Count(x => x == "availability"));
        Assert.Equal(1, handler.Stages.Count(x => x == "token"));
        // The SDK may re-detect capabilities when it switches from its availability probe to MSAL.
        // This smoke test permits that re-detection, not a proof of zero retries: the exact
        // sync/async404 predicate tests separately enforce the no-retry decision.
        Assert.InRange(handler.Stages.Count(x => x == "capability"), 1, 2);
        Assert.DoesNotContain(handler.Stages.Zip(handler.Stages.Skip(1)), pair =>
            pair.First == "capability" && pair.Second == "capability");
    }

    private sealed class ImdsHandler : HttpMessageHandler
    {
        public List<string> Stages { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.Equal("169.254.169.254", uri.Host);
            Assert.Equal(HttpMethod.Get, request.Method);
            if (uri.AbsolutePath == "/metadata/identity/getplatformmetadata")
            {
                Assert.Equal("?cred-api-version=2.0&client_id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", uri.Query);
                Assert.False(request.Headers.Contains("Metadata"));
                Stages.Add("capability");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            Assert.Equal("/metadata/identity/oauth2/token", uri.AbsolutePath);
            if (!request.Headers.Contains("Metadata"))
            {
                Stages.Add("availability");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            Stages.Add("token");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"access_token":"synthetic-test-token","expires_on":"4102444800","resource":"https://database.windows.net/","token_type":"Bearer"}
                    """)
            });
        }
    }
}
