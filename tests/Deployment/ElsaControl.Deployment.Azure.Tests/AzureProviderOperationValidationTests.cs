using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderOperationValidationTests
{
    [Theory]
    [InlineData("foundation", 513)]
    [InlineData("workloadDeployment", 513)]
    [InlineData("workloadResource", 1025)]
    [InlineData("acrPullDeployment", 513)]
    [InlineData("resourceGroup", 91)]
    public void Rejects_each_reference_longer_than_its_persistence_contract(string referenceKind, int length)
    {
        var value = new string('a', length);
        var references = referenceKind switch
        {
            "foundation" => new AzureProviderResourceReferences(FoundationDeploymentId: value),
            "workloadDeployment" => new AzureProviderResourceReferences(WorkloadDeploymentId: value),
            "workloadResource" => new AzureProviderResourceReferences(WorkloadResourceId: value),
            "acrPullDeployment" => new AzureProviderResourceReferences(AcrPullDeploymentId: value),
            "resourceGroup" => new AzureProviderResourceReferences(ResourceGroupName: value),
            _ => throw new ArgumentOutOfRangeException(nameof(referenceKind))
        };

        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(references));
    }

    [Fact]
    public void Accepts_the_complete_safe_restartable_resource_contract()
    {
        var references = new AzureProviderResourceReferences(
            ResourceGroupName: "proof-rg",
            FoundationDeploymentId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.Resources/deployments/foundation",
            WorkloadDeploymentId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.Resources/deployments/workload",
            WorkloadResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.App/containerApps/app",
            WorkloadRevisionName: "app--candidate",
            StableTrafficRevisionName: "app--stable",
            WorkloadIdentityResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/identity",
            WorkloadIdentityClientId: "11111111-1111-1111-1111-111111111111",
            WorkloadIdentityPrincipalId: "22222222-2222-2222-2222-222222222222",
            KeyVaultResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.KeyVault/vaults/vault",
            KeyVaultUri: "https://vault.vault.azure.net/",
            SqlServerResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.Sql/servers/sql",
            SqlServerFqdn: "sql.database.windows.net",
            ContainerAppsEnvironmentResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.App/managedEnvironments/env",
            RegistryResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/registry-rg/providers/Microsoft.ContainerRegistry/registries/registry",
            AcrPullDeploymentId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/registry-rg/providers/Microsoft.Resources/deployments/acr-pull",
            AcrPullRoleAssignmentId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/registry-rg/providers/Microsoft.ContainerRegistry/registries/registry/providers/Microsoft.Authorization/roleAssignments/33333333-3333-3333-3333-333333333333");

        AzureProviderOperationValidation.ValidateReferences(references);
    }

    [Theory]
    [InlineData("https://user:secret@vault.vault.azure.net/")]
    [InlineData("https://vault.vault.azure.net/?token=secret")]
    [InlineData("http://vault.vault.azure.net/")]
    [InlineData("https://vault.vault.azure.net/secrets/value")]
    public void Rejects_unsafe_key_vault_origins(string value)
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(
            new AzureProviderResourceReferences(KeyVaultUri: value)));
    }

    [Theory]
    [InlineData("NOT-A-GUID")]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    public void Rejects_noncanonical_identity_ids(string value)
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(
            new AzureProviderResourceReferences(WorkloadIdentityClientId: value)));
    }

    [Fact]
    public void Rejects_resource_ids_outside_the_owned_resource_group()
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(
            new AzureProviderResourceReferences(
                ResourceGroupName: "proof-rg",
                WorkloadResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/other-rg/providers/Microsoft.App/containerApps/app")));
    }

    [Fact]
    public void Rejects_registry_role_assignments_outside_the_exact_registry_resource()
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(
            new AzureProviderResourceReferences(
                RegistryResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/registry-rg/providers/Microsoft.ContainerRegistry/registries/registry",
                AcrPullRoleAssignmentId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/registry-rg/providers/Microsoft.Authorization/roleAssignments/33333333-3333-3333-3333-333333333333")));
    }

    [Fact]
    public void Rejects_key_vault_and_sql_endpoints_that_do_not_match_their_resource_ids()
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(
            new AzureProviderResourceReferences(
                ResourceGroupName: "proof-rg",
                KeyVaultResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.KeyVault/vaults/expected",
                KeyVaultUri: "https://different.vault.azure.net/")));
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateReferences(
            new AzureProviderResourceReferences(
                ResourceGroupName: "proof-rg",
                SqlServerResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/proof-rg/providers/Microsoft.Sql/servers/expected",
                SqlServerFqdn: "different.database.windows.net")));
    }

    [Theory]
    [InlineData("https://runtime.example.test", "https://runtime.example.test")]
    [InlineData("HTTPS://Runtime.Example.Test:443/", "https://runtime.example.test")]
    [InlineData("https://runtime.example.test:8443/", "https://runtime.example.test:8443")]
    public void Accepts_and_canonicalizes_absolute_https_origins_with_empty_or_root_path(
        string endpoint,
        string expected)
    {
        AzureProviderOperationValidation.ValidateEndpoint(endpoint);
        Assert.Equal(expected, AzureProviderOperationValidation.NormalizeEndpoint(endpoint));
    }

    [Theory]
    [InlineData("https://runtime.example.test/api")]
    [InlineData("https:///")]
    [InlineData("https://user:secret@runtime.example.test/")]
    [InlineData("https://runtime.example.test/?token=secret")]
    [InlineData("https://runtime.example.test/#fragment")]
    [InlineData("http://runtime.example.test/")]
    [InlineData("https://runtime.example.test/\r\n")]
    public void Rejects_non_origin_provider_endpoints(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => AzureProviderOperationValidation.ValidateEndpoint(endpoint));
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

    [Theory]
    [InlineData(null, "3.8.0", "packageVersions.incomplete")]
    [InlineData("3.8.0", null, "packageVersions.incomplete")]
    [InlineData("3.8 stable", "3.8.0", "sqlWorkflowPackageVersion.invalid")]
    [InlineData("3.8.0", "not-a-version!", "sqlQuartzPackageVersion.invalid")]
    public void Rejects_incomplete_or_non_NuGet_package_metadata(
        string? workflowVersion,
        string? quartzVersion,
        string expectedError)
    {
        var errors = AzureProviderOperationValidation.Validate(ValidRequest() with
        {
            SqlWorkflowPackageVersion = workflowVersion,
            SqlQuartzPackageVersion = quartzVersion
        });

        Assert.Contains(expectedError, errors);
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
    [InlineData("secret://source.vault.azure.net/secrets/sql-connection/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [InlineData("https://source.vault.azure.net/secrets/sql-connection/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
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
    public void Secret_reference_collection_rejects_names_that_collide_after_Azure_mapping()
    {
        var references = new Dictionary<string, string>
        {
            ["database:password"] = "secret://vault/database-password",
            ["database_password"] = "secret://vault/other-database-password"
        };

        Assert.False(AzureProviderOperationValidation.IsSafeSecretReferences(references));
        Assert.Contains(
            "secretReferences.nameCollision",
            AzureProviderOperationValidation.Validate(ValidRequest() with { SecretReferences = references }));
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
    public void Rejects_whitespace_only_checkpoint_messages()
    {
        var checkpoint = new AzureProviderCheckpoint(
            AzureProviderOperationPhase.Planned, "operation.planned", "   ", new(), null,
            AzureProviderHealth.Unknown, []);

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

    [Fact]
    public void Provider_scope_fingerprint_is_normalized_and_bound_to_request_and_operation_identity()
    {
        var first = ValidRequest() with { ProviderScopeFingerprint = new string('A', 64) };
        var same = first with { ProviderScopeFingerprint = new string('a', 64) };
        var different = first with { ProviderScopeFingerprint = new string('b', 64) };

        Assert.Equal(new string('a', 64), AzureProviderOperationValidation.Normalize(first).ProviderScopeFingerprint);
        Assert.Equal(AzureProviderOperationValidation.ComputeRequestHash(first), AzureProviderOperationValidation.ComputeRequestHash(same));
        Assert.Equal(AzureProviderOperationValidation.ComputeOperationIdentity(first), AzureProviderOperationValidation.ComputeOperationIdentity(same));
        Assert.NotEqual(AzureProviderOperationValidation.ComputeRequestHash(first), AzureProviderOperationValidation.ComputeRequestHash(different));
        Assert.NotEqual(AzureProviderOperationValidation.ComputeOperationIdentity(first), AzureProviderOperationValidation.ComputeOperationIdentity(different));
    }

    private static AzureProviderOperationRequest ValidRequest() => new(
        Guid.NewGuid(), "workload-a", AzureProviderOperationAction.Reconcile, "request-1",
        new('a', 64), new('b', 64), "3.8.0", "3.8", "combined", "Dedicated", "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined", "sha256:" + new string('c', 64));
}
