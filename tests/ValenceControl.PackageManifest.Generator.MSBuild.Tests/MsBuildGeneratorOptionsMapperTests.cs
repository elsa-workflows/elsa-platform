using ValenceControl.PackageManifest.Generator.MSBuild.Packaging;
using FluentAssertions;

namespace ValenceControl.PackageManifest.Generator.MSBuild.Tests;

public sealed class MsBuildGeneratorOptionsMapperTests
{
    [Fact]
    public void MapOptions_uses_expected_defaults_and_splits_additional_interfaces()
    {
        var options = MsBuildGeneratorOptionsMapper.MapOptions(
            null,
            null,
            null,
            "true",
            "true",
            "false",
            null,
            "A.IFeature;B.IFeature");

        options.OutputPath.Should().Be(Path.Combine("obj", "elsa-package.json"));
        options.PackagePath.Should().Be("elsa-package.json");
        options.ValidationSeverity.Should().Be("Error");
        options.Strict.Should().BeTrue();
        options.FailOnWarnings.Should().BeTrue();
        options.AllowTargetFrameworkDifferences.Should().BeFalse();
        options.AdditionalFeatureInterfaceTypes.Should().Equal("A.IFeature", "B.IFeature");
    }

    [Fact]
    public void MapPackageMetadata_keeps_msbuild_metadata_deterministic()
    {
        var metadata = MsBuildGeneratorOptionsMapper.MapPackageMetadata(
            "Sample.Package",
            "1.0.0",
            "Sample Package",
            "Description",
            "Zed;Ada",
            "https://example.com/repo",
            "https://example.com/project",
            "workflow;elsa",
            "MIT",
            "README.md",
            "net10.0",
            "net10.0;net9.0");

        metadata.Authors.Should().Equal("Ada", "Zed");
        metadata.TargetFrameworks.Should().Equal("net10.0", "net9.0");
        metadata.PackageTags.Should().Equal("elsa", "workflow");
    }
}
