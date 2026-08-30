using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderOperationValidationTests
{
    [Theory]
    [InlineData("foundation", 513)]
    [InlineData("workloadDeployment", 513)]
    [InlineData("workloadResource", 1025)]
    [InlineData("resourceGroup", 91)]
    public void Rejects_each_reference_longer_than_its_persistence_contract(string referenceKind, int length)
    {
        var value = new string('a', length);
        var references = referenceKind switch
        {
            "foundation" => new AzureProviderResourceReferences(FoundationDeploymentId: value),
            "workloadDeployment" => new AzureProviderResourceReferences(WorkloadDeploymentId: value),
            "workloadResource" => new AzureProviderResourceReferences(WorkloadResourceId: value),
            "resourceGroup" => new AzureProviderResourceReferences(ResourceGroupName: value),
            _ => throw new ArgumentOutOfRangeException(nameof(referenceKind))
        };

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

    [Theory]
    [InlineData("secret://database", true)]
    [InlineData("secret://vault/database", true)]
    [InlineData("secret://vault/database/connection", true)]
    [InlineData("secret://vault/../database", false)]
    [InlineData("secret://vault/./database", false)]
    [InlineData("secret://vault//database", false)]
    [InlineData("secret://vault/database%2Fconnection", false)]
    [InlineData("secret://vault/database%2E%2E", false)]
    [InlineData("secret://vault/database\\connection", false)]
    [InlineData("secret://user:password@vault/database", false)]
    [InlineData("secret://vault/database?version=1", false)]
    [InlineData("secret://vault/database#fragment", false)]
    public void Secret_locators_are_opaque_and_never_filesystem_paths(string locator, bool expected)
    {
        Assert.Equal(expected, AzureProviderOperationValidation.IsSafeSecretReference(locator));
    }

    [Fact]
    public void Secret_reference_collection_rejects_null_and_oversized_collections()
    {
        Assert.False(AzureProviderOperationValidation.IsSafeSecretReferences(null));
        var references = Enumerable.Range(0, 65)
            .ToDictionary(index => $"secret-{index}", _ => "secret://vault/value");
        Assert.False(AzureProviderOperationValidation.IsSafeSecretReferences(references));
    }

    [Fact]
    public void Rejects_newline_terminated_codes_and_worker_ids()
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateCode("operation.succeeded\n"));
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateWorkerId("worker-1\n"));
    }

    [Fact]
    public void Rejects_undefined_checkpoint_health()
    {
        var checkpoint = new AzureProviderCheckpoint(
            AzureProviderOperationPhase.Planned, "operation.planned", "Planned.", new(), null,
            (AzureProviderHealth)999, []);

        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateCheckpoint(checkpoint));
    }

    [Fact]
    public void Hash_and_identity_are_stable_for_case_normalization()
    {
        var first = ValidRequest();
        var second = first with
        {
            TargetKey = first.TargetKey.ToUpperInvariant(),
            PlanFingerprint = first.PlanFingerprint.ToUpperInvariant(),
            ImageDigest = first.ImageDigest.ToUpperInvariant(),
            ImageRepository = first.ImageRepository.ToUpperInvariant(),
            Isolation = first.Isolation.ToUpperInvariant()
        };

        Assert.Equal(AzureProviderOperationValidation.ComputeRequestHash(first), AzureProviderOperationValidation.ComputeRequestHash(second));
        Assert.Equal(AzureProviderOperationValidation.ComputeOperationIdentity(first), AzureProviderOperationValidation.ComputeOperationIdentity(second));
    }

    private static AzureProviderOperationRequest ValidRequest() => new(
        Guid.NewGuid(), "workload-a", AzureProviderOperationAction.Reconcile, "request-1",
        new('a', 64), new('b', 64), "3.8.0", "3.8", "combined", "Dedicated", "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined", "sha256:" + new string('c', 64));
}
