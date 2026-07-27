using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ValenceControl.Healing.ComponentManifest;

namespace ValenceControl.Healing.ComponentManifest.Tests;

public sealed class ComponentManifestTests
{
    [Fact]
    public void Serialize_is_canonical_across_input_order_and_normalizes_safe_values()
    {
        var first = CreateManifest(
            components:
            [
                Package("Zulu", "2.0.0", "lib\\net10.0\\Zulu.dll", ["nuget:Alpha:1.0.0"]),
                Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])
            ],
            repositoryUrl: "HTTPS://GitHub.com/Acme/WorkflowHost.git/");
        var second = CreateManifest(
            components:
            [
                Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", []),
                Package("Zulu", "2.0.0", "lib/net10.0/Zulu.dll", ["nuget:Alpha:1.0.0"])
            ],
            repositoryUrl: "https://github.com/Acme/WorkflowHost");

        var firstJson = ComponentManifestSerializer.Serialize(first);
        var secondJson = ComponentManifestSerializer.Serialize(second);

        Assert.Equal(secondJson, firstJson);
        Assert.Contains("https://github.com/Acme/WorkflowHost", firstJson);
        Assert.DoesNotContain("\\\\", firstJson);
        using var document = JsonDocument.Parse(firstJson);
        Assert.Equal("Alpha", document.RootElement.GetProperty("components")[0].GetProperty("name").GetString());
        Assert.Matches("^sha256:[0-9a-f]{64}$", document.RootElement.GetProperty("manifestDigest").GetString());
    }

    [Fact]
    public void Digest_is_the_sha256_of_the_canonical_document_without_the_digest_field()
    {
        var manifest = CreateManifest([Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])]);

        var digest = ComponentManifestDigest.Compute(manifest);
        var serialized = ComponentManifestSerializer.Serialize(manifest);

        const string expectedCanonicalBody = "{\"application\":{\"name\":\"Acme.WorkflowHost\",\"runtimeIdentifier\":\"linux-x64\",\"targetFramework\":\"net10.0\",\"version\":\"2.4.1\"},\"components\":[{\"assemblies\":[{\"contentHash\":\"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"name\":\"Alpha\",\"publicKeyToken\":null,\"relativePath\":\"lib/net10.0/Alpha.dll\",\"version\":\"1.0.0.0\"}],\"contentHash\":\"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"dependencies\":[],\"directDependency\":true,\"key\":\"nuget:Alpha:1.0.0\",\"kind\":\"package\",\"name\":\"Alpha\",\"repositoryCommit\":null,\"repositoryUrl\":\"https://github.com/acme/packages\",\"version\":\"1.0.0\"}],\"revision\":{\"buildId\":\"build-42\",\"createdAt\":\"2026-07-16T00:00:00Z\",\"repositoryUrl\":\"https://github.com/acme/workflow-host\",\"sourceRevision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"},\"schemaVersion\":\"1.0\"}";
        var expectedDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expectedCanonicalBody))).ToLowerInvariant()}";
        Assert.Equal(expectedDigest, digest);
        Assert.Contains($"\"manifestDigest\":\"{digest}\"", serialized);
    }

    [Theory]
    [InlineData("../secret.dll")]
    [InlineData("lib/../../secret.dll")]
    [InlineData("/tmp/secret.dll")]
    [InlineData("C:\\Users\\alice\\secret.dll")]
    public void Validate_rejects_paths_outside_the_component_root(string path)
    {
        var manifest = CreateManifest([Package("Alpha", "1.0.0", path, [])]);

        var findings = ComponentManifestValidator.Validate(manifest);

        Assert.Contains(findings, x => x.Code == "manifest.path.unsafe");
    }

    [Fact]
    public void Validate_rejects_duplicate_components_and_unknown_dependency_edges()
    {
        var first = Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", ["nuget:Missing:1.0.0"]);
        var conflicting = first with { ContentHash = Sha('b') };
        var manifest = CreateManifest([first, conflicting]);

        var findings = ComponentManifestValidator.Validate(manifest);

        Assert.Contains(findings, x => x.Code == "manifest.component.duplicate-key");
        Assert.Contains(findings, x => x.Code == "manifest.dependency.unknown");
    }

    [Fact]
    public void Validate_rejects_identical_duplicate_component_keys()
    {
        var manifest = CreateManifest([Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])]);
        var duplicate = manifest.Components[0];

        var findings = ComponentManifestValidator.Validate(manifest with
        {
            Components = [duplicate, duplicate]
        });

        Assert.Single(findings, x => x.Code == "manifest.component.duplicate-key");
    }

    [Fact]
    public void Serialize_never_emits_local_roots_user_names_or_repository_credentials()
    {
        var manifest = CreateManifest(
            [Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])],
            repositoryUrl: "https://token@github.com/Acme/WorkflowHost.git");

        var action = () => ComponentManifestSerializer.Serialize(manifest);

        var exception = Assert.Throws<ComponentManifestValidationException>(action);
        Assert.Contains(exception.Findings, x => x.Code == "manifest.repository.credentials");
    }

    [Fact]
    public void Unknown_component_kinds_and_safe_extension_fields_are_preserved_for_observation_only_consumers()
    {
        using var source = JsonDocument.Parse("{\"confidence\":0.7,\"source\":\"build-attestation\"}");
        var unknown = Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", []) with
        {
            Kind = "future-component-kind",
            ExtensionData = source.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal)
        };
        var manifest = CreateManifest([unknown]);

        var json = ComponentManifestSerializer.Serialize(manifest);
        var roundTrip = ComponentManifestSerializer.Deserialize(json);

        Assert.NotNull(roundTrip);
        var component = Assert.Single(roundTrip.Components, x => x.Kind == "future-component-kind");
        Assert.NotNull(component.ExtensionData);
        Assert.Contains("confidence", component.ExtensionData!.Keys);
    }

    [Theory]
    [InlineData("accessToken", "not-for-a-manifest")]
    [InlineData("nested", "{\"connectionString\":\"Server=localhost;Password=pw\"}")]
    [InlineData("localPath", "\"/Users/alice/.nuget/packages/acme\"")]
    [InlineData("artifact", "\"../outside/source.cs\"")]
    [InlineData("repository", "\"https://token@github.com/acme/private?access_token=x\"")]
    public void Unsafe_extension_data_is_rejected_recursively(string key, string jsonValue)
    {
        var encodedValue = jsonValue.StartsWith('{') || jsonValue.StartsWith('"')
            ? jsonValue
            : JsonSerializer.Serialize(jsonValue);
        using var value = JsonDocument.Parse(encodedValue);
        var manifest = CreateManifest([Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])]) with
        {
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = value.RootElement.Clone() }
        };

        var findings = ComponentManifestValidator.Validate(manifest);

        Assert.Contains(findings, x => x.Code == "manifest.extension.unsafe");
    }

    [Fact]
    public void Validate_reports_malformed_null_shapes_instead_of_throwing_null_reference_exceptions()
    {
        var manifest = CreateManifest([Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])]) with { Components = null! };

        var action = () => ComponentManifestValidator.Validate(manifest);

        Assert.Null(Record.Exception(action));
        Assert.Contains(action(), x => x.Code == "manifest.shape.invalid");
    }

    [Fact]
    public void Validate_reports_malformed_nested_required_shapes_without_throwing()
    {
        var malformed = Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", []) with
        {
            ContentHash = null!,
            Assemblies = null!,
            Dependencies = null!
        };
        var manifest = CreateManifest([malformed]) with { Application = null!, Revision = null! };

        var action = () => ComponentManifestValidator.Validate(manifest);

        Assert.Null(Record.Exception(action));
        Assert.Contains(action(), x => x.Code == "manifest.shape.invalid");
        Assert.Contains(action(), x => x.Code == "manifest.hash.invalid");
    }

    [Fact]
    public void Validate_rejects_a_non_commit_source_revision()
    {
        var manifest = CreateManifest([Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", [])]);
        manifest = manifest with { Revision = manifest.Revision with { SourceRevision = "main" } };

        Assert.Contains(ComponentManifestValidator.Validate(manifest), x => x.Code == "manifest.revision.source-revision");
    }

    [Fact]
    public void Serialize_normalizes_repository_commits_and_rejects_non_sha_metadata()
    {
        var uppercaseCommit = new string('B', 40);
        var component = Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", []) with { RepositoryCommit = uppercaseCommit };
        var manifest = CreateManifest([component]);

        var json = ComponentManifestSerializer.Serialize(manifest);

        Assert.Contains($"\"repositoryCommit\":\"{uppercaseCommit.ToLowerInvariant()}\"", json);

        var malicious = manifest with
        {
            Components = [component with { RepositoryCommit = "$(HOME)/token?secret=value" }]
        };
        Assert.Contains(ComponentManifestValidator.Validate(malicious), x => x.Code == "manifest.repository-commit.invalid");
    }

    [Fact]
    public void Validate_rejects_duplicate_normalized_assembly_paths_before_canonical_tie_ordering()
    {
        var component = Package("Alpha", "1.0.0", "lib/net10.0/Alpha.dll", []);
        var conflictingDuplicate = component.Assemblies[0] with
        {
            Name = "Conflicting.Alpha",
            RelativePath = "lib\\net10.0\\Alpha.dll",
            ContentHash = Sha('d')
        };
        var manifest = CreateManifest([component with { Assemblies = [component.Assemblies[0], conflictingDuplicate] }]);

        var findings = ComponentManifestValidator.Validate(manifest);

        Assert.Single(findings, x => x.Code == "manifest.assembly.duplicate-path");
        Assert.Single(ComponentManifestValidator.Validate(manifest with
        {
            Components = [component with { Assemblies = [conflictingDuplicate, component.Assemblies[0]] }]
        }), x => x.Code == "manifest.assembly.duplicate-path");
        var action = () => ComponentManifestSerializer.Serialize(manifest);
        Assert.Throws<ComponentManifestValidationException>(action);
    }

    private static HealingComponentManifest CreateManifest(
        IReadOnlyList<ComponentManifestEntry> components,
        string repositoryUrl = "https://github.com/acme/workflow-host") =>
        new(
            "1.0",
            new ComponentManifestApplication("Acme.WorkflowHost", "2.4.1", "net10.0", "linux-x64"),
            new ComponentManifestRevision(
                new string('a', 40),
                repositoryUrl,
                "build-42",
                DateTimeOffset.Parse("2026-07-16T00:00:00Z")),
            components);

    private static ComponentManifestEntry Package(
        string name,
        string version,
        string assemblyPath,
        IReadOnlyList<string> dependencies) =>
        new(
            $"nuget:{name}:{version}",
            "package",
            name,
            version,
            Sha('a'),
            "https://github.com/acme/packages",
            null,
            name == "Alpha",
            [new ComponentManifestAssembly(name, $"{version}.0", null, assemblyPath, Sha('c'))],
            dependencies);

    private static string Sha(char value) => $"sha256:{new string(value, 64)}";
}
