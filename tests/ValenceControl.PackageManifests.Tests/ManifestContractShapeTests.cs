using System.Reflection;
using ValenceControl.PackageManifests;
using ValenceControl.PackageManifests.Compatibility;
using ValenceControl.PackageManifests.Documentation;
using ValenceControl.PackageManifests.Infrastructure;
using ValenceControl.PackageManifests.Licensing;
using ValenceControl.PackageManifests.Validation;
using FluentAssertions;

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

        types.Should().OnlyContain(type => type.IsPublic || type.GetTypeInfo().IsNestedPublic);
    }
}
