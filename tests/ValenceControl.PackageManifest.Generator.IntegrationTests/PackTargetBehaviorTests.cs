using ValenceControl.PackageManifest.Generator.Testing;

namespace ValenceControl.PackageManifest.Generator.IntegrationTests;

public sealed class PackTargetBehaviorTests
{
    [Fact]
    public async Task Build_then_pack_no_build_includes_existing_manifest_without_regenerating()
    {
        await using var project = CreateProjectWithReferencedFeatureAssembly();

        var build = await project.BuildAsync("Release");
        Assert.Equal(0, build.ExitCode);
        Assert.True(File.Exists(project.ManifestPathForConfiguration("Release", "net10.0")));

        DeleteReferencedAssemblyCopies(project);

        var pack = await project.PackAsync("Release");

        Assert.Equal(0, pack.ExitCode);
        NuGetPackageInspector.AssertSingleEntry(project.ReleasePackagePath, "elsa-package.json");
        Assert.Contains("Pack Feature", NuGetPackageInspector.ReadEntry(project.ReleasePackagePath, "elsa-package.json"));
    }

    [Fact]
    public async Task Pack_with_build_from_clean_state_generates_and_includes_manifest()
    {
        await using var project = CreateProjectWithReferencedFeatureAssembly();

        var pack = await project.PackWithBuildAsync("Release");

        Assert.Equal(0, pack.ExitCode);
        Assert.True(File.Exists(project.ManifestPathForConfiguration("Release", "net10.0")));
        NuGetPackageInspector.AssertSingleEntry(project.ReleasePackagePath, "elsa-package.json");
    }

    [Fact]
    public async Task Pack_with_package_reference_resolves_reference_closure_and_includes_manifest()
    {
        // Regression: during `dotnet pack` the manifest target runs in the pack evaluation where
        // @(ReferencePath) is not populated unless it is forced. When a feature surface references
        // types from a NuGet PackageReference (whose assembly is not copied to the output directory),
        // the generator must still resolve that assembly to discover features and pack must succeed.
        await using var project = new SampleProjectBuilder()
            .WithLocalGeneratorPackage()
            .WithExternalCShellsPackageReference()
            .WithSource(FeatureSource);

        var pack = await project.PackWithBuildAsync("Release");

        Assert.Equal(0, pack.ExitCode);
        Assert.DoesNotContain("Could not find assembly", pack.CombinedOutput);
        NuGetPackageInspector.AssertSingleEntry(project.ReleasePackagePath, "elsa-package.json");
        Assert.Contains("Pack Feature", NuGetPackageInspector.ReadEntry(project.ReleasePackagePath, "elsa-package.json"));
    }

    [Fact]
    public async Task Pack_of_non_packable_project_does_not_run_manifest_generation()
    {
        // Non-packable projects (test/host projects) produce no package, and `dotnet pack` skips their build,
        // so the target assembly may not exist. The generator must not run for them instead of failing with
        // "Assembly path does not exist".
        await using var project = new SampleProjectBuilder()
            .WithLocalGeneratorPackage()
            .WithProperty("IsPackable", "false")
            .WithSource(FeatureSource);

        var pack = await project.PackWithBuildAsync("Release");

        Assert.Equal(0, pack.ExitCode);
        Assert.DoesNotContain("Assembly path does not exist", pack.CombinedOutput);
    }

    [Fact]
    public async Task Pack_no_build_reports_missing_required_manifest_clearly()
    {
        await using var project = CreateProjectWithReferencedFeatureAssembly();

        var pack = await project.PackAsync("Release");

        Assert.True(pack.ExitCode != 0, pack.CombinedOutput);
        Assert.Contains("Elsa package manifest was not found", pack.CombinedOutput);
        Assert.Contains("dotnet build", pack.CombinedOutput);
        Assert.Contains("--no-build", pack.CombinedOutput);
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

        Assert.Equal(0, pack.ExitCode);
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
