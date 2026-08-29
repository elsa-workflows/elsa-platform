using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.PackageCatalog.Abstractions.Catalog;
using ElsaControl.PackageCatalog.Abstractions.Compatibility;
using ElsaControl.RuntimeBuilder.Abstractions;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using ElsaControl.RuntimeBuilder.Core.Plans;
using ElsaControl.RuntimeBuilder.Core.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class ElsaInstancePlanResolverTests
{
    [Fact]
    public async Task Resolves_arbitrary_release_line_to_immutable_plan_and_current_release()
    {
        var sourceId = Guid.NewGuid();
        var version = new PublicPackageVersionProjection(
            "Elsa.Email",
            "2.0.0",
            new(sourceId, "source", "https://packages.example.test/index.json"),
            "1.0",
            ["elsa.server"],
            DateTimeOffset.UtcNow,
            [new(
                "email", "Elsa.Email", "2.0.0", new(sourceId, "source", "https://packages.example.test/index.json"),
                "Elsa.Email.Feature", "Email", null, null, Categories: [], RequiredCapabilities: [], RuntimeKinds: ["elsa.server"],
                Dependencies: [], Conflicts: [], Infrastructure: [], Advanced: false, Experimental: false, ExtensionsJson: "{}", Settings: [])],
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var intent = new ElsaInstanceIntent(
            new("future-runtime", "5.0", channel: "preview"),
            new("server-studio", featurePresetId: "starter", packagePolicy: "approved"),
            new("managed", "westeurope", "dedicated", "standard-small", "public", "managed"));
        var request = new ElsaInstancePlanResolutionRequest(
            intent,
            new(new("future-runtime", null, null, null), [new(sourceId, "Elsa.Email", "2.0.0", ["email"], null)], [], [], null),
            AdmittedManifest("5.0", "5.0.0-preview.1", "server-studio"),
            "plan_01J5FUTURE",
            "https://control.example.test/api/workspaces/00000000-0000-0000-0000-000000000001/instances/00000000-0000-0000-0000-000000000002/resolved-plans/plan_01J5FUTURE");

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([version]), new FakeCompatibility()).ResolveAsync(request);

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(x => x.Code + ":" + x.Message)));
        Assert.NotNull(result.Plan);
        Assert.NotNull(result.Reference);
        Assert.NotNull(result.CurrentResolvedRelease);
        Assert.Equal("5.0", result.CurrentResolvedRelease!.ReleaseLine);
        Assert.Equal("5.0.0-preview.1", result.CurrentResolvedRelease.Version);
        Assert.Equal("sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", result.CurrentResolvedRelease.ManifestDigest);
        Assert.Equal(result.Reference, result.CurrentResolvedRelease.PlanReference);
        Assert.Equal(result.Plan!.Release.ReleaseManifestDigest, result.CurrentResolvedRelease.ManifestDigest);
        Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Plan.Packages[0].ManifestDigest);
        Assert.Equal("sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", Assert.Single(result.CurrentResolvedRelease.ComponentDigests).Digest);
        Assert.Equal(result.Reference.ContentHash, ComputeHash(result.Plan));
    }

    [Fact]
    public async Task Resolves_actual_admission_result_after_signature_identity_is_sanitized()
    {
        var baseline = CreateRequest();
        var admission = await AdmitManifest(baseline.ReleaseManifest.Manifest!);

        Assert.True(admission.Accepted, string.Join("; ", admission.Findings.Select(x => x.Code + ":" + x.Message)));
        Assert.Empty(admission.Manifest!.Topologies[0].SupplyChain!.Signatures);

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility())
            .ResolveAsync(baseline with { ReleaseManifest = admission });

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(x => x.Code)));
        Assert.Contains(result.Plan!.Evidence, evidence => evidence.Kind == ReleaseManifestEvidenceKinds.Signature);
    }

    [Theory]
    [InlineData("3.8", "3.8.0-preview.5413")]
    [InlineData("3.9", "3.9.0-preview.1")]
    [InlineData("3.10", "3.10.0-preview.1")]
    [InlineData("4.0", "4.0.0-preview.1")]
    [InlineData("4.1", "4.1.0-preview.1")]
    [InlineData("5.0", "5.0.0-preview.1")]
    public async Task Resolves_each_supported_release_line_without_major_specific_branches(string releaseLine, string version)
    {
        var baseline = CreateRequest();
        var request = baseline with
        {
            InstanceIntent = baseline.InstanceIntent with
            {
                Release = new("future-runtime", releaseLine, version, "preview")
            },
            ReleaseManifest = AdmittedManifest(releaseLine, version, "server-studio")
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(x => x.Code + ":" + x.Message)));
        Assert.Equal(releaseLine, result.Plan!.Release.ReleaseLine);
        Assert.Equal(version, result.Plan.Release.Version);
        Assert.Equal(releaseLine, result.CurrentResolvedRelease!.ReleaseLine);
        Assert.Equal(version, result.CurrentResolvedRelease.Version);
    }

    [Fact]
    public async Task Rejects_legacy_provider_and_secret_surfaces_instead_of_ignoring_them()
    {
        var request = CreateRequest() with
        {
            BuilderIntent = new(
                new("future-runtime", "latest", 8080, new Dictionary<string, string> { ["Password"] = "secret-value" }),
                [],
                [new(Guid.NewGuid(), "private", "https://user:token@example.test/feed", "nuget")],
                [new("database", "azure-sql", "managed", new Dictionary<string, System.Text.Json.JsonElement> { ["server"] = System.Text.Json.JsonSerializer.SerializeToElement("secret") })],
                new(true, "/private/customer/packages"),
                "azure")
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "builder.image.tag.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.image.environment.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.image.hostPort.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.packageSources.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.infrastructure.provider.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.infrastructure.settings.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.localPackages.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "builder.target.unsupported");
        Assert.DoesNotContain(result.Findings, finding => finding.Message.Contains("secret-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_ungoverned_application_selection_values()
    {
        var request = CreateRequest() with
        {
            InstanceIntent = CreateRequest().InstanceIntent with
            {
                Application = new ElsaApplicationIntent("server-studio", "customer-preset", packagePolicy: "customer-policy", configurationShapeRevisionId: "shape-v2")
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "application.featurePreset.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "application.packagePolicy.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "application.configurationShape.unsupported");
    }

    [Fact]
    public async Task Projects_typed_feature_overrides_into_canonical_configuration_shape()
    {
        var request = CreateRequest() with
        {
            InstanceIntent = CreateRequest().InstanceIntent with
            {
                Application = new("server-studio", "starter", [new("replicas", ElsaFeatureOverride.FromNumber(3))], "approved")
            }
        };

        var resolver = new ElsaInstancePlanResolver(
            new FakeCatalog([]),
            new FakeCompatibility(),
            new ElsaInstancePlanResolutionOptions(
                FeatureOverrideDefinitions: new Dictionary<string, ElsaFeatureOverrideKind>
                {
                    ["replicas"] = ElsaFeatureOverrideKind.Number
                }));
        var result = await resolver.ResolveAsync(request);

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(x => x.Code)));
        var overrideEntry = Assert.Single(result.Plan!.Configuration.Entries, entry => entry.Key == "featureOverride.replicas");
        Assert.Equal("number", overrideEntry.JsonType);
        Assert.Equal(3, overrideEntry.Value!.Value.GetInt32());
    }

    [Fact]
    public async Task Rejects_unknown_feature_override_without_emitting_it()
    {
        var request = CreateRequest() with
        {
            InstanceIntent = CreateRequest().InstanceIntent with
            {
                Application = new("server-studio", "starter", [new("unlisted", ElsaFeatureOverride.FromBoolean(true))], "approved")
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "application.featureOverride.unsupported");
        Assert.DoesNotContain(result.Plan?.Configuration.Entries ?? [], entry => entry.Key == "featureOverride.unlisted");
    }

    [Fact]
    public async Task Rejects_feature_override_kind_mismatch_without_emitting_it()
    {
        var request = CreateRequest() with
        {
            InstanceIntent = CreateRequest().InstanceIntent with
            {
                Application = new("server-studio", "starter", [new("replicas", ElsaFeatureOverride.FromBoolean(true))], "approved")
            }
        };
        var resolver = new ElsaInstancePlanResolver(
            new FakeCatalog([]),
            new FakeCompatibility(),
            new ElsaInstancePlanResolutionOptions(
                FeatureOverrideDefinitions: new Dictionary<string, ElsaFeatureOverrideKind>
                {
                    ["replicas"] = ElsaFeatureOverrideKind.Number
                }));

        var result = await resolver.ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "application.featureOverride.kindMismatch");
        Assert.DoesNotContain(result.Plan?.Configuration.Entries ?? [], entry => entry.Key == "featureOverride.replicas");
    }

    [Fact]
    public async Task Rejects_unknown_channel_and_lifecycle_values()
    {
        var request = CreateRequest() with
        {
            InstanceIntent = CreateRequest().InstanceIntent with
            {
                Release = new ElsaReleaseIntent("future-runtime", "5.0", channel: "future-channel")
            },
            ReleaseManifest = AdmittedManifest("5.0", "5.0.0-preview.1", "server-studio", channel: "future-channel", lifecycle: "future-lifecycle")
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "release.channel.unsupported");
        Assert.Contains(result.Findings, finding => finding.Code == "release.lifecycle.unsupported");
    }

    [Theory]
    [InlineData("sbom")]
    [InlineData("provenance")]
    [InlineData("vulnerabilityScan")]
    public async Task Rejects_missing_required_supply_chain_evidence(string missingKind)
    {
        var baseline = CreateRequest();
        var topology = baseline.ReleaseManifest.Manifest!.Topologies[0];
        var supplyChain = topology.SupplyChain! with
        {
            Sbom = missingKind == "sbom" ? null : topology.SupplyChain!.Sbom,
            Provenance = missingKind == "provenance" ? null : topology.SupplyChain!.Provenance,
            VulnerabilityScan = missingKind == "vulnerabilityScan" ? null : topology.SupplyChain!.VulnerabilityScan
        };
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with
            {
                Manifest = baseline.ReleaseManifest.Manifest with
                {
                    Topologies = [topology with { SupplyChain = supplyChain }]
                }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == $"supplyChain.{missingKind}.required");
    }

    [Fact]
    public async Task Rejects_missing_retained_signature_evidence()
    {
        var baseline = CreateRequest();
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with { SignatureEvidence = null }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "releaseManifest.notAdmitted");
    }

    [Fact]
    public async Task Rejects_requested_topology_when_null_admission_selection_defaults_to_a_different_topology()
    {
        var baseline = CreateRequest();
        var selected = baseline.ReleaseManifest.Manifest!.Topologies[0];
        var first = selected with { Id = "combined" };
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with
            {
                TopologyId = null,
                Manifest = baseline.ReleaseManifest.Manifest with { Topologies = [first, selected] }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "topology.selection.mismatch");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Blank_admission_topology_selection_defaults_to_the_first_topology(string topologyId)
    {
        var baseline = CreateRequest();
        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility())
            .ResolveAsync(baseline with
            {
                ReleaseManifest = baseline.ReleaseManifest with { TopologyId = topologyId }
            });

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.Equal(baseline.InstanceIntent.Application.TopologyId, result.Plan!.Topology.Id);
    }

    [Fact]
    public async Task Rejects_malformed_explicit_evidence_digest_even_when_reference_embeds_a_digest()
    {
        var baseline = CreateRequest();
        var topology = baseline.ReleaseManifest.Manifest!.Topologies[0];
        var supplyChain = topology.SupplyChain! with
        {
            Sbom = topology.SupplyChain!.Sbom! with { Digest = "sha256:invalid" }
        };
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with
            {
                Manifest = baseline.ReleaseManifest.Manifest with
                {
                    Topologies = [topology with { SupplyChain = supplyChain }]
                }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "supplyChain.sbom.invalid");
    }

    [Fact]
    public async Task Rejects_package_runtime_incompatible_with_topology_without_selected_features()
    {
        var sourceId = Guid.NewGuid();
        var source = new PublicPackageSourceProjection(sourceId, "source", "https://packages.example.test/index.json");
        var version = new PublicPackageVersionProjection(
            "Elsa.StudioOnly",
            "2.0.0",
            source,
            "1.0",
            ["elsa.studio"],
            null,
            [],
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var baseline = CreateRequest();
        var topology = baseline.ReleaseManifest.Manifest!.Topologies[0] with { RuntimeKinds = ["elsa.server"] };
        var request = baseline with
        {
            BuilderIntent = baseline.BuilderIntent with
            {
                Packages = [new(sourceId, "Elsa.StudioOnly", "2.0.0", null, null)]
            },
            ReleaseManifest = baseline.ReleaseManifest with
            {
                Manifest = baseline.ReleaseManifest.Manifest with { Topologies = [topology] }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([version]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "package.runtimeKindUnsupported");
    }

    [Fact]
    public async Task Rejects_topology_runtime_incompatibility_before_projection()
    {
        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility(compatible: false)).ResolveAsync(CreateRequest());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "compatibility.rejected");
    }

    [Theory]
    [InlineData("registry.example/future:latest")]
    [InlineData("registry.example/future")]
    public async Task Rejects_mutable_or_non_digest_component_reference(string imageReference)
    {
        var baseline = CreateRequest();
        var topology = baseline.ReleaseManifest.Manifest!.Topologies[0];
        var image = topology.Images[0]! with { Reference = imageReference };
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with
            {
                Manifest = baseline.ReleaseManifest.Manifest with
                {
                    Topologies = [topology with { Images = [image] }]
                }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "releaseManifest.image.invalid");
    }

    [Theory]
    [InlineData("https://attacker.example.test/callback")]
    [InlineData("/api?token=secret")]
    [InlineData("/api/../admin")]
    [InlineData("/api/%2e%2e/admin")]
    [InlineData("/api\\admin")]
    [InlineData("/api//admin")]
    public async Task Rejects_unsafe_image_endpoint_path(string path)
    {
        var baseline = CreateRequest();
        var topology = baseline.ReleaseManifest.Manifest!.Topologies[0];
        var image = topology.Images[0]! with
        {
            Endpoints = [topology.Images[0]!.Endpoints![0] with { Path = path }]
        };
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with
            {
                Manifest = baseline.ReleaseManifest.Manifest with
                {
                    Topologies = [topology with { Images = [image] }]
                }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "releaseManifest.image.invalid");
    }

    [Fact]
    public async Task Treats_blank_optional_image_endpoint_path_as_absent()
    {
        var baseline = CreateRequest();
        var topology = baseline.ReleaseManifest.Manifest!.Topologies[0];
        var image = topology.Images[0]! with
        {
            Endpoints = [topology.Images[0]!.Endpoints![0] with { Path = " " }]
        };
        var request = baseline with
        {
            ReleaseManifest = baseline.ReleaseManifest with
            {
                Manifest = baseline.ReleaseManifest.Manifest with
                {
                    Topologies = [topology with { Images = [image] }]
                }
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(finding => finding.Code)));
        Assert.Null(Assert.Single(Assert.Single(result.Plan!.Topology.Components).Endpoints).Path);
        Assert.Null(Assert.Single(result.Plan.Network.Endpoints).Path);
    }

    [Fact]
    public async Task Rejects_raw_secret_setting_and_retains_only_safe_external_reference()
    {
        var sourceId = Guid.NewGuid();
        var source = new PublicPackageSourceProjection(sourceId, "source", "https://packages.example.test/index.json");
        var feature = new PublicFeatureProjection(
            "email", "Elsa.Email", "2.0.0", source, "Elsa.Email.Feature", "Email", null, null, [], [], ["elsa.server"], [], [], [], false, false, "{}",
            [new("smtpPassword", "System.String", "string", true, null, "SMTP password", null, null, "{}", true, true, "SMTP_PASSWORD", "{}", "{}")]);
        var version = new PublicPackageVersionProjection("Elsa.Email", "2.0.0", source, "1.0", ["elsa.server"], null, [feature], "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var request = CreateRequest() with
        {
            BuilderIntent = CreateRequest().BuilderIntent with
            {
                Packages = [new(sourceId, "Elsa.Email", "2.0.0", ["email"], new Dictionary<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>>
                {
                    ["email"] = new Dictionary<string, System.Text.Json.JsonElement>
                    {
                        ["smtpPassword"] = System.Text.Json.JsonSerializer.SerializeToElement("super-secret")
                    }
                })]
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([version]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "configuration.secretValue.forbidden");
        Assert.DoesNotContain(result.Findings, finding => finding.Message.Contains("super-secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolves_typed_settings_and_external_secret_references_without_retaining_secret_values()
    {
        var sourceId = Guid.NewGuid();
        var source = new PublicPackageSourceProjection(sourceId, "source", "https://packages.example.test/index.json");
        var feature = new PublicFeatureProjection(
            "email", "Elsa.Email", "2.0.0", source, "Elsa.Email.Feature", "Email", null, null, [], [], ["elsa.server"], [], [], [], false, false, "{}",
            [
                new("smtpHost", "System.String", "string", true, "\"smtp.example.test\"", "SMTP host", null, null, "{}", false, false, "SMTP_HOST", "{}", "{}"),
                new("smtpPassword", "System.String", "string", true, null, "SMTP password", null, null, "{}", true, true, "SMTP_PASSWORD", "{}", "{}")
            ]);
        var version = new PublicPackageVersionProjection("Elsa.Email", "2.0.0", source, "1.0", ["elsa.server"], null, [feature], "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var request = CreateRequest() with
        {
            BuilderIntent = CreateRequest().BuilderIntent with
            {
                Packages = [new(sourceId, "Elsa.Email", "2.0.0", ["email"], new Dictionary<string, IReadOnlyDictionary<string, System.Text.Json.JsonElement>>
                {
                    ["email"] = new Dictionary<string, System.Text.Json.JsonElement>
                    {
                        ["smtpPassword"] = System.Text.Json.JsonSerializer.SerializeToElement("secret://workspace/smtp-password")
                    }
                })]
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([version]), new FakeCompatibility()).ResolveAsync(request);

        Assert.True(result.Succeeded, string.Join("; ", result.Findings.Select(x => x.Code)));
        var host = Assert.Single(result.Plan!.Configuration.Entries, entry => entry.Key == "smtpHost");
        Assert.Equal("smtp.example.test", host.Value!.Value.GetString());
        var password = Assert.Single(result.Plan.Configuration.Entries, entry => entry.Key == "smtpPassword");
        Assert.Equal("secret://workspace/smtp-password", password.SecretReference);
        Assert.Null(password.Value);
        Assert.Contains("secret://workspace/smtp-password", ResolvedElsaApplicationPlanSerialization.Serialize(result.Plan));
    }

    [Fact]
    public async Task Rejects_missing_required_non_secret_setting_without_a_governed_default()
    {
        var sourceId = Guid.NewGuid();
        var source = new PublicPackageSourceProjection(sourceId, "source", "https://packages.example.test/index.json");
        var feature = new PublicFeatureProjection(
            "email", "Elsa.Email", "2.0.0", source, "Elsa.Email.Feature", "Email", null, null, [], [], ["elsa.server"], [], [], [], false, false, "{}",
            [new("smtpHost", "System.String", "string", true, null, "SMTP host", null, null, "{}", false, false, "SMTP_HOST", "{}", "{}")]);
        var version = new PublicPackageVersionProjection(
            "Elsa.Email", "2.0.0", source, "1.0", ["elsa.server"], null, [feature],
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var baseline = CreateRequest();
        var request = baseline with
        {
            BuilderIntent = baseline.BuilderIntent with
            {
                Packages = [new(sourceId, "Elsa.Email", "2.0.0", ["email"], null)]
            }
        };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([version]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "configuration.requiredValue.missing");
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task Produces_same_content_hash_for_reordered_safe_inputs()
    {
        var first = CreateRequest() with
        {
            ExistingEvidence = [
                new("z-evidence", "https://evidence.example.test/z", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "Safe evidence."),
                new("a-evidence", "https://evidence.example.test/a", "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "Safe evidence.")]
        };
        var second = first with { ExistingEvidence = first.ExistingEvidence!.Reverse().ToArray() };
        var resolver = new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility());

        var firstResult = await resolver.ResolveAsync(first);
        var secondResult = await resolver.ResolveAsync(second);

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.Equal(firstResult.Reference!.ContentHash, secondResult.Reference!.ContentHash);
    }

    [Theory]
    [InlineData("https://provider.example.test/api/workspaces/x/instances/y/plan")]
    [InlineData("https://control.example.test/api/workspaces/00000000-0000-0000-0000-000000000001/extra/instances/00000000-0000-0000-0000-000000000002/resolved-plans/plan_01J5FUTURE")]
    [InlineData("https://control.example.test/api/workspaces/00000000-0000-0000-0000-000000000001/instances/00000000-0000-0000-0000-000000000002/resolved-plans/a-different-plan")]
    [InlineData("https://control.example.test/api/workspaces/00000000-0000-0000-0000-000000000001/instances/00000000-0000-0000-0000-000000000002/resolved-plans/plan_01J5FUTURE/")]
    public async Task Rejects_non_control_plan_uri_before_projection(string planUri)
    {
        var request = CreateRequest() with { PlanUri = planUri };

        var result = await new ElsaInstancePlanResolver(new FakeCatalog([]), new FakeCompatibility()).ResolveAsync(request);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Findings, finding => finding.Code == "plan.uri.invalid");
    }

    private static string ComputeHash(ResolvedElsaApplicationPlan plan) =>
        $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ResolvedElsaApplicationPlanSerialization.Serialize(plan)))).ToLowerInvariant()}";

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private static ElsaInstancePlanResolutionRequest CreateRequest() => new(
        new ElsaInstanceIntent(
            new("future-runtime", "5.0", channel: "preview"),
            new("server-studio", featurePresetId: "starter", packagePolicy: "approved"),
            new("managed", "westeurope", "dedicated", "standard-small", "public", "managed")),
        new(new("future-runtime", null, null, null), [], [], [], null),
        AdmittedManifest("5.0", "5.0.0-preview.1", "server-studio"),
        "plan_01J5FUTURE",
        "https://control.example.test/api/workspaces/00000000-0000-0000-0000-000000000001/instances/00000000-0000-0000-0000-000000000002/resolved-plans/plan_01J5FUTURE");

    private static ReleaseManifestAdmissionResult AdmittedManifest(
        string releaseLine,
        string version,
        string topologyId,
        string channel = "preview",
        string lifecycle = "supported") => new(
        true,
        "oci://future-runtime/release-manifest@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        new(
            "1",
            new("future-runtime", "commercial", releaseLine, version, channel, lifecycle, new("https://github.com/example/future-runtime", "0123456789abcdef0123456789abcdef01234567", "release", "42")),
            [new(
                topologyId,
                ["elsa.server", "elsa.studio"],
                [new("paid", "registry.example/future@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", null, "server", ["server"], ["workflow.runtime"], [new("api", "https", 443, "public", true, "/api")], null)],
                new Dictionary<string, string> { ["server"] = version },
                new Dictionary<string, string> { ["api"] = "/api" },
                new("1", ["workflow.runtime", "workflow.studio"]),
                new(new("oci://future-runtime/sbom@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd", "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"), new("oci://future-runtime/provenance@sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"), [new("paid", "approved", "oci://future-runtime/signature@sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")], new("scanner", "policy-v1", "oci://future-runtime/scan@sha256:1111111111111111111111111111111111111111111111111111111111111111", "sha256:1111111111111111111111111111111111111111111111111111111111111111")))
        ]),
        new("oci://future-runtime/signature@sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"),
        "paid",
        topologyId,
        []);

    private static async Task<ReleaseManifestAdmissionResult> AdmitManifest(CommercialReleaseManifest manifest)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var digest = $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";
        var signatureDigest = Digest('f');
        var verifier = new StaticSignatureVerifier(new(
            true,
            "subject",
            digest,
            $"oci://future-runtime/signature@{signatureDigest}",
            signatureDigest));
        var artifact = new ReleaseManifestArtifact($"oci://future-runtime/release-manifest@{digest}", digest, payload);
        return await new ReleaseManifestAdmissionService(verifier).AdmitAsync(
            artifact,
            new("subject", "paid", manifest.Topologies[0].Id));
    }

    private sealed class FakeCatalog(IReadOnlyList<PublicPackageVersionProjection> versions) : IPublicCatalogQueries
    {
        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PublicPackageProjection>> ListPackagesForWorkspaceAsync(Guid workspaceId, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PublicPackageProjection?> GetPackageAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PublicPackageProjection?> GetPackageForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsAsync(Guid sourceId, string packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PublicPackageVersionProjection>>(versions.Where(x => x.Source.Id == sourceId && x.PackageId == packageId).ToArray());
        public Task<IReadOnlyList<PublicPackageVersionProjection>> ListVersionsForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, CancellationToken cancellationToken = default) => ListVersionsAsync(sourceId, packageId, cancellationToken);
        public Task<PublicPackageVersionProjection?> GetVersionAsync(Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) => Task.FromResult(versions.SingleOrDefault(x => x.Source.Id == sourceId && x.PackageId == packageId && x.Version == version));
        public Task<PublicPackageVersionProjection?> GetVersionForWorkspaceAsync(Guid workspaceId, Guid sourceId, string packageId, string version, CancellationToken cancellationToken = default) => GetVersionAsync(sourceId, packageId, version, cancellationToken);
        public Task<IReadOnlyList<PublicFeatureProjection>> ListFeaturesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PublicFeatureProjection?> GetFeatureAsync(string featureId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeCompatibility(bool compatible = true) : IPackageCompatibilityService
    {
        public Task<CompatibilityCheckResult> CheckAsync(CompatibilityCheckRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CompatibilityCheckResult(compatible, []));
    }

    private sealed class StaticSignatureVerifier(ReleaseManifestSignatureVerification verification) : IReleaseManifestSignatureVerifier
    {
        public ValueTask<ReleaseManifestSignatureVerification> VerifyAsync(
            ReleaseManifestArtifact artifact,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(verification);
    }
}
