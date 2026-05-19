using System.Reflection;
using Elsa.Platform.PackageManifests;
using Elsa.Platform.PackageManifests.Compatibility;
using Elsa.Platform.PackageManifests.Documentation;
using Elsa.Platform.PackageManifests.Infrastructure;
using Elsa.Platform.PackageManifests.Licensing;
using Elsa.Platform.PackageManifests.Validation;
using FluentAssertions;

namespace Elsa.Platform.PackageManifests.Tests;

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
