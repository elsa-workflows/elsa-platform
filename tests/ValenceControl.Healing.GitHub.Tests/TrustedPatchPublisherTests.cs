using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using FluentAssertions;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class TrustedPatchPublisherTests
{
    private const string TargetRevision = "cccccccccccccccccccccccccccccccccccccccc";

    [Theory]
    [MemberData(nameof(MaliciousDiffs))]
    public async Task Malicious_or_self_protecting_patch_is_rejected_before_credentials_are_minted(string diff)
    {
        using var rsa = RSA.Create(2048);
        var context = Context(rsa);
        var handler = new Handler(_ => throw new InvalidOperationException("Token must not be requested"));
        using var client = Client(handler);
        var repository = new RepositoryPublisher(TargetRevision);
        var publisher = new TrustedGitHubPatchPublisher(
            new GitHubAppTokenProvider(client), new ContextResolver(context), repository);

        var act = () => publisher.PublishAsync(Request(context, diff)).AsTask();

        await act.Should().ThrowAsync<GitHubSecurityException>();
        handler.Count.Should().Be(0);
        repository.PublishCount.Should().Be(0);
    }

    public static TheoryData<string> MaliciousDiffs => new()
    {
        // Traversal.
        "diff --git a/src/ok.cs b/src/../secret.cs\n--- a/src/ok.cs\n+++ b/src/../secret.cs\n@@ -1 +1 @@\n-old\n+new\n",
        // Binary patch.
        "diff --git a/src/a.bin b/src/a.bin\nindex aaa..bbb 100644\nGIT binary patch\nliteral 1\nA\n",
        // Symlink.
        "diff --git a/src/link b/src/link\nnew file mode 120000\n--- /dev/null\n+++ b/src/link\n@@ -0,0 +1 @@\n+target\n",
        // Submodule.
        "diff --git a/src/module b/src/module\nnew file mode 160000\n--- /dev/null\n+++ b/src/module\n@@ -0,0 +1 @@\n+Subproject commit aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n",
        // Permanent self-protecting workflow path.
        "diff --git a/.github/workflows/heal.yml b/.github/workflows/heal.yml\n--- a/.github/workflows/heal.yml\n+++ b/.github/workflows/heal.yml\n@@ -1 +1 @@\n-old\n+new\n"
    };

    [Fact]
    public async Task Stale_target_sha_is_rejected_after_read_only_comparison_and_before_publication()
    {
        using var rsa = RSA.Create(2048);
        var context = Context(rsa);
        var handler = TokenHandler();
        using var client = Client(handler);
        var repository = new RepositoryPublisher(new string('d', 40));
        var publisher = new TrustedGitHubPatchPublisher(
            new GitHubAppTokenProvider(client), new ContextResolver(context), repository);

        var act = () => publisher.PublishAsync(Request(context, SafeDiff())).AsTask();

        await act.Should().ThrowAsync<GitHubSecurityException>()
            .Where(x => x.ReasonCode == GitHubSecurityReasonCodes.TargetRevisionStale);
        repository.PublishCount.Should().Be(0);
    }

    [Fact]
    public async Task Valid_patch_uses_content_and_pr_only_token_and_builds_deterministic_draft_plan()
    {
        using var rsa = RSA.Create(2048);
        var context = Context(rsa);
        string? tokenPayload = null;
        var handler = new Handler(async request =>
        {
            tokenPayload = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.Created, new { token = "publish-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(30) });
        });
        using var client = Client(handler);
        var repository = new RepositoryPublisher(TargetRevision);
        var publisher = new TrustedGitHubPatchPublisher(
            new GitHubAppTokenProvider(client), new ContextResolver(context), repository);

        var result = await publisher.PublishAsync(Request(context, SafeDiff(), "inferred-high-confidence"));

        result.Number.Should().Be(12);
        handler.Count.Should().Be(2);
        repository.Plan.Should().NotBeNull();
        repository.Plan!.Branch.Should().StartWith("valence-control-healing/");
        repository.Plan.IsDraft.Should().BeTrue();
        repository.Plan.Patch.Files.Select(x => x.EffectivePath).Should().Equal("src/A.cs");
        using var payload = JsonDocument.Parse(tokenPayload!);
        payload.RootElement.GetProperty("permissions").EnumerateObject().Select(x => x.Name)
            .Should().BeEquivalentTo("contents", "pull_requests", "metadata");
    }

    [Fact]
    public void Unified_diff_parser_rejects_malformed_hunk_counts()
    {
        var malformed = "diff --git a/src/A.cs b/src/A.cs\n--- a/src/A.cs\n+++ b/src/A.cs\n@@ -1,2 +1,1 @@\n-old\n+new\n";

        var act = () => UnifiedDiffParser.Parse(malformed);

        act.Should().Throw<GitHubSecurityException>();
    }

    [Fact]
    public async Task Existing_repair_branch_must_match_the_recomputed_patch_tree()
    {
        const string existingRevision = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        const string baseTree = "1111111111111111111111111111111111111111";
        const string expectedTree = "2222222222222222222222222222222222222222";
        var attemptId = Guid.NewGuid();
        const string idempotencyKey = "publish:tree-bound";
        var plan = new TrustedGitHubPatchPlan(
            $"valence-control-healing/{attemptId:N}", "main", TargetRevision, "repair commit", "repair", "body", false,
            UnifiedDiffParser.Parse(SafeDiff()));
        var expectedMessage = $"{plan.CommitMessage}\n\nValence-Control-Healing-Idempotency: {Convert.ToHexString(Encoding.UTF8.GetBytes(idempotencyKey))}";
        var handler = new Handler(async request =>
        {
            await Task.CompletedTask;
            var path = request.RequestUri!.PathAndQuery;
            if (request.Method == HttpMethod.Get && path.Contains("/git/ref/heads/valence-control-healing/", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new { @object = new { sha = existingRevision } });
            if (request.Method == HttpMethod.Get && path.EndsWith($"/git/commits/{TargetRevision}", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new { tree = new { sha = baseTree }, message = "base", parents = Array.Empty<object>() });
            if (request.Method == HttpMethod.Get && path.Contains($"/git/trees/{baseTree}?recursive=1", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new { truncated = false, tree = new[] { new { path = "src/A.cs", mode = "100644", type = "blob" } } });
            if (request.Method == HttpMethod.Get && path.Contains("/contents/src/A.cs", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("old") };
            if (request.Method == HttpMethod.Post && path.EndsWith("/git/blobs", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, new { sha = "3333333333333333333333333333333333333333" });
            if (request.Method == HttpMethod.Post && path.EndsWith("/git/trees", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, new { sha = expectedTree });
            if (request.Method == HttpMethod.Get && path.EndsWith($"/git/commits/{existingRevision}", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, new
                {
                    tree = new { sha = "4444444444444444444444444444444444444444" },
                    message = expectedMessage,
                    parents = new[] { new { sha = TargetRevision } }
                });
            throw new InvalidOperationException($"Unexpected GitHub request: {request.Method} {path}");
        });
        using var client = Client(handler);
        using var rsa = RSA.Create(2048);
        var authorization = Context(rsa).Authorization;
        var publisher = new GitHubHttpTrustedRepositoryPublisher(client);

        var act = () => publisher.PublishAsync(authorization, "token", plan, idempotencyKey).AsTask();

        await act.Should().ThrowAsync<GitHubSecurityException>()
            .Where(x => x.ReasonCode == GitHubSecurityReasonCodes.IdempotencyConflict);
    }

    private static RepairPublicationRequest Request(
        TrustedGitHubPublicationContext context,
        string diff,
        string classification = "reproduced")
    {
        var attemptId = Guid.NewGuid();
        var result = new RepairResultEnvelope(
            HealingContractVersions.AgentProtocol, attemptId, "100", 1, TargetRevision, TargetRevision,
            classification, 0.98m, "Null guard required", diff, Hash(diff), [],
            new RepairReproductionEvidence(true, classification == "reproduced", classification, "Observed", ["dotnet test"]),
            new RepairRegressionEvidence(true, "Added a regression test", ["A.Tests.cs"]),
            [new RepairValidationResult("test", "dotnet test", "passed", "All tests pass", TimeSpan.FromSeconds(3))],
            ["low-risk"], "Revert this commit", new RepairUsageSummary(10, 20, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)),
            new RepairTimingSummary(DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        return new RepairPublicationRequest(
            HealingContractVersions.ProviderProtocol,
            new ProviderRepositoryReference(context.Authorization.ProviderConnectionId, context.Authorization.RepositoryProviderId,
                context.Authorization.Owner, context.Authorization.Name),
            Guid.NewGuid(), Guid.NewGuid(), attemptId, "main", TargetRevision, result,
            new PolicyEvaluationSnapshot(
                HealingContractVersions.PolicyProtocol, context.PathPolicy.PolicyVersion, context.PathPolicy.PolicyHash,
                new string('e', 64), PolicyDecisions.AllowPublication,
                [new PolicyGateResult("path", PolicyGateState.Pass, "allowed")], DateTimeOffset.UtcNow),
            "publish:1");
    }

    private static TrustedGitHubPublicationContext Context(RSA rsa) => new(
        new GitHubRepositoryAuthorization(Guid.NewGuid(), "987", "acme", "app", "42",
            new GitHubAppCredential("123", rsa.ExportRSAPrivateKeyPem()), new Dictionary<string, GitHubApprovedWorkflow>()),
        new TrustedGitHubPublicationPolicy("1", new string('a', 64), ["src"], [], 5, 100, 100_000));

    private static string SafeDiff() =>
        "diff --git a/src/A.cs b/src/A.cs\nindex aaaaaaa..bbbbbbb 100644\n--- a/src/A.cs\n+++ b/src/A.cs\n@@ -1 +1 @@\n-old\n+new\n";

    private static Handler TokenHandler() => new(_ => Task.FromResult(Json(HttpStatusCode.Created,
        new { token = "publish-token", expires_at = DateTimeOffset.UtcNow.AddMinutes(30) })));
    private static string Hash(string value) => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";
    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://api.github.com/") };
    private static HttpResponseMessage Json(HttpStatusCode status, object payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
    };

    private sealed class ContextResolver(TrustedGitHubPublicationContext context) : ITrustedGitHubPublicationContextResolver
    {
        public ValueTask<TrustedGitHubPublicationContext?> ResolveAsync(RepairPublicationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TrustedGitHubPublicationContext?>(context);
    }

    private sealed class RepositoryPublisher(string currentRevision) : ITrustedGitHubRepositoryPublisher
    {
        public int PublishCount { get; private set; }
        public TrustedGitHubPatchPlan? Plan { get; private set; }
        public ValueTask<string?> GetTargetRevisionAsync(GitHubRepositoryAuthorization authorization, string targetBranch, string installationToken, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(currentRevision);
        public ValueTask<bool> IsCommitReachableAsync(GitHubRepositoryAuthorization authorization, string ancestorRevision, string targetRevision, string installationToken, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(string.Equals(ancestorRevision, targetRevision, StringComparison.OrdinalIgnoreCase));
        public ValueTask<ProviderPullRequestReference> PublishAsync(GitHubRepositoryAuthorization authorization, string installationToken, TrustedGitHubPatchPlan plan, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            Plan = plan;
            return ValueTask.FromResult(new ProviderPullRequestReference("91", 12,
                new Uri("https://github.com/acme/app/pull/12"), new string('d', 40), TargetRevision, plan.IsDraft, "request-1"));
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
