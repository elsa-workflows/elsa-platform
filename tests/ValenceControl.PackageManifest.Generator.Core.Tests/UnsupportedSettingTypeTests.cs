using ValenceControl.PackageManifest.Generator.Core.Generation;
using ValenceControl.PackageManifest.Generator.Core.Validation;
using ValenceControl.PackageManifest.Generator.Testing;
using System.Text.Json;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class UnsupportedSettingTypeTests
{
    [Fact]
    public async Task Generate_does_not_warn_for_ignored_delegate_hooks_by_default()
    {
        var (_, diagnostics) = await GenerateAsync(CShellsFeatureFixtures.DelegateHooksFeatureSource);

        Assert.DoesNotContain(diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Warning && x.Code == "EPMGEN_SETTING_CODE_HOOK_IGNORED");
        Assert.DoesNotContain(diagnostics.Items, x => x.Code == "EPMGEN_SETTING_TYPE_UNSUPPORTED" && x.Target is not null && x.Target.Contains("Action", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Generate_reports_ignored_delegate_hooks_with_verbose_diagnostics()
    {
        var (_, diagnostics) = await GenerateAsync(CShellsFeatureFixtures.DelegateHooksFeatureSource, "verbose");

        Assert.Contains(diagnostics.Items, x => x.Code == "EPMGEN_SETTING_CODE_HOOK_IGNORED" && x.Severity == GenerationDiagnosticSeverity.Info);
    }

    [Fact]
    public async Task Generate_omits_system_type_settings_with_low_importance_diagnostic()
    {
        var (artifact, diagnostics) = await GenerateAsync("""
#nullable enable
using System;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Identity", Description = "Identity feature.")]
public sealed class DefaultAuthenticationFeature : IShellFeature
{
    public string ProviderName { get; set; } = "";

    public Type? ApiKeyProviderType { get; set; }
}
""");

        Assert.Contains(diagnostics.Items, x =>
            x.Code == "EPMGEN_SETTING_TYPE_UNSUPPORTED" &&
            x.Severity == GenerationDiagnosticSeverity.Info &&
            x.Target == "Sample.Elsa.Package.Identity.ApiKeyProviderType");
        Assert.DoesNotContain(diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Warning || x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray();
        Assert.Equal("ProviderName", Assert.Single(settings.Select(x => x.GetProperty("name").GetString())));
    }

    [Fact]
    public async Task Generate_omits_non_delegate_complex_object_settings()
    {
        var (artifact, diagnostics) = await GenerateAsync("""
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

        Assert.Contains(diagnostics.Items, x => x.Code == "EPMGEN_SETTING_TYPE_UNSUPPORTED" && x.Severity == GenerationDiagnosticSeverity.Info);
        Assert.DoesNotContain(diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Warning || x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray();
        Assert.Equal("Name", Assert.Single(settings.Select(x => x.GetProperty("name").GetString())));
    }

    private static async Task<(GeneratedManifestArtifact Artifact, GenerationDiagnostics Diagnostics)> GenerateAsync(string source, string diagnosticsVerbosity = "concise")
    {
        await using var project = new SampleProjectBuilder().WithSource(source);
        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var diagnostics = new GenerationDiagnostics();
        var artifact = new ManifestGenerator().Generate(
            new GeneratorOptions(true, Path.Combine(project.ProjectDirectory, "obj", "elsa-package.json"), true, "elsa-package.json", null, "Error", false, false, false, diagnosticsVerbosity, []),
            ProjectPackageMetadataMapper.Map("Sample.Elsa.Package", "1.2.3", "Sample", "Sample package.", null, null, null, null, null, null, "net10.0", null),
            new AssemblyInspectionInput(project.AssemblyPath, project.XmlDocumentationPath, "net10.0", [], true),
            diagnostics);

        return (artifact, diagnostics);
    }
}
