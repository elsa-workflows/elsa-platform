using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderOperationValidationTests
{
    [Fact]
    public void Rejects_deployment_references_longer_than_the_persistence_contract()
    {
        var references = new AzureProviderResourceReferences(
            FoundationDeploymentId: new string('a', 513),
            WorkloadDeploymentId: new string('b', 513),
            WorkloadResourceId: new string('c', 1024));

        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(references));
    }

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
