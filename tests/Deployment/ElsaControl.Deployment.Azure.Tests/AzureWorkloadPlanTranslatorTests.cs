using ElsaControl.Deployment.Azure;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureWorkloadPlanTranslatorTests
{
    private const string ManifestDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ImageDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Rejects_missing_inputs_without_throwing()
    {
        var missingPlan = AzureWorkloadPlanTranslator.Translate(null, new("workload-a", "westeurope"));
        var missingTarget = AzureWorkloadPlanTranslator.Translate(CreatePlan(), null);

        Assert.Contains(missingPlan.Findings, x => x.Code == "plan.required");
        Assert.Contains(missingTarget.Findings, x => x.Code == "azure.target.required");
        Assert.Null(missingPlan.Plan);
        Assert.Null(missingTarget.Plan);
    }

    [Fact]
    public void Translates_supported_plan_to_deterministic_safe_intent()
    {
        var plan = CreatePlan();
        var target = new AzureWorkloadTarget("workload-a", "westeurope");

        var first = AzureWorkloadPlanTranslator.Translate(plan, target);
        var second = AzureWorkloadPlanTranslator.Translate(plan with
        {
            Evidence = plan.Evidence.Reverse().ToArray(),
            ProviderCapabilities = plan.ProviderCapabilities.Reverse().ToArray()
        }, target);

        Assert.True(first.IsAccepted);
        Assert.Empty(first.Findings);
        Assert.NotNull(first.Plan);
        Assert.Equal(first.Plan.Fingerprint, second.Plan?.Fingerprint);
        Assert.Equal("workload-a", first.Plan.WorkloadName);
        Assert.Equal("westeurope", first.Plan.Location);
        Assert.Equal("3.8.0-preview.5413", first.Plan.ElsaVersion);
        Assert.Equal("combined", first.Plan.Topology);
        Assert.Equal("Dedicated", first.Plan.Isolation);
        Assert.Equal("valenceruntimeimages.azurecr.io/runtime-combined", first.Plan.ImageRepository);
        Assert.Equal(new string('a', 64), first.Plan.ImageDigest);
        Assert.Equal("oci://release-manifest", first.Plan.ReleaseManifestReference);
        Assert.Equal(ManifestDigest, first.Plan.ReleaseManifestDigest);
        Assert.Equal("oci://release-manifest.signature", first.Plan.ReleaseManifestSignatureReference);
        Assert.Equal(ImageDigest, first.Plan.ReleaseManifestSignatureDigest);
        Assert.Equal("secret://database", first.Plan.SecretReferences["Database:ConnectionString"]);
        Assert.Equal("database:connectionstring", Assert.Single(first.Plan.SecretReferences).Key);
        Assert.Matches("^[a-f0-9]{64}$", first.Plan.Fingerprint);
    }

    [Fact]
    public void Surfaces_resolved_plan_validation_and_does_not_translate()
    {
        var plan = CreatePlan() with
        {
            Topology = CreatePlan().Topology with
            {
                Components = [CreatePlan().Topology.Components[0] with
                {
                    Image = CreatePlan().Topology.Components[0].Image with
                    {
                        Reference = "valenceruntimeimages.azurecr.io/runtime-combined:latest"
                    }
                }]
            },
            Configuration = new([new("Database:ConnectionString", "string", true, true, false, null, Json("\"Server=secret\""), null, null)])
        };

        var result = AzureWorkloadPlanTranslator.Translate(plan, new("workload-a", "westeurope"));

        Assert.False(result.IsAccepted);
        Assert.Null(result.Plan);
        Assert.Contains(result.Findings, x => x.Code == "image.reference.immutableRequired");
        Assert.Contains(result.Findings, x => x.Code == "configuration.secretValue.forbidden");
        Assert.DoesNotContain(result.Findings, x => x.Message.Contains("Server=secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_null_collections_without_throwing()
    {
        var result = AzureWorkloadPlanTranslator.Translate(
            CreatePlan() with { Packages = null! },
            new("workload-a", "westeurope"));

        Assert.False(result.IsAccepted);
        Assert.Null(result.Plan);
        Assert.Contains(result.Findings, x => x.Code == "azure.plan.normalization.invalid");
    }

    [Fact]
    public void Does_not_layer_provider_findings_over_base_schema_failures()
    {
        var result = AzureWorkloadPlanTranslator.Translate(
            CreatePlan() with { Topology = null! },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "topology.required");
        Assert.DoesNotContain(result.Findings, x => x.Code.StartsWith("azure.", StringComparison.Ordinal));
    }

    [Fact]
    public void Configuration_key_casing_does_not_change_fingerprint()
    {
        var plan = CreatePlan();
        var changedCasing = plan with
        {
            Configuration = new([plan.Configuration.Entries[0] with { Key = "database:connectionstring" }])
        };

        var first = AzureWorkloadPlanTranslator.Translate(plan, new("workload-a", "westeurope"));
        var second = AzureWorkloadPlanTranslator.Translate(changedCasing, new("workload-a", "westeurope"));

        Assert.Equal(first.Plan?.Fingerprint, second.Plan?.Fingerprint);
    }

    [Fact]
    public void Rejects_null_image_repository_without_throwing()
    {
        var plan = CreatePlan();
        var component = plan.Topology.Components[0];
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Topology = plan.Topology with
                {
                    Components = [component with { Image = component.Image with { Repository = null! } }]
                }
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "image.repository.required");
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData("server-studio", "Dedicated", "westeurope", "azure.topology.unsupported")]
    [InlineData("combined", "Shared", "westeurope", "azure.isolation.unsupported")]
    [InlineData("combined", "Dedicated", "northeurope", "azure.location.unsupported")]
    public void Rejects_unsupported_initial_provider_profile(
        string topology,
        string isolation,
        string location,
        string expectedCode)
    {
        var plan = CreatePlan() with
        {
            Topology = CreatePlan().Topology with { Id = topology },
            Isolation = isolation
        };

        var result = AzureWorkloadPlanTranslator.Translate(plan, new("workload-a", location));

        Assert.False(result.IsAccepted);
        Assert.Contains(result.Findings, x => x.Code == expectedCode);
    }

    [Fact]
    public void Rejects_private_networking_and_unknown_required_capabilities()
    {
        var plan = CreatePlan();
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Network = plan.Network with { Egress = "restricted", RequiresPrivateConnectivity = true },
                ProviderCapabilities = [.. plan.ProviderCapabilities, new("gpu-runtime", "Needs GPU compute.", true, ["gpu"])]
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.network.unsupported");
        Assert.Contains(result.Findings, x => x.Code == "azure.providerCapability.unsupported");
    }

    [Fact]
    public void Rejects_missing_or_inconsistent_release_manifest_evidence()
    {
        var missing = AzureWorkloadPlanTranslator.Translate(
            CreatePlan() with { Evidence = [] },
            new("workload-a", "westeurope"));
        var mismatch = AzureWorkloadPlanTranslator.Translate(
            CreatePlan() with { Evidence = [new(ReleaseManifestEvidenceKinds.Manifest, "oci://other", ManifestDigest, "Verified release manifest")] },
            new("workload-a", "westeurope"));

        Assert.Contains(missing.Findings, x => x.Code == "azure.releaseManifestEvidence.required");
        Assert.Contains(mismatch.Findings, x => x.Code == "azure.releaseManifestEvidence.mismatch");
    }

    [Fact]
    public void Rejects_missing_or_unsafe_signature_evidence()
    {
        var missing = CreatePlan() with
        {
            Evidence = CreatePlan().Evidence.Where(x => x.Kind != ReleaseManifestEvidenceKinds.Signature).ToArray()
        };
        var unsafeEvidence = CreatePlan().Evidence
            .Select(x => x.Kind == ReleaseManifestEvidenceKinds.Signature ? x with { Reference = "https://user:token@example.com/signature?token=secret" } : x)
            .ToArray();

        var missingResult = AzureWorkloadPlanTranslator.Translate(missing, new("workload-a", "westeurope"));
        var unsafeResult = AzureWorkloadPlanTranslator.Translate(CreatePlan() with { Evidence = unsafeEvidence }, new("workload-a", "westeurope"));

        Assert.Contains(missingResult.Findings, x => x.Code == "azure.releaseManifestSignatureEvidence.required");
        Assert.Contains(unsafeResult.Findings, x => x.Code == "azure.releaseManifestSignatureEvidence.invalid");
        Assert.DoesNotContain(unsafeResult.Findings, x => x.Message.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_image_reference_digest_mismatch()
    {
        var component = CreatePlan().Topology.Components[0];
        var result = AzureWorkloadPlanTranslator.Translate(
            CreatePlan() with
            {
                Topology = new("combined", [component with { Image = component.Image with { Digest = ManifestDigest } }])
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "image.referenceDigest.mismatch");
    }

    [Fact]
    public void Rejects_image_repository_that_disagrees_with_immutable_reference()
    {
        var plan = CreatePlan();
        var component = plan.Topology.Components[0];
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Topology = plan.Topology with
                {
                    Components = [component with
                    {
                        Image = component.Image with { Repository = "valenceruntimeimages.azurecr.io/other" }
                    }]
                }
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.imageReference.repositoryMismatch");
    }

    [Fact]
    public void Rejects_images_outside_initial_paid_registry_authority()
    {
        var plan = CreatePlan();
        var component = plan.Topology.Components[0];
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Topology = plan.Topology with
                {
                    Components = [component with
                    {
                        Image = component.Image with
                        {
                            RegistryClass = "community",
                            Repository = "ghcr.io/example/runtime",
                            Reference = $"ghcr.io/example/runtime@{ImageDigest}"
                        }
                    }]
                }
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.imageRegistry.unsupported");
    }

    [Fact]
    public void Rejects_public_endpoint_without_Https_and_Tls()
    {
        var plan = CreatePlan();
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Network = plan.Network with
                {
                    Endpoints = [plan.Network.Endpoints[0] with { Protocol = "http", RequiresTls = false }]
                }
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.network.tlsRequired");
    }

    [Fact]
    public void Rejects_unsafe_image_repository_and_manifest_locator_without_echoing_them()
    {
        var plan = CreatePlan();
        var component = plan.Topology.Components[0];
        const string unsafeRepository = "user:secret@registry.example/runtime";
        const string unsafeManifest = "https://user:secret@example.com/manifest?token=secret";
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Release = plan.Release with { ReleaseManifestReference = unsafeManifest },
                Topology = plan.Topology with
                {
                    Components = [component with
                    {
                        Image = component.Image with
                        {
                            Repository = unsafeRepository,
                            Reference = $"{unsafeRepository}@{ImageDigest}"
                        }
                    }]
                },
                Evidence = plan.Evidence.Select(x => x.Kind == ReleaseManifestEvidenceKinds.Manifest ? x with { Reference = unsafeManifest } : x).ToArray()
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.imageRepository.invalid");
        Assert.Contains(result.Findings, x => x.Code == "azure.releaseManifestEvidence.mismatch");
        Assert.DoesNotContain(result.Findings, x => x.Message.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("valenceruntimeimages.azurecr.io/../runtime")]
    [InlineData("valenceruntimeimages.azurecr.io/-runtime")]
    [InlineData("valenceruntimeimages.azurecr.io/runtime/")]
    [InlineData("valenceruntimeimages.azurecr.io/runtime//child")]
    public void Rejects_non_Oci_repository_paths(string repository)
    {
        var plan = CreatePlan();
        var component = plan.Topology.Components[0];
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Topology = plan.Topology with
                {
                    Components = [component with
                    {
                        Image = component.Image with
                        {
                            Repository = repository,
                            Reference = $"{repository}@{ImageDigest}"
                        }
                    }]
                }
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.imageRepository.invalid");
    }

    [Fact]
    public void Rejects_repository_names_over_the_Oci_length_limit()
    {
        var plan = CreatePlan();
        var component = plan.Topology.Components[0];
        var repository = $"valenceruntimeimages.azurecr.io/{new string('a', 256)}";
        var result = AzureWorkloadPlanTranslator.Translate(
            plan with
            {
                Topology = plan.Topology with
                {
                    Components = [component with
                    {
                        Image = component.Image with { Repository = repository, Reference = $"{repository}@{ImageDigest}" }
                    }]
                }
            },
            new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.imageRepository.invalid");
    }

    [Theory]
    [InlineData("--bad")]
    [InlineData("bad-")]
    [InlineData("this-name-is-far-too-long")]
    public void Rejects_workload_names_that_cannot_be_Bicep_inputs(string name)
    {
        var result = AzureWorkloadPlanTranslator.Translate(CreatePlan(), new(name, "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.workloadName.invalid");
    }

    [Fact]
    public void Later_Elsa_version_remains_data_and_is_rejected_as_provider_policy()
    {
        var plan = CreatePlan("5.0", "5.0.0") with
        {
            Packages = [CreatePlan().Packages[0] with { Version = "5.0.0" }]
        };

        var result = AzureWorkloadPlanTranslator.Translate(plan, new("workload-a", "westeurope"));

        Assert.Contains(result.Findings, x => x.Code == "azure.releaseLine.unsupported");
        Assert.Equal("release.releaseLine", result.Findings.Single(x => x.Code == "azure.releaseLine.unsupported").Scope);
    }

    private static ResolvedElsaApplicationPlan CreatePlan(string releaseLine = "3.8", string version = "3.8.0-preview.5413")
    {
        var component = new ResolvedElsaComponent(
            "runtime",
            ["studio", "server"],
            new("paid", "valenceruntimeimages.azurecr.io/runtime-combined", $"valenceruntimeimages.azurecr.io/runtime-combined@{ImageDigest}", ImageDigest),
            ["elsa.studio", "elsa.server"],
            [new("studio", "https", 8080, "public", true, "/"), new("api", "https", 8080, "public", true, "/elsa/api")],
            ["workflow.runtime", "workflow.studio"]);

        return new(
            ResolvedElsaApplicationPlanSchema.CurrentVersion,
            new("valence-runtime", releaseLine, version, "https://github.com/valence-works/elsa-production-image", "1aeee8df455b21cf3bf3d2b26dfbd512d76da27b", "oci://release-manifest", ManifestDigest),
            new("combined", [component]),
            [new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Elsa.Core", version, ImageDigest, ["elsa.server"], [new("runtime", "Elsa.Runtime", ["elsa.server"], ["workflow.runtime"])])],
            new([new("Database:ConnectionString", "string", true, true, false, "ELSA_DATABASE_CONNECTION", null, "secret://database", null)]),
            new([new("runtime", 1, 1, 500, 1024)], [new("elsa-data", "relational", "persistent", "exclusive", 10)]),
            new("public", "unrestricted", false, [], [new("runtime", "api", "https", 443, "public", true, "/elsa/api")]),
            "Dedicated",
            new("preview", "Preview", "internal", "automatic-within-minor", "explicit-approval", "explicit-migration"),
            [new("managed-runtime", "Run the resolved runtime components.", true, ["container", "persistent-storage"])],
            [
                new(ReleaseManifestEvidenceKinds.Manifest, "oci://release-manifest", ManifestDigest, "Verified release manifest"),
                new(ReleaseManifestEvidenceKinds.Signature, "oci://release-manifest.signature", ImageDigest, "Verified release manifest signature"),
                new("catalog", "catalog://snapshot", null, "Resolved catalog snapshot")
            ]);
    }

    private static System.Text.Json.JsonElement Json(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
