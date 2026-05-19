using Elsa.Platform.PackageManifest.Generator.Core.Generation;
using FluentAssertions;

namespace Elsa.Platform.PackageManifest.Generator.Core.Tests;

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

        metadata.PackageId.Should().Be("Elsa.Sample");
        metadata.Version.Should().Be("2.0.0");
        metadata.Authors.Should().Equal("Ada", "Zed");
        metadata.PackageTags.Should().Equal("elsa", "workflow");
        metadata.TargetFrameworks.Should().Equal("net10.0", "net9.0");
    }

    [Theory]
    [InlineData("Elsa.Common", "Common")]
    [InlineData("Elsa.Expressions.CSharp", "Expressions.CSharp")]
    [InlineData("elsa.Diagnostics.StructuredLogs", "Diagnostics.StructuredLogs")]
    [InlineData("Elsa", "Elsa")]
    [InlineData("Other.Package", "Other.Package")]
    public void Package_display_name_omits_elsa_namespace_prefix(string packageId, string displayName)
    {
        NamingHelpers.ToPackageDisplayName(packageId).Should().Be(displayName);
    }
}
