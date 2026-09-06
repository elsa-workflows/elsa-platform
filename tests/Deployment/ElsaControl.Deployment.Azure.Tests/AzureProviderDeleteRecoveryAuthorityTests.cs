using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderDeleteRecoveryAuthorityTests
{
    [Fact]
    public void Authority_round_trips_only_the_canonical_safe_snapshot()
    {
        var authority = new AzureProviderDeleteRecoveryAuthority(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            LifecycleAttemptNumber: 2,
            InstanceVersion: 9,
            ProviderAttemptNumber: 1,
            ProviderVersion: 7,
            ProviderCheckpointSequence: 4,
            new string('a', 64),
            new string('b', 64),
            "e0123456789abcde",
            new string('c', 64),
            new string('d', 64),
            new string('e', 64));

        var serialized = authority.Serialize();

        Assert.True(AzureProviderDeleteRecoveryAuthority.TryParse(serialized, out var parsed));
        Assert.Equal(authority, parsed);
        Assert.False(AzureProviderDeleteRecoveryAuthority.TryParse(serialized + "|unexpected", out _));
        Assert.False(AzureProviderDeleteRecoveryAuthority.TryParse(serialized.Replace("|e012", "|e 012", StringComparison.Ordinal), out _));
    }

    [Theory]
    [InlineData("v0")]
    [InlineData("v1|||||||||||||")]
    [InlineData("v1|not-a-guid")]
    public void Authority_parser_rejects_legacy_or_malformed_values(string value)
    {
        Assert.False(AzureProviderDeleteRecoveryAuthority.TryParse(value, out _));
    }
}
