using System.Reflection;
using ValenceControl.PackageManifests;
using ValenceControl.PackageManifests.Compatibility;
using ValenceControl.PackageManifests.Documentation;
using ValenceControl.PackageManifests.Infrastructure;
using ValenceControl.PackageManifests.Licensing;
using ValenceControl.PackageManifests.Validation;

namespace ValenceControl.PackageManifests.Tests;

public sealed class ManifestContractShapeTests
{
    [Fact]
    public void Public_contract_types_are_available()
    {
        var types = new[]
        {
            typeof(ElsaPackageManifest),
            typeof(FeatureManifest),
            typeof(FeatureSettingManifest),
            typeof(CompatibilityManifest),
            typeof(DependencyManifest),
            typeof(ConflictManifest),
            typeof(InfrastructureRequirementManifest),
            typeof(LicenseManifest),
            typeof(DocumentationManifest),
            typeof(ManifestValidationResult)
        };

        Assert.All(types, type => Assert.True(type.IsPublic || type.GetTypeInfo().IsNestedPublic));
    }
}
