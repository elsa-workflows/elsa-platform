using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderOperationValidationTests
{
    [Fact]
    public void Normalizes_safe_identity_and_digests()
    {
        var request = ValidRequest() with
        {
            TargetKey = " Workload-A ",
            PlanFingerprint = new('A', 64),
            TemplateFingerprint = new('B', 64),
            ImageDigest = $"SHA256:{new string('C', 64)}"
        };

        var normalized = AzureProviderOperationValidation.Normalize(request);

        Assert.Equal("workload-a", normalized.TargetKey);
        Assert.Equal(new string('a', 64), normalized.PlanFingerprint);
        Assert.Equal("sha256:" + new string('c', 64), normalized.ImageDigest);
    }

    [Theory]
    [InlineData("sha256:bad")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")]
    public void Rejects_invalid_digests(string digest)
    {
        var errors = AzureProviderOperationValidation.Validate(ValidRequest() with { ImageDigest = digest });
        Assert.Contains("imageDigest.invalid", errors);
    }

    [Fact]
    public void Rejects_raw_or_unsafe_repository_values()
    {
        var errors = AzureProviderOperationValidation.Validate(ValidRequest() with
        {
            ImageRepository = "oci://user:secret@example.com/image\n"
        });

        Assert.Contains("imageRepository.mustBeRepository", errors);
        Assert.Contains("imageRepository.unsafe", errors);
    }

    [Fact]
    public void Accepts_safe_non_governed_repository_for_upstream_admission()
    {
        var request = ValidRequest() with { ImageRepository = "another.azurecr.io/runtime-combined" };
        Assert.DoesNotContain("imageRepository.mustBeRepository", AzureProviderOperationValidation.Validate(request));
    }

    [Theory]
    [InlineData("https://runtime.example.test/%2e%2e/admin")]
    [InlineData("https://runtime.example.test/path?token=value")]
    public void Rejects_ambiguous_or_secret_bearing_endpoints(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateEndpoint(endpoint));
    }

    [Fact]
    public void Rejects_endpoint_larger_than_the_persistence_contract()
    {
        var endpoint = "https://runtime.example.test/" + new string('a', 2048);
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateEndpoint(endpoint));
    }

    [Theory]
    [InlineData("oci://registry.example/manifest", true)]
    [InlineData("https://registry.example/evidence/signature", true)]
    [InlineData("oci://registry.example", false)]
    [InlineData("https://user:token@registry.example/evidence", false)]
    [InlineData("https://registry.example/evidence?token=value", false)]
    [InlineData("https://registry.example/../evidence", false)]
    public void Evidence_locators_are_absolute_non_root_and_credential_free(string locator, bool expected)
    {
        Assert.Equal(expected, AzureProviderOperationValidation.IsSafeImmutableLocator(locator));
    }

    [Fact]
    public void Evidence_locator_digest_must_be_present_and_match_embedded_digest()
    {
        var digest = "sha256:" + new string('a', 64);
        Assert.True(AzureProviderOperationValidation.IsSafeImmutableEvidenceReference("oci://registry.example/manifest", digest));
        Assert.True(AzureProviderOperationValidation.IsSafeImmutableEvidenceReference($"oci://registry.example/manifest@{digest}", digest));
        Assert.False(AzureProviderOperationValidation.IsSafeImmutableEvidenceReference("oci://registry.example/manifest", null));
        Assert.False(AzureProviderOperationValidation.IsSafeImmutableEvidenceReference($"oci://registry.example/manifest@{digest}", "sha256:" + new string('b', 64)));
        Assert.False(AzureProviderOperationValidation.IsSafeImmutableEvidenceReference("oci://registry.example/manifest@not-a-digest", digest));
    }

    [Fact]
    public void Hash_and_identity_are_stable_for_case_normalization()
    {
        var first = ValidRequest();
        var second = first with
        {
            TargetKey = first.TargetKey.ToUpperInvariant(),
            PlanFingerprint = first.PlanFingerprint.ToUpperInvariant(),
            ImageDigest = first.ImageDigest.ToUpperInvariant()
        };

        Assert.Equal(AzureProviderOperationValidation.ComputeRequestHash(first), AzureProviderOperationValidation.ComputeRequestHash(second));
        Assert.Equal(AzureProviderOperationValidation.ComputeOperationIdentity(first), AzureProviderOperationValidation.ComputeOperationIdentity(second));
    }

    private static AzureProviderOperationRequest ValidRequest() => new(
        Guid.NewGuid(), "workload-a", AzureProviderOperationAction.Reconcile, "request-1",
        new('a', 64), new('b', 64), "3.8.0", "3.8", "combined", "Dedicated", "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined", "sha256:" + new string('c', 64));
}
