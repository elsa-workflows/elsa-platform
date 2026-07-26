using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace ValenceControl.Healing.GitHub.Tests;

public sealed class GitHubWebhookVerifierTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("a-long-random-webhook-secret-value");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""
        {"action":"completed","installation":{"id":42},"repository":{"id":987,"full_name":"acme/app"}}
        """);

    [Fact]
    public async Task Signature_is_verified_before_payload_is_allowlisted_and_delivery_is_recorded()
    {
        var store = new ReplayStore();
        var verifier = new GitHubWebhookVerifier(store);

        var result = await verifier.VerifyAsync(Request(Body, Signature(Body), "delivery-1"));

        result.Succeeded.Should().BeTrue();
        result.Webhook.Should().Match<VerifiedGitHubWebhook>(x =>
            x.InstallationId == "42" && x.RepositoryId == "987" && x.Action == "completed");
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task Exact_redelivery_is_accepted_with_an_explicit_replay_signal()
    {
        var verifier = new GitHubWebhookVerifier(new ReplayStore());
        var request = Request(Body, Signature(Body), "delivery-1");

        (await verifier.VerifyAsync(request)).Succeeded.Should().BeTrue();
        var replay = await verifier.VerifyAsync(request);

        replay.Succeeded.Should().BeTrue();
        replay.IsReplay.Should().BeTrue();
        replay.ReasonCode.Should().Be(GitHubSecurityReasonCodes.WebhookReplay);
    }

    [Fact]
    public async Task Reusing_a_delivery_id_with_a_different_body_fails_closed()
    {
        var verifier = new GitHubWebhookVerifier(new ReplayStore());
        var first = Request(Body, Signature(Body), "delivery-1");
        var changedBody = Encoding.UTF8.GetBytes("""
            {"action":"completed","installation":{"id":42},"repository":{"id":987,"full_name":"acme/app"},"changed":true}
            """);

        (await verifier.VerifyAsync(first)).Succeeded.Should().BeTrue();
        var conflict = await verifier.VerifyAsync(Request(changedBody, Signature(changedBody), "delivery-1"));

        conflict.Succeeded.Should().BeFalse();
        conflict.IsReplay.Should().BeFalse();
        conflict.ReasonCode.Should().Be(GitHubSecurityReasonCodes.WebhookReplay);
    }

    [Theory]
    [InlineData("bad-signature")]
    [InlineData("wrong-installation")]
    [InlineData("wrong-repository")]
    [InlineData("event-not-allowed")]
    [InlineData("action-not-allowed")]
    public async Task Authenticity_identity_and_event_allowlists_fail_closed(string scenario)
    {
        var body = scenario switch
        {
            "wrong-installation" => Encoding.UTF8.GetBytes("{\"action\":\"completed\",\"installation\":{\"id\":43},\"repository\":{\"id\":987,\"full_name\":\"acme/app\"}}"),
            "wrong-repository" => Encoding.UTF8.GetBytes("{\"action\":\"completed\",\"installation\":{\"id\":42},\"repository\":{\"id\":111,\"full_name\":\"attacker/app\"}}"),
            "action-not-allowed" => Encoding.UTF8.GetBytes("{\"action\":\"requested\",\"installation\":{\"id\":42},\"repository\":{\"id\":987,\"full_name\":\"acme/app\"}}"),
            _ => Body
        };
        var signature = scenario == "bad-signature" ? "sha256=" + new string('0', 64) : Signature(body);
        var request = Request(body, signature, "delivery-x") with
        {
            Event = scenario == "event-not-allowed" ? "push" : "workflow_run"
        };
        var store = new ReplayStore();

        var result = await new GitHubWebhookVerifier(store).VerifyAsync(request);

        result.ReasonCode.Should().Be(GitHubSecurityReasonCodes.WebhookInvalid);
        store.Count.Should().Be(0);
    }

    private static GitHubWebhookVerificationRequest Request(byte[] body, string signature, string delivery) => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), body, signature, delivery, "workflow_run", Secret, "42", "987", "acme/app",
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["workflow_run"] = new HashSet<string>(["completed"], StringComparer.Ordinal)
        });

    private static string Signature(byte[] body) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Secret, body)).ToLowerInvariant();

    private sealed class ReplayStore : IGitHubWebhookReplayStore
    {
        private readonly Dictionary<string, string> _deliveries = new(StringComparer.Ordinal);
        public int Count => _deliveries.Count;
        public ValueTask<GitHubWebhookReplayResult> TryAcceptAsync(
            GitHubWebhookReplayRecord delivery,
            CancellationToken cancellationToken = default)
        {
            var key = $"{delivery.WorkspaceId:N}:{delivery.DeliveryId}";
            if (!_deliveries.TryAdd(key, delivery.BodyDigest))
                return ValueTask.FromResult(string.Equals(_deliveries[key], delivery.BodyDigest, StringComparison.Ordinal)
                    ? GitHubWebhookReplayResult.ExactReplay(_deliveries[key])
                    : GitHubWebhookReplayResult.Conflict(_deliveries[key]));
            return ValueTask.FromResult(GitHubWebhookReplayResult.Accepted());
        }
    }
}
