using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class ReleaseManifestEvidenceContractTests
{
    [Fact]
    public void IsSafe_returns_false_when_reference_is_null()
    {
        var safe = ReleaseManifestEvidenceContract.IsSafe(
            ReleaseManifestEvidenceKinds.Manifest,
            reference: null,
            $"sha256:{new string('a', 64)}",
            ReleaseManifestEvidenceContract.DescriptionFor(ReleaseManifestEvidenceKinds.Manifest));

        Assert.False(safe);
    }

    [Fact]
    public void DescriptionFor_returns_generic_description_for_unknown_kind()
    {
        var description = ReleaseManifestEvidenceContract.DescriptionFor("unknown-evidence-kind");

        Assert.Equal(ReleaseManifestEvidenceContract.GenericDescription, description);
    }
}
