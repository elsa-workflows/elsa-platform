using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class GitHubRepairProviderTests
{
    [Fact]
    public async Task Issue_projection_uses_issue_only_token_and_replays_without_another_provider_call()
    {
        using var rsa = RSA.Create(2048);
        var authorization = Authorization(rsa);
        var requests = new List<(HttpMethod Method, string Path, string? Body)>();
        var handler = new Handler(async request =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            if (request.RequestUri.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, new { token = "narrow-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(30) });
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, Array.Empty<object>());
            return Json(HttpStatusCode.Created, new { id = 91L, number = 7L, html_url = "https://github.com/acme/app/issues/7", state = "open", body = "" });
        });
        var ledger = new Ledger();
        using var client = Client(handler);
        var provider = new GitHubRepairWorkProvider(client, new GitHubAppTokenProvider(client), new Resolver(authorization), ledger);
        const string summary = "redacted summary for @here";
        var request = new RepairWorkItemUpsertRequest(
            HealingContractVersions.ProviderProtocol, Repository(authorization), Guid.NewGuid(), Guid.NewGuid(),
            "Unhandled failure", summary, Hash(summary), "issue:1");

        var first = await provider.UpsertWorkItemAsync(request);
        var replay = await provider.UpsertWorkItemAsync(request);

        Assert.Equal(first, replay);
        Assert.Equal(3, requests.Count());
        using var tokenRequest = JsonDocument.Parse(requests[0].Body!);
        var permissions = tokenRequest.RootElement.GetProperty("permissions");
        Assert.Equal("write", permissions.GetProperty("issues").GetString());
        Assert.Equal("read", permissions.GetProperty("metadata").GetString());
        Assert.Equivalent(new[] { "issues", "metadata" }, permissions.EnumerateObject().Select(x => x.Name));
        Assert.Contains("valence-control-healing:incident:", requests[2].Body);
        Assert.DoesNotContain("@here", requests[2].Body);
    }

    [Fact]
    public async Task Workflow_dispatch_requires_the_approved_identity_and_revision_and_is_idempotent()
    {
        using var rsa = RSA.Create(2048);
        var authorization = Authorization(rsa);
        var requests = new List<(string Path, string Body)>();
        var handler = new Handler(async request =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add((request.RequestUri!.AbsolutePath, body));
            return request.RequestUri.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal)
                ? Json(HttpStatusCode.Created, new { token = "actions-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(30) })
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var client = Client(handler);
        var provider = new GitHubRepairWorkProvider(client, new GitHubAppTokenProvider(client), new Resolver(authorization), new Ledger());
        var request = Dispatch(authorization, ".github/workflows/heal.yml", new string('b', 40), "dispatch:1");

        var first = await provider.DispatchWorkflowAsync(request);
        var replay = await provider.DispatchWorkflowAsync(request);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(2, requests.Count());
        using var tokenRequest = JsonDocument.Parse(requests[0].Body);
        Assert.Equivalent(new[] { "actions", "metadata" }, tokenRequest.RootElement.GetProperty("permissions").EnumerateObject().Select(x => x.Name));
        using var dispatch = JsonDocument.Parse(requests[1].Body);
        Assert.Equal("healing-workflow-v1", dispatch.RootElement.GetProperty("ref").GetString());
        Assert.True(dispatch.RootElement.GetProperty("inputs").TryGetProperty("attempt_nonce", out _));
    }

    [Fact]
    public async Task Workflow_dispatch_rejects_unapproved_workflow_before_minting_a_token()
    {
        using var rsa = RSA.Create(2048);
        var authorization = Authorization(rsa);
        var handler = new Handler(_ => throw new InvalidOperationException("GitHub must not be called"));
        using var client = Client(handler);
        var provider = new GitHubRepairWorkProvider(client, new GitHubAppTokenProvider(client), new Resolver(authorization), new Ledger());

        var act = () => provider.DispatchWorkflowAsync(Dispatch(
            authorization, ".github/workflows/other.yml", new string('b', 40), "dispatch:2")).AsTask();

        var exception = await Assert.ThrowsAsync<GitHubSecurityException>(act);
        Assert.Equal(GitHubSecurityReasonCodes.WorkflowNotAuthorized, exception.ReasonCode);
        Assert.Equal(0, handler.Count);
    }

    private static RepairWorkflowDispatchRequest Dispatch(
        GitHubRepositoryAuthorization authorization, string workflow, string workflowRevision, string key) => new(
        HealingContractVersions.ProviderProtocol, Repository(authorization), Guid.NewGuid(), workflow, "refs/tags/healing-workflow-v1", workflowRevision,
        new Uri("https://control.example.test"), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        "verified", "main", new string('c', 40), key);

    private static ProviderRepositoryReference Repository(GitHubRepositoryAuthorization authorization) =>
        new(authorization.ProviderConnectionId, authorization.RepositoryProviderId, authorization.Owner, authorization.Name);

    private static GitHubRepositoryAuthorization Authorization(RSA rsa) => new(
        Guid.NewGuid(), "987", "acme", "app", "42", new GitHubAppCredential("123", rsa.ExportRSAPrivateKeyPem()),
        new Dictionary<string, GitHubApprovedWorkflow>
        {
            [".github/workflows/heal.yml"] = new(".github/workflows/heal.yml", "refs/tags/healing-workflow-v1", new string('b', 40))
        });

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.github.com/") };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private sealed class Resolver(GitHubRepositoryAuthorization authorization) : IGitHubRepositoryAuthorizationResolver
    {
        public ValueTask<GitHubRepositoryAuthorization?> ResolveAsync(Guid providerConnectionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GitHubRepositoryAuthorization?>(providerConnectionId == authorization.ProviderConnectionId ? authorization : null);
    }

    private sealed class Ledger : IGitHubProviderOperationLedger
    {
        private readonly Dictionary<GitHubProviderOperationKey, GitHubProviderOperationRecord> _items = [];
        public ValueTask<GitHubProviderOperationRecord?> GetAsync(GitHubProviderOperationKey key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.GetValueOrDefault(key));
        public ValueTask<bool> TryReserveAsync(GitHubProviderOperationKey key, string payload, string hash, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            if (_items.ContainsKey(key)) return ValueTask.FromResult(false);
            _items[key] = new GitHubProviderOperationRecord(key, payload, hash, GitHubProviderOperationStatus.Reserved, null, at);
            return ValueTask.FromResult(true);
        }
        public ValueTask CompleteAsync(GitHubProviderOperationKey key, string payload, string hash, string result, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            _items[key] = new GitHubProviderOperationRecord(key, payload, hash, GitHubProviderOperationStatus.Completed, result, at);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return callback(request);
        }
    }
}
