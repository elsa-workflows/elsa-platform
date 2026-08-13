using ValenceControl.PackageManifest.Generator.Core.Generation;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class ProjectPackageMetadataTests
{
    [Fact]
    public void Map_splits_and_sorts_authors_tags_and_target_frameworks()
    {
        var metadata = ProjectPackageMetadataMapper.Map(
            "Elsa.Sample",
            "2.0.0",
            "Sample",
            "Description",
            "Zed;Ada",
            "https://example.com/repo",
            "https://example.com/project",
            "workflow;elsa;workflow",
            "MIT",
            "README.md",
            "net10.0",
            "net10.0;net9.0");

        Assert.Equal("Elsa.Sample", metadata.PackageId);
        Assert.Equal("2.0.0", metadata.Version);
        Assert.Equal(["Ada", "Zed"], metadata.Authors);
        Assert.Equal(["elsa", "workflow"], metadata.PackageTags);
        Assert.Equal(["net10.0", "net9.0"], metadata.TargetFrameworks);
    }

    [Theory]
    [InlineData("Elsa.Common", "Common")]
    [InlineData("Elsa.Expressions.CSharp", "Expressions.CSharp")]
    [InlineData("elsa.Diagnostics.StructuredLogs", "Diagnostics.StructuredLogs")]
    [InlineData("Elsa", "Elsa")]
    [InlineData("Other.Package", "Other.Package")]
    public void Package_display_name_omits_elsa_namespace_prefix(string packageId, string displayName)
    {
        Assert.Equal(displayName, NamingHelpers.ToPackageDisplayName(packageId));
    }
}
