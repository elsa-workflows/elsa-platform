using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class GitHubMergeProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Merge_snapshot_refreshes_branch_protection_and_checks_with_read_only_tokens()
    {
        using var rsa = RSA.Create(2048);
        var authorization = Authorization(rsa);
        var tokenRequests = new List<JsonElement>();
        var checkRefreshes = 0;
        var handler = new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/access_tokens", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                tokenRequests.Add(document.RootElement.Clone());
                return Json(HttpStatusCode.Created, new { token = "narrow-token", expires_at = Now.AddMinutes(30) });
            }
            if (path.EndsWith("/pulls/17", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new
                {
                    number = 17,
                    state = "open",
                    draft = false,
                    head = new { sha = new string('a', 40), @ref = "healing/17" },
                    @base = new { sha = new string('b', 40), @ref = "main" },
                    mergeable = true,
                    mergeable_state = "clean"
                });
            if (path.EndsWith("/branches/main/protection", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new
                {
                    required_status_checks = new
                    {
                        strict = true,
                        contexts = new[] { "ci/test" },
                        checks = new[] { new { context = "security", app_id = 1 } }
                    }
                });
            if (path.EndsWith("/check-runs", StringComparison.Ordinal))
            {
                checkRefreshes++;
                var state = checkRefreshes == 1 ? "in_progress" : "completed";
                var conclusion = checkRefreshes == 1 ? null : "success";
                return Json(HttpStatusCode.OK, new
                {
                    total_count = 2,
                    check_runs = new object[]
                    {
                        new { name = "ci/test", status = state, conclusion, head_sha = new string('a', 40), app = new { id = 2 } },
                        new { name = "security", status = "completed", conclusion = "success", head_sha = new string('a', 40), app = new { id = checkRefreshes == 3 ? 999 : 1 } }
                    }
                });
            }
            if (path.EndsWith("/statuses", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, Array.Empty<object>());
            throw new InvalidOperationException($"Unexpected GitHub request: {request.Method} {request.RequestUri}");
        });
        using var client = Client(handler);
        var provider = Provider(client, authorization, new Ledger());

        var pending = await provider.GetMergeSnapshotAsync(Repository(authorization), "17");
        var passing = await provider.GetMergeSnapshotAsync(Repository(authorization), "17");
        var spoofedApp = await provider.GetMergeSnapshotAsync(Repository(authorization), "17");

        Assert.Equal(new string('a', 40), pending.HeadRevision);
        Assert.True(pending.IsOpen);
        Assert.False(pending.IsDraft);
        Assert.Equal(new string('b', 40), pending.BaseRevision);
        Assert.Equal(["ci/test", "security"], pending.RequiredChecks);
        Assert.Equal("in_progress", pending.Checks.Single(x => x.Name == "ci/test").State);
        Assert.False(pending.IsBranchProtectionSatisfied);
        Assert.All(passing.Checks, x => Assert.Equal("success", x.State));
        Assert.True(passing.IsBranchProtectionSatisfied);
        Assert.False(spoofedApp.IsBranchProtectionSatisfied);
        Assert.Equal(3, checkRefreshes);

        Assert.Equal(9, tokenRequests.Count());
        Assert.Equivalent(new[] { "pull_requests", "metadata" }, PermissionNames(tokenRequests[0]));
        Assert.Equivalent(new[] { "administration", "metadata" }, PermissionNames(tokenRequests[1]));
        Assert.Equivalent(new[] { "checks", "statuses", "metadata" }, PermissionNames(tokenRequests[2]));
        foreach (var refresh in tokenRequests.Chunk(3))
        {
            Assert.Equivalent(new[] { "pull_requests", "metadata" }, PermissionNames(refresh[0]));
            Assert.Equivalent(new[] { "administration", "metadata" }, PermissionNames(refresh[1]));
            Assert.Equivalent(new[] { "checks", "statuses", "metadata" }, PermissionNames(refresh[2]));
        }
        Assert.DoesNotContain("contents", tokenRequests.SelectMany(x => PermissionNames(x)));
    }

    [Fact]
    public async Task Merge_request_uses_contents_only_write_token_and_replays_without_another_provider_call()
    {
        using var rsa = RSA.Create(2048);
        var authorization = Authorization(rsa);
        var requests = new List<(string Path, string Body)>();
        var handler = new Handler(async request =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add((request.RequestUri!.AbsolutePath, body));
            if (request.RequestUri.AbsolutePath.EndsWith("/access_tokens", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, new { token = "merge-token", expires_at = Now.AddMinutes(30) });
            var response = Json(HttpStatusCode.OK, new { sha = new string('c', 40), merged = true, message = "Pull Request successfully merged" });
            response.Headers.Add("X-GitHub-Request-Id", "provider-request-1");
            return response;
        });
        using var client = Client(handler);
        var provider = Provider(client, authorization, new Ledger());
        var request = MergeRequest(authorization, "merge:17");

        var first = await provider.RequestMergeAsync(request);
        var replay = await provider.RequestMergeAsync(request);

        Assert.False(first.IsReplay);
        Assert.Equal("provider-request-1", first.ProviderCorrelationId);
        Assert.Equal(first with { IsReplay = true }, replay);
        Assert.Equal(2, requests.Count());
        using var tokenRequest = JsonDocument.Parse(requests[0].Body);
        Assert.Equivalent(new[] { "contents", "metadata" }, PermissionNames(tokenRequest.RootElement));
        Assert.Equal("write", tokenRequest.RootElement.GetProperty("permissions").GetProperty("contents").GetString());
        using var mergeRequest = JsonDocument.Parse(requests[1].Body);
        Assert.Equal(new string('a', 40), mergeRequest.RootElement.GetProperty("sha").GetString());
        Assert.Equal("squash", mergeRequest.RootElement.GetProperty("merge_method").GetString());
    }

    [Fact]
    public async Task Merge_request_rejects_a_nonpassing_policy_before_contacting_GitHub()
    {
        using var rsa = RSA.Create(2048);
        var authorization = Authorization(rsa);
        var handler = new Handler(_ => throw new InvalidOperationException("GitHub must not be called"));
        using var client = Client(handler);
        var provider = Provider(client, authorization, new Ledger());
        var request = MergeRequest(authorization, "merge:blocked") with
        {
            MergePolicy = Policy(PolicyDecisions.Deny, PolicyGateState.Block)
        };

        var act = () => provider.RequestMergeAsync(request).AsTask();

        var exception = await Assert.ThrowsAsync<GitHubSecurityException>(act);
        Assert.Equal(GitHubSecurityReasonCodes.InvalidRequest, exception.ReasonCode);
        Assert.Equal(0, handler.Count);
    }

    private static GitHubMergeProvider Provider(
        HttpClient client, GitHubRepositoryAuthorization authorization, IGitHubProviderOperationLedger ledger) =>
        new(client, new GitHubAppTokenProvider(client, new FixedTimeProvider(Now)), new Resolver(authorization), ledger,
            new FixedTimeProvider(Now));

    private static ProviderMergeRequest MergeRequest(GitHubRepositoryAuthorization authorization, string key) => new(
        HealingContractVersions.ProviderProtocol,
        Repository(authorization),
        "17",
        new string('a', 40),
        Policy(PolicyDecisions.AllowAutomaticMerge, PolicyGateState.Pass),
        key);

    private static PolicyEvaluationSnapshot Policy(string decision, PolicyGateState gate) => new(
        HealingContractVersions.PolicyProtocol,
        "merge/v1",
        new string('d', 64),
        new string('e', 64),
        decision,
        [new ValenceControl.Healing.Abstractions.PolicyGateResult(
            "repository-policy", gate, gate == PolicyGateState.Pass ? "passed" : "blocked")],
        Now);

    private static ProviderRepositoryReference Repository(GitHubRepositoryAuthorization authorization) =>
        new(authorization.ProviderConnectionId, authorization.RepositoryProviderId, authorization.Owner, authorization.Name);

    private static GitHubRepositoryAuthorization Authorization(RSA rsa) => new(
        Guid.NewGuid(), "987", "acme", "app", "42", new GitHubAppCredential("123", rsa.ExportRSAPrivateKeyPem()),
        new Dictionary<string, GitHubApprovedWorkflow>());

    private static IReadOnlyList<string> PermissionNames(JsonElement tokenRequest) =>
        tokenRequest.GetProperty("permissions").EnumerateObject().Select(x => x.Name).ToArray();

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.github.com/") };
    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Resolver(GitHubRepositoryAuthorization authorization) : IGitHubRepositoryAuthorizationResolver
    {
        public ValueTask<GitHubRepositoryAuthorization?> ResolveAsync(
            Guid providerConnectionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GitHubRepositoryAuthorization?>(
                providerConnectionId == authorization.ProviderConnectionId ? authorization : null);
    }

    private sealed class Ledger : IGitHubProviderOperationLedger
    {
        private readonly Dictionary<GitHubProviderOperationKey, GitHubProviderOperationRecord> _items = [];

        public ValueTask<GitHubProviderOperationRecord?> GetAsync(
            GitHubProviderOperationKey key, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_items.GetValueOrDefault(key));

        public ValueTask<bool> TryReserveAsync(
            GitHubProviderOperationKey key, string payload, string hash, DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            if (_items.ContainsKey(key))
                return ValueTask.FromResult(false);
            _items[key] = new GitHubProviderOperationRecord(
                key, payload, hash, GitHubProviderOperationStatus.Reserved, null, at);
            return ValueTask.FromResult(true);
        }

        public ValueTask CompleteAsync(
            GitHubProviderOperationKey key, string payload, string hash, string result, DateTimeOffset at,
            CancellationToken cancellationToken = default)
        {
            _items[key] = new GitHubProviderOperationRecord(
                key, payload, hash, GitHubProviderOperationStatus.Completed, result, at);
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
