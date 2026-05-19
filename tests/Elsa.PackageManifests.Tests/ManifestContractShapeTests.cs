using System.Reflection;
using Elsa.PackageManifests;
using Elsa.PackageManifests.Compatibility;
using Elsa.PackageManifests.Documentation;
using Elsa.PackageManifests.Infrastructure;
using Elsa.PackageManifests.Licensing;
using Elsa.PackageManifests.Validation;
using FluentAssertions;

namespace Elsa.PackageManifests.Tests;

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
