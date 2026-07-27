using ValenceControl.PackageManifest.Generator.Testing;

namespace ValenceControl.PackageManifest.Generator.IntegrationTests;

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

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(project.AssemblyPath));
    }
}
