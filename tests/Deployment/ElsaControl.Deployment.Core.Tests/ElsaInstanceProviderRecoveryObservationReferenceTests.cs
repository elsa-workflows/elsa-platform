using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class ElsaInstanceProviderRecoveryObservationReferenceTests
{
    [Fact]
    public void Create_and_parse_round_trip_and_retry_evidence_requires_matching_digest()
    {
        var id = Guid.Parse("d2fd5f4a-6c63-4e25-ae39-5b6b2e4a5b50");
        var digest = "sha256:" + new string('a', 64);
        var reference = ElsaInstanceProviderRecoveryObservationReference.Create(id, digest);

        Assert.True(ElsaInstanceProviderRecoveryObservationReference.TryParse(reference, out var parsedId, out var parsedDigest));
        Assert.Equal(id, parsedId);
        Assert.Equal(digest, parsedDigest);
        var evidence = new ElsaInstanceProviderRetryEvidence(reference, digest);
        Assert.Equal(reference, evidence.Reference);
        Assert.Equal(digest, evidence.Digest);
        Assert.Throws<ArgumentException>(() => new ElsaInstanceProviderRetryEvidence(
            reference,
            "sha256:" + new string('b', 64)));
    }

    [Theory]
    [InlineData("urn:elsa-control:provider-recovery-observation:v1:D2FD5F4A6C634E25AE395B6B2E4A5B50:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("urn:elsa-control:provider-recovery-observation:v1:d2fd5f4a6c634e25ae395b6b2e4a5b50:sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("urn:elsa-control:provider-recovery-observation:v1:d2fd5f4a6c634e25ae395b6b2e4a5b50:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Parse_rejects_noncanonical_observation_references(string reference)
    {
        Assert.False(ElsaInstanceProviderRecoveryObservationReference.TryParse(reference, out _, out _));
        Assert.Throws<ArgumentException>(() => new ElsaInstanceProviderRetryEvidence(
            reference,
            "sha256:" + new string('a', 64)));
    }

    [Fact]
    public void Recovery_request_requires_an_accepted_recovery_envelope()
    {
        var request = new ElsaInstanceProviderRecoveryRequest(CreateSubmission(), null!);

        var exception = Assert.Throws<ArgumentNullException>(request.Validate);
        Assert.Equal("Envelope", exception.ParamName);
    }

    [Fact]
    public void Recovery_request_accepts_distinct_observation_and_recovery_ids()
    {
        var request = new ElsaInstanceProviderRecoveryRequest(CreateSubmission(), CreateEnvelope());

        request.Validate();
    }

    [Theory]
    [InlineData("instances\noperations", "recovery-key")]
    [InlineData("instances/operations", "recovery key")]
    [InlineData("instances/operations", "recovery:key")]
    public void Recovery_envelope_requires_canonical_idempotency_values(string scope, string key)
    {
        var envelope = CreateEnvelope() with { IdempotencyScope = scope, IdempotencyKey = key };

        Assert.Throws<InvalidOperationException>(envelope.Validate);
    }

    [Fact]
    public void Idempotency_scope_normalizes_using_the_shared_scope_policy()
    {
        Assert.Equal("instances/operations", ElsaInstanceIdempotencyScope.Normalize(" instances/operations "));
    }

    [Theory]
    [InlineData(2, 2, 4, 5)]
    [InlineData(3, 2, 4, 5)]
    [InlineData(1, 3, 4, 5)]
    [InlineData(int.MaxValue, 2, 4, 5)]
    [InlineData(1, 2, 4, 4)]
    [InlineData(1, 2, 4, 3)]
    public void Recovery_envelope_rejects_contradictory_acceptance_versions(
        int observedAttempt, int acceptedAttempt, int observedVersion, int acceptedVersion)
    {
        var envelope = CreateEnvelope() with
        {
            ObservedLifecycleAttemptNumber = observedAttempt,
            AcceptedLifecycleAttemptNumber = acceptedAttempt,
            ObservedInstanceVersion = observedVersion,
            AcceptedInstanceVersion = acceptedVersion
        };

        Assert.Throws<InvalidOperationException>(envelope.Validate);
    }

    [Fact]
    public void Recovery_envelope_allows_intervening_reconciliation_version_increment()
    {
        var envelope = CreateEnvelope() with { AcceptedInstanceVersion = 6 };

        envelope.Validate();
    }

    [Fact]
    public void Recovery_result_uses_a_fixed_summary_and_safe_code()
    {
        var result = new ElsaInstanceProviderRecoveryResult(
            ElsaInstanceProviderRecoveryOutcome.Succeeded,
            "provider.recovery.succeeded");

        result.Validate();

        Assert.Equal("Provider recovery succeeded.", result.Summary);
        Assert.Throws<InvalidOperationException>(() => new ElsaInstanceProviderRecoveryResult(
            ElsaInstanceProviderRecoveryOutcome.Failed,
            "Provider recovery failed").Validate());
    }

    private static ElsaInstanceProviderSubmission CreateSubmission()
    {
        return new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            2,
            ElsaDesiredLifecycle.Running,
            CreatePlan(),
            new(
                Guid.Parse("40000000-0000-0000-0000-000000000001"),
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                Guid.Parse("70000000-0000-0000-0000-000000000001"),
                Guid.Parse("80000000-0000-0000-0000-000000000001"),
                Guid.Parse("90000000-0000-0000-0000-000000000001")),
            "westeurope",
            Guid.Parse("90000000-0000-0000-0000-000000000002"),
            ElsaInstanceOperationAction.Recover);
    }

    private static ResolvedElsaApplicationPlan CreatePlan() => new(
        ResolvedElsaApplicationPlanSchema.CurrentVersion,
        new("distribution", "3.8", "3.8.0", "https://example.test/source", "commit", "https://example.test/release", "sha256:" + new string('a', 64)),
        new("combined", []),
        [],
        new([]),
        new([], []),
        new("private", "restricted", false, [], []),
        "isolated",
        new("stable", "production", "standard", "automatic", "explicit", "explicit"),
        [],
        []);

    private static ElsaInstanceProviderRecoveryEnvelope CreateEnvelope()
    {
        var digest = "sha256:" + new string('a', 64);
        return new(
            Guid.Parse("a0000000-0000-0000-0000-000000000001"),
            Guid.Parse("90000000-0000-0000-0000-000000000002"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            1,
            4,
            2,
            5,
            "instances/operations",
            "recovery-key",
            new string('b', 64),
            ElsaInstanceProviderRecoveryObservationReference.Create(
                Guid.Parse("b0000000-0000-0000-0000-000000000001"), digest),
            digest);
    }
}
