using ElsaControl.Deployment.Core.Instances;
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
}
