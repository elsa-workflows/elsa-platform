using System.Text.Json;
using ValenceControl.PackageManifest.Generator.Core.Generation;
using ValenceControl.PackageManifest.Generator.Core.Validation;
using ValenceControl.PackageManifest.Generator.Testing;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class MetadataInspectionSafetyTests
{
    [Fact]
    public async Task Generate_does_not_invoke_feature_constructors_or_property_getters()
    {
        await using var project = new SampleProjectBuilder().WithSource(TripwireFeatureFixtures.Source);
        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var diagnostics = new GenerationDiagnostics();
        var artifact = new ManifestGenerator().Generate(
            new GeneratorOptions(true, Path.Combine(project.ProjectDirectory, "obj", "elsa-package.json"), true, "elsa-package.json", null, "Error", false, false, false, "concise", []),
            ProjectPackageMetadataMapper.Map("Sample.Elsa.Package", "1.2.3", "Sample", "Sample package.", null, null, null, null, null, null, "net10.0", null),
            new AssemblyInspectionInput(project.AssemblyPath, project.XmlDocumentationPath, "net10.0", [], true),
            diagnostics);

        Assert.DoesNotContain(diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);
        using var document = JsonDocument.Parse(artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray().Select(x => x.GetProperty("name").GetString());
        Assert.Equal("SafeSetting", Assert.Single(settings));
    }

    [Fact]
    public async Task Generate_does_not_invoke_ignored_delegates_or_factories()
    {
        await using var project = new SampleProjectBuilder().WithSource("""
#nullable enable
using System;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("DelegateTripwire")]
public sealed class DelegateTripwireFeature : IShellFeature
{
    public DelegateTripwireFeature() => throw new InvalidOperationException("The generator must not invoke feature constructors.");

    public string SafeSetting { get; set; } = "";

    public Action Configure { get; set; } = () => throw new InvalidOperationException("The generator must not invoke delegates.");

    public Func<IServiceProvider, object> Factory { get; set; } = _ => throw new InvalidOperationException("The generator must not invoke factories.");

    public string ExplodingGetter => throw new InvalidOperationException("The generator must not read property getters.");
}
""");
        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var diagnostics = new GenerationDiagnostics();
        var artifact = new ManifestGenerator().Generate(
            new GeneratorOptions(true, Path.Combine(project.ProjectDirectory, "obj", "elsa-package.json"), true, "elsa-package.json", null, "Error", false, false, false, "concise", []),
            ProjectPackageMetadataMapper.Map("Sample.Elsa.Package", "1.2.3", "Sample", "Sample package.", null, null, null, null, null, null, "net10.0", null),
            new AssemblyInspectionInput(project.AssemblyPath, project.XmlDocumentationPath, "net10.0", [], true),
            diagnostics);

        Assert.DoesNotContain(diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);
        using var document = JsonDocument.Parse(artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray().Select(x => x.GetProperty("name").GetString());
        Assert.Equal("SafeSetting", Assert.Single(settings));
    }
}
