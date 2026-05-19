using Elsa.Platform.PackageManifest.Generator.Testing;
using FluentAssertions;

namespace Elsa.Platform.PackageManifest.Generator.IntegrationTests;

public sealed class SampleProjectBuilderSmokeTests
{
    [Fact]
    public async Task BuildAsync_compiles_a_sample_cshells_feature_project()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Smoke", DisplayName = "Smoke Feature")]
public sealed class SmokeFeature : IShellFeature
{
    public string? Value { get; set; }
}
""");

        var result = await project.BuildAsync();

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        File.Exists(project.AssemblyPath).Should().BeTrue();
    }
}
