using ValenceControl.PackageManifest.Generator.Core.Generation;
using ValenceControl.PackageManifest.Generator.Core.Validation;
using ValenceControl.PackageManifest.Generator.Testing;
using System.Text.Json;

namespace ValenceControl.PackageManifest.Generator.IntegrationTests;

public sealed class ValidationSeverityBuildTests
{
    [Fact]
    public async Task Default_policy_succeeds_for_omitted_non_delegate_unsupported_setting()
    {
        await using var project = new SampleProjectBuilder().WithSource("""
#nullable enable
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Complex", Description = "Complex feature.")]
public sealed class ComplexFeature : IShellFeature
{
    public string Name { get; set; } = "";

    public ComplexOptions Options { get; set; } = new();
}

public sealed class ComplexOptions
{
    public string Value { get; set; } = "";
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

        Assert.Contains(diagnostics.Items, x => x.Code == "EPMGEN_SETTING_TYPE_UNSUPPORTED" && x.Severity == GenerationDiagnosticSeverity.Info);
        Assert.False(new ValidationSeverityPolicy("Error", false).ShouldFail(diagnostics));

        using var document = JsonDocument.Parse(artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray();
        Assert.Equal("Name", Assert.Single(settings.Select(x => x.GetProperty("name").GetString())));
    }
}
