using ValenceControl.PackageManifest.Generator.MSBuild.Packaging;

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

        Assert.Equal(Path.Combine("obj", "elsa-package.json"), options.OutputPath);
        Assert.Equal("elsa-package.json", options.PackagePath);
        Assert.Equal("Error", options.ValidationSeverity);
        Assert.True(options.Strict);
        Assert.True(options.FailOnWarnings);
        Assert.False(options.AllowTargetFrameworkDifferences);
        Assert.Equal(["A.IFeature", "B.IFeature"], options.AdditionalFeatureInterfaceTypes);
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

        Assert.Equal(["Ada", "Zed"], metadata.Authors);
        Assert.Equal(["net10.0", "net9.0"], metadata.TargetFrameworks);
        Assert.Equal(["elsa", "workflow"], metadata.PackageTags);
    }
}
