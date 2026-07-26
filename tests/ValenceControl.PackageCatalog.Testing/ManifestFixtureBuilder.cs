using System.Text.Json;
using ValenceControl.PackageManifests;
using ValenceControl.PackageManifests.Compatibility;

namespace ValenceControl.PackageCatalog.Testing;

public sealed class ManifestFixtureBuilder
{
    private string _packageId = "Elsa.Email";
    private string _version = "1.0.0";
    private string _displayName = "Email";
    private IReadOnlyList<string>? _runtimeKinds;
    private readonly List<FeatureManifest> _features = [];
    private readonly List<string> _targetFrameworks = ["net10.0"];

    public ManifestFixtureBuilder WithPackage(string packageId, string version)
    {
        _packageId = packageId;
        _version = version;
        return this;
    }

    public ManifestFixtureBuilder WithDisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    public ManifestFixtureBuilder WithFeature(string featureId = "email", string typeName = "Elsa.Email.EmailFeature")
    {
        _features.Add(new FeatureManifest
        {
            Id = featureId,
            TypeName = typeName,
            DisplayName = "Email",
            Description = "Adds email activities and services.",
            Category = "Communication"
        });

        return this;
    }

    public ManifestFixtureBuilder WithFeature(
        string featureId,
        string typeName,
        IReadOnlyList<string>? runtimeKinds)
    {
        _features.Add(new FeatureManifest
        {
            Id = featureId,
            TypeName = typeName,
            DisplayName = "Email",
            Description = "Adds email activities and services.",
            Category = "Communication",
            Compatibility = runtimeKinds is null ? null : new CompatibilityManifest { RuntimeKinds = runtimeKinds }
        });

        return this;
    }

    public ManifestFixtureBuilder WithRuntimeKinds(params string[] runtimeKinds)
    {
        _runtimeKinds = runtimeKinds;
        return this;
    }

    public ManifestFixtureBuilder WithTargetFrameworks(params string[] targetFrameworks)
    {
        _targetFrameworks.Clear();
        _targetFrameworks.AddRange(targetFrameworks);
        return this;
    }

    public ElsaPackageManifest Build() => new()
    {
        SchemaVersion = ManifestSchemaVersions.Current,
        Package = new PackageIdentityManifest { Id = _packageId, Version = _version },
        DisplayName = _displayName,
        Compatibility = _runtimeKinds is null ? null : new CompatibilityManifest { RuntimeKinds = _runtimeKinds },
        Features = _features,
        Extensions = { ["targetFrameworks"] = _targetFrameworks.ToArray() }
    };

    public string BuildJson() => JsonSerializer.Serialize(Build(), ManifestJsonSerializerOptions.Default);
}
