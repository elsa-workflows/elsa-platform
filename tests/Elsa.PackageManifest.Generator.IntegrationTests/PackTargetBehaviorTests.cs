using Elsa.PackageManifest.Generator.Testing;
using FluentAssertions;

namespace Elsa.PackageManifest.Generator.IntegrationTests;

public sealed class PackTargetBehaviorTests
{
    [Fact]
    public async Task Build_then_pack_no_build_includes_existing_manifest_without_regenerating()
    {
        await using var project = CreateProjectWithReferencedFeatureAssembly();

        var build = await project.BuildAsync("Release");
        build.ExitCode.Should().Be(0, build.CombinedOutput);
        File.Exists(project.ManifestPathForConfiguration("Release", "net10.0")).Should().BeTrue();

        DeleteReferencedAssemblyCopies(project);

        var pack = await project.PackAsync("Release");

        pack.ExitCode.Should().Be(0, pack.CombinedOutput);
        NuGetPackageInspector.AssertSingleEntry(project.ReleasePackagePath, "elsa-package.json");
        NuGetPackageInspector.ReadEntry(project.ReleasePackagePath, "elsa-package.json").Should().Contain("Pack Feature");
    }

    [Fact]
    public async Task Pack_with_build_from_clean_state_generates_and_includes_manifest()
    {
        await using var project = CreateProjectWithReferencedFeatureAssembly();

        var pack = await project.PackWithBuildAsync("Release");

        pack.ExitCode.Should().Be(0, pack.CombinedOutput);
        File.Exists(project.ManifestPathForConfiguration("Release", "net10.0")).Should().BeTrue();
        NuGetPackageInspector.AssertSingleEntry(project.ReleasePackagePath, "elsa-package.json");
    }

    [Fact]
    public async Task Pack_no_build_reports_missing_required_manifest_clearly()
    {
        await using var project = CreateProjectWithReferencedFeatureAssembly();

        var pack = await project.PackAsync("Release");

        pack.ExitCode.Should().NotBe(0, pack.CombinedOutput);
        pack.CombinedOutput.Should().Contain("Elsa package manifest was not found");
        pack.CombinedOutput.Should().Contain("dotnet build");
        pack.CombinedOutput.Should().Contain("--no-build");
    }

    [Fact]
    public async Task Multi_targeted_pack_with_build_includes_single_root_manifest()
    {
        await using var project = new SampleProjectBuilder()
            .WithLocalGeneratorPackage()
            .WithProperty("LangVersion", "latest")
            .WithTargetFrameworks("net10.0", "netstandard2.1")
            .WithSource(FeatureSource);

        var pack = await project.PackWithBuildAsync("Release");

        pack.ExitCode.Should().Be(0, pack.CombinedOutput);
        NuGetPackageInspector.AssertSingleEntry(project.ReleasePackagePath, "elsa-package.json");
    }

    private static SampleProjectBuilder CreateProjectWithReferencedFeatureAssembly() =>
        new SampleProjectBuilder()
            .WithLocalGeneratorPackage()
            .WithExternalCShellsReference()
            .WithSource(FeatureSource);

    private static void DeleteReferencedAssemblyCopies(SampleProjectBuilder project)
    {
        foreach (var path in Directory.EnumerateFiles(project.ProjectDirectory, "CShells.Abstractions.dll", SearchOption.AllDirectories))
            File.Delete(path);
    }

    private const string FeatureSource = """
#nullable enable
using CShells.Features;

namespace Sample.Features;

[ShellFeature("PackFeature", DisplayName = "Pack Feature")]
public sealed class PackFeature : IShellFeature
{
    public string Endpoint { get; set; } = "";
}
""";
}
