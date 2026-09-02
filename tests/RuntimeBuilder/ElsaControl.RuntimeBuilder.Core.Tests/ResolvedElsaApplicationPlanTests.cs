using System.Text.Json;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class ResolvedElsaApplicationPlanTests
{
    [Fact]
    public void Serialization_is_deterministic_for_equivalent_collection_orderings()
    {
        var first = CreatePlan(reverseCollections: false);
        var second = CreatePlan(reverseCollections: true);

        Assert.Equal(
            ResolvedElsaApplicationPlanSerialization.Serialize(first),
            ResolvedElsaApplicationPlanSerialization.Serialize(second));
    }

    [Fact]
    public void Serialization_canonicalizes_nested_json_objects_but_preserves_array_order()
    {
        var first = CreatePlan() with
        {
            Configuration = new([new(
                "Runtime:Options",
                "object",
                false,
                false,
                false,
                null,
                JsonValue("{\"z\":{\"b\":2,\"a\":1},\"a\":[{\"d\":4,\"c\":3}]}"),
                null,
                null)])
        };
        var second = first with
        {
            Configuration = new([new(
                "Runtime:Options",
                "object",
                false,
                false,
                false,
                null,
                JsonValue("{\"a\":[{\"c\":3,\"d\":4}],\"z\":{\"a\":1,\"b\":2}}"),
                null,
                null)])
        };

        Assert.Equal(
            ResolvedElsaApplicationPlanSerialization.Serialize(first),
            ResolvedElsaApplicationPlanSerialization.Serialize(second));
    }

    [Fact]
    public void Serialization_round_trips_an_arbitrary_release_line_and_composition()
    {
        var plan = CreatePlan(releaseLine: "4.0", topologyId: "server-studio", reverseCollections: true);

        var roundTripped = ResolvedElsaApplicationPlanSerialization.Deserialize(
            ResolvedElsaApplicationPlanSerialization.Serialize(plan));

        Assert.Equal("4.0", roundTripped.Release.ReleaseLine);
        Assert.Equal("server-studio", roundTripped.Topology.Id);
        Assert.Equal(2, roundTripped.Topology.Components.Count);
        Assert.Equal(
            ResolvedElsaApplicationPlanSerialization.Serialize(plan),
            ResolvedElsaApplicationPlanSerialization.Serialize(roundTripped));
    }

    [Fact]
    public void Validator_rejects_mutable_images_invalid_digests_and_embedded_secrets()
    {
        var plan = CreatePlan() with
        {
            Topology = new(
                "combined",
                [new(
                    "runtime",
                    ["server"],
                    new("paid", "registry/runtime", "registry/runtime:latest", "not-a-digest"),
                    ["elsa.server"],
                    [new("api", "http", 8080, "public", true)],
                    [])]),
            Configuration = new(
                [new("Database:ConnectionString", "string", true, true, false, null, JsonDocument.Parse("\"secret\"").RootElement, null, null)])
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(plan);

        Assert.Contains(findings, x => x.Code == "image.reference.immutableRequired");
        Assert.Contains(findings, x => x.Code == "image.digest.invalid");
        Assert.Contains(findings, x => x.Code == "configuration.secretValue.forbidden");
    }

    [Fact]
    public void Validator_reports_malformed_nested_records_instead_of_throwing()
    {
        var malformed = CreatePlan() with
        {
            Release = null!,
            Topology = new("combined", [null!, CreatePlan().Topology.Components[0] with { Image = null!, Endpoints = [null!] }]),
            Packages = [null!],
            Configuration = new([null!]),
            Capacity = new([null!], [null!]),
            Network = new("public", "restricted", false, [], [null!]),
            ReleasePolicy = null!,
            ProviderCapabilities = [null!],
            Evidence = [null!]
        };

        var exception = Record.Exception(() => ResolvedElsaApplicationPlanValidator.Validate(malformed));
        var findings = ResolvedElsaApplicationPlanValidator.Validate(malformed);

        Assert.Null(exception);
        Assert.Contains(findings, x => x.Code == "release.required");
        Assert.Contains(findings, x => x.Code == "topology.component.null");
        Assert.Contains(findings, x => x.Code == "image.required");
        Assert.Contains(findings, x => x.Code == "topology.component.endpoint.null");
        Assert.Contains(findings, x => x.Code == "package.null");
        Assert.Contains(findings, x => x.Code == "configuration.entry.null");
        Assert.Contains(findings, x => x.Code == "capacity.component.null");
        Assert.Contains(findings, x => x.Code == "network.endpoint.null");
        Assert.Contains(findings, x => x.Code == "releasePolicy.required");
        Assert.Contains(findings, x => x.Code == "providerCapability.null");
        Assert.Contains(findings, x => x.Code == "evidence.null");
    }

    [Fact]
    public void Validator_rejects_non_locator_secret_references()
    {
        var references = new[]
        {
            "database-secret",
            "https://vault.example/secrets/database",
            "AzureKeyVault://vault/database",
            "secret:/database",
            "secret://vault/database?token=unsafe",
            "secret://user:password@vault/database",
            "secret://vault/database/../admin",
            "secret://vault/database/./password",
            "secret://vault/database//password",
            "secret://vault/database%2f..%2fadmin",
            "secret://vault/database\\password",
            "secret://vault/database\0password",
            "secret://host",
            "secret://host/",
            "secret://vault/database/"
        };
        var plan = CreatePlan() with
        {
            Configuration = new(references.Select((reference, index) => new ResolvedConfigurationEntry(
                $"Runtime:Secret:{index}",
                "string",
                true,
                true,
                false,
                null,
                null,
                reference,
                null)).ToArray())
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(plan);

        Assert.Equal(references.Length, findings.Count(x => x.Code == "configuration.secretReference.invalid"));
        Assert.All(
            findings.Where(x => x.Code == "configuration.secretReference.invalid"),
            finding => Assert.Equal(SecretReferencePolicy.InvalidReferenceMessage, finding.Message));
        Assert.Empty(ResolvedElsaApplicationPlanValidator.Validate(CreatePlan()));
    }

    [Theory]
    [InlineData("secret://host")]
    [InlineData("secret://host/")]
    [InlineData("secret://host/database/")]
    public void Rejects_secret_references_with_root_or_non_canonical_paths(string reference)
    {
        Assert.False(SecretReferencePolicy.IsSafe(reference));
    }

    [Theory]
    [InlineData("invalid\nkey")]
    [InlineData("invalid\0key")]
    [InlineData(" invalid")]
    [InlineData("invalid ")]
    [InlineData("invalid/key")]
    public void Validator_rejects_unsafe_configuration_keys_without_echoing_them(string key)
    {
        var plan = CreatePlan() with
        {
            Configuration = new([new(key, "string", false, false, false, null, JsonValue("\"safe\""), null, null)])
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(plan);
        var finding = Assert.Single(findings, x => x.Code == "configuration.key.invalid");

        Assert.Equal("configuration.entries", finding.Scope);
        Assert.DoesNotContain(key, JsonSerializer.Serialize(findings), StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_rejects_overlong_configuration_keys_without_echoing_them()
    {
        var key = new string('k', ConfigurationKeyPolicy.MaxLength + 1);
        var plan = CreatePlan() with
        {
            Configuration = new([new(key, "string", false, false, false, null, JsonValue("\"safe\""), null, null)])
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(plan);
        var finding = Assert.Single(findings, x => x.Code == "configuration.key.invalid");

        Assert.Equal("configuration.entries", finding.Scope);
        Assert.DoesNotContain(key, JsonSerializer.Serialize(findings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validator_reports_missing_configuration_keys_with_fixed_scope(string? key)
    {
        var plan = CreatePlan() with
        {
            Configuration = new([new(key!, "string", false, false, false, null, JsonValue("\"safe\""), null, null)])
        };

        var finding = Assert.Single(
            ResolvedElsaApplicationPlanValidator.Validate(plan),
            x => x.Code == "configuration.key.required");

        Assert.Equal("configuration.entries", finding.Scope);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/api", true)]
    [InlineData("/api/", false)]
    [InlineData("//api", false)]
    public void Enforces_canonical_endpoint_path_form(string path, bool expected)
    {
        Assert.Equal(expected, EndpointPathPolicy.IsSafe(path));
    }

    [Theory]
    [InlineData("https://attacker.example.test/callback")]
    [InlineData("/elsa/api?token=secret")]
    [InlineData("/elsa/api/../admin")]
    [InlineData("/elsa/api/")]
    [InlineData("//api")]
    public void Validator_rejects_unsafe_topology_endpoint_paths(string path)
    {
        var baseline = CreatePlan();
        var component = baseline.Topology.Components[0];
        var plan = baseline with
        {
            Topology = new("combined", [component with
            {
                Endpoints = [component.Endpoints[0] with { Path = path }, component.Endpoints[1]]
            }])
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(plan);

        Assert.Contains(findings, x => x.Code == "endpoint.path.invalid");
    }

    [Fact]
    public void Duplicate_platform_keys_are_reported_and_normalization_rejects_them_deterministically()
    {
        var image = new ResolvedImageIdentity(
            "paid",
            "registry/runtime",
            $"registry/runtime@{Digest('a')}",
            Digest('a'),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["linux/amd64"] = Digest('a'),
                ["LINUX/AMD64"] = Digest('b')
            });
        var plan = CreatePlan() with
        {
            Topology = new("combined", [CreatePlan().Topology.Components[0] with { Image = image }])
        };

        var findings = ResolvedElsaApplicationPlanValidator.Validate(plan);

        Assert.Contains(findings, x => x.Code == "image.platform.duplicate");
        var exception = Assert.Throws<ArgumentException>(() => ResolvedElsaApplicationPlanSerialization.Serialize(plan));
        Assert.Contains("Platform digest key", exception.Message);
    }

    [Fact]
    public void Serialization_rejects_null_collection_items_deterministically()
    {
        var plan = CreatePlan() with { Packages = [null!] };

        var exception = Assert.Throws<ArgumentException>(() => ResolvedElsaApplicationPlanSerialization.Serialize(plan));

        Assert.Contains("packages", exception.Message);
    }

    [Fact]
    public void Validator_accepts_combined_and_server_studio_without_major_version_branching()
    {
        var combined = CreatePlan(releaseLine: "3.8", topologyId: "combined");
        var separate = CreatePlan(releaseLine: "4.0", topologyId: "server-studio");

        Assert.Empty(ResolvedElsaApplicationPlanValidator.Validate(combined));
        Assert.Empty(ResolvedElsaApplicationPlanValidator.Validate(separate));
    }

    [Fact]
    public void Validator_rejects_arbitrary_customer_packages_for_dedicated_isolation()
    {
        var baseline = CreatePlan();
        var plan = baseline with
        {
            Packages =
            [
                baseline.Packages[0] with
                {
                    ExtensionClass = ResolvedExtensionClass.ArbitraryCustomer
                }
            ]
        };

        var finding = Assert.Single(
            ResolvedElsaApplicationPlanValidator.Validate(plan),
            candidate => candidate.Code == "package.extensionClass.forbidden");

        Assert.Equal("packages", finding.Scope);
        Assert.DoesNotContain(baseline.Packages[0].PackageId, JsonSerializer.Serialize(finding), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("secret-1.*")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0")]
    public void Validator_rejects_inexact_package_versions_with_value_free_finding(string version)
    {
        var baseline = CreatePlan();
        var plan = baseline with
        {
            Packages = [baseline.Packages[0] with { Version = version }]
        };

        var finding = Assert.Single(
            ResolvedElsaApplicationPlanValidator.Validate(plan),
            candidate => candidate.Code == "package.version.inexact");

        Assert.Equal("packages", finding.Scope);
        Assert.DoesNotContain(version, JsonSerializer.Serialize(finding), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Dedicated", ResolvedExtensionClass.BuiltIn, true)]
    [InlineData("Dedicated", ResolvedExtensionClass.ValenceApproved, true)]
    [InlineData("Dedicated", ResolvedExtensionClass.ArbitraryCustomer, false)]
    [InlineData("Shared", ResolvedExtensionClass.BuiltIn, true)]
    [InlineData("Shared", ResolvedExtensionClass.ValenceApproved, false)]
    [InlineData("Data-isolated", ResolvedExtensionClass.ValenceApproved, false)]
    [InlineData("Private", ResolvedExtensionClass.ValenceApproved, true)]
    [InlineData("Private", ResolvedExtensionClass.ArbitraryCustomer, false)]
    [InlineData("future-profile", ResolvedExtensionClass.BuiltIn, false)]
    public void Extension_policy_fails_closed_for_unavailable_or_unproven_classes(
        string isolation,
        ResolvedExtensionClass extensionClass,
        bool expected)
    {
        Assert.Equal(expected, ResolvedExtensionPolicy.IsAllowed(isolation, extensionClass));
    }

    [Fact]
    public void Legacy_plan_without_extension_authority_fails_closed()
    {
        var json = ResolvedElsaApplicationPlanSerialization.Serialize(CreatePlan());
        var document = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json)!;
        var package = document["packages"]!.AsArray()[0]!.AsObject();
        package.Remove("extensionClass");
        package.Remove("policyEvidenceDigest");

        var legacy = ResolvedElsaApplicationPlanSerialization.Deserialize(document.ToJsonString());
        var findings = ResolvedElsaApplicationPlanValidator.Validate(legacy);

        Assert.Contains(findings, candidate => candidate.Code == "package.extensionClass.required");
        Assert.Contains(findings, candidate => candidate.Code == "package.policyEvidenceDigest.invalid");
    }

    [Fact]
    public void Canonical_hash_binds_extension_class_and_policy_evidence()
    {
        var baseline = CreatePlan();
        var builtIn = baseline with
        {
            Packages = [baseline.Packages[0] with { ExtensionClass = ResolvedExtensionClass.BuiltIn }]
        };
        var differentEvidence = baseline with
        {
            Packages = [baseline.Packages[0] with { PolicyEvidenceDigest = Digest('c') }]
        };
        var equivalentEvidenceCasing = baseline with
        {
            Packages = [baseline.Packages[0] with { PolicyEvidenceDigest = baseline.Packages[0].PolicyEvidenceDigest!.ToUpperInvariant() }]
        };

        Assert.NotEqual(
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(baseline),
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(builtIn));
        Assert.NotEqual(
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(baseline),
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(differentEvidence));
        Assert.Equal(
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(baseline),
            ResolvedElsaApplicationPlanSerialization.ComputeContentHash(equivalentEvidenceCasing));
    }

    [Fact]
    public void Validator_rejects_unknown_isolation_even_without_packages()
    {
        var plan = CreatePlan() with { Isolation = "future-profile", Packages = [] };

        Assert.Contains(
            ResolvedElsaApplicationPlanValidator.Validate(plan),
            candidate => candidate.Code == "isolation.profile.unknown");
    }

    private static ResolvedElsaApplicationPlan CreatePlan(
        string releaseLine = "3.8",
        string topologyId = "combined",
        bool reverseCollections = false)
    {
        var digestA = Digest('a');
        var digestB = Digest('b');
        var sourceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var package = new ResolvedElsaPackage(
            sourceId,
            "Elsa.Core",
            releaseLine == "3.8" ? "3.8.0-preview.5413" : "4.0.0-preview.1",
            digestA,
            ["elsa.server"],
            [new("runtime", "Elsa.Runtime", ["elsa.server"], ["workflow.runtime"]) ],
            ResolvedExtensionClass.ValenceApproved,
            digestB);

        ResolvedElsaComponent[] components = topologyId == "combined"
            ? [new ResolvedElsaComponent(
                "runtime",
                ["studio", "server"],
                new("paid", "valenceruntimeimages.azurecr.io/runtime-combined", $"valenceruntimeimages.azurecr.io/runtime-combined@{digestA}", digestA, new Dictionary<string, string>
                {
                    ["linux/arm64"] = digestB,
                    ["linux/amd64"] = digestA
                }),
                ["elsa.studio", "elsa.server"],
                [new("studio", "https", 8080, "public", true, "/"), new("api", "https", 8080, "public", true, "/elsa/api")],
                ["workflow.runtime", "workflow.studio"])]
            : [
                new ResolvedElsaComponent(
                    "studio",
                    ["studio"],
                    new("community", "registry/elsa-studio", $"registry/elsa-studio@{digestB}", digestB),
                    ["elsa.studio"],
                    [new("ui", "https", 8081, "public", true, "/")],
                    ["workflow.studio"],
                    "server"),
                new ResolvedElsaComponent(
                    "server",
                    ["server"],
                    new("community", "registry/elsa-server", $"registry/elsa-server@{digestA}", digestA),
                    ["elsa.server"],
                    [new("api", "https", 8080, "private", true, "/elsa/api")],
                    ["workflow.runtime"])
            ];

        if (reverseCollections)
            Array.Reverse(components);

        return new(
            ResolvedElsaApplicationPlanSchema.CurrentVersion,
            new(
                "valence-runtime",
                releaseLine,
                releaseLine == "3.8" ? "3.8.0-preview.5413" : "4.0.0-preview.1",
                "https://github.com/valence-works/elsa-production-image",
                "1aeee8df455b21cf3bf3d2b26dfbd512d76da27b",
                "oci://release-manifest",
                digestB),
            new(topologyId, components),
            reverseCollections ? [package with { Features = [package.Features[0] with { RequiredCapabilities = ["workflow.runtime"] }] }] : [package],
            new([new("Database:ConnectionString", "string", true, true, false, "ELSA_DATABASE_CONNECTION", null, "secret://database/connection", null)]),
            new([new(topologyId == "combined" ? "runtime" : "server", 1, 1, 500, 1024)], [new("elsa-data", "relational", "persistent", "exclusive", 10)]),
            new("public", "restricted", true, ["registry.example"], [new(topologyId == "combined" ? "runtime" : "server", "api", "https", 443, "public", true, "/elsa/api")]),
            "Dedicated",
            new("preview", "Preview", "internal", "automatic-within-minor", "explicit-approval", "explicit-migration"),
            [new("managed-runtime", "Run the resolved runtime components.", true, ["container", "persistent-storage"])],
            [new("release-manifest", "oci://release-manifest", digestB, "Verified release manifest")]);
    }

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private static JsonElement JsonValue(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
