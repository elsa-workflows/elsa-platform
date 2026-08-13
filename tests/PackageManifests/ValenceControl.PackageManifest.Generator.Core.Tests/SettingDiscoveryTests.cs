using System.Text.Json;
using ValenceControl.PackageManifest.Generator.Core.Generation;
using ValenceControl.PackageManifest.Generator.Core.Validation;
using ValenceControl.PackageManifest.Generator.Testing;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class SettingDiscoveryTests
{
    [Fact]
    public async Task Generate_excludes_direct_action_and_service_factory_hooks()
    {
        var result = await GenerateAsync(CShellsFeatureFixtures.DelegateHooksFeatureSource);

        Assert.Contains("Endpoint", result.Settings.Keys);
        Assert.DoesNotContain("Configure", result.Settings.Keys);
        Assert.DoesNotContain("ServiceFactory", result.Settings.Keys);
    }

    [Fact]
    public async Task Generate_excludes_http_client_configuration_hooks()
    {
        var result = await GenerateAsync(CShellsFeatureFixtures.DelegateHooksFeatureSource);

        Assert.DoesNotContain("ConfigureHttpClient", result.Settings.Keys);
    }

    [Fact]
    public async Task Generate_excludes_delegate_valued_dictionary_and_list_hooks()
    {
        var result = await GenerateAsync("""
#nullable enable
using System;
using System.Collections.Generic;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("DelegateCollections")]
public sealed class DelegateCollectionsFeature : IShellFeature
{
    public string Name { get; set; } = "";
    public List<Func<IServiceProvider, object>> Factories { get; set; } = [];
    public Dictionary<string, Func<IServiceProvider, object>> FactoryMap { get; set; } = [];
}
""");

        Assert.Equal("Name", Assert.Single(result.Settings.Keys));
    }

    [Fact]
    public async Task Generate_treats_non_nullable_boolean_settings_as_optional_with_false_default()
    {
        var result = await GenerateAsync("""
#nullable enable
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("BooleanSettings")]
public sealed class BooleanSettingsFeature : IShellFeature
{
    public bool Enabled { get; set; }

    [DefaultValue(true)]
    public bool StartEnabled { get; set; }

    [Required]
    public bool ExplicitlyRequired { get; set; }

    [Required]
    [DefaultValue(false)]
    public bool RequiredWithFalseDefault { get; set; }

    public bool? OptionalFlag { get; set; }
}
""");

        Assert.False(result.Settings["Enabled"].GetProperty("required").GetBoolean());
        Assert.False(result.Settings["Enabled"].GetProperty("defaultValue").GetBoolean());
        Assert.False(result.Settings["StartEnabled"].GetProperty("required").GetBoolean());
        Assert.True(result.Settings["StartEnabled"].GetProperty("defaultValue").GetBoolean());
        Assert.True(result.Settings["ExplicitlyRequired"].GetProperty("required").GetBoolean());
        Assert.False(result.Settings["ExplicitlyRequired"].TryGetProperty("defaultValue", out _));
        Assert.True(result.Settings["RequiredWithFalseDefault"].GetProperty("required").GetBoolean());
        Assert.False(result.Settings["RequiredWithFalseDefault"].GetProperty("defaultValue").GetBoolean());
        Assert.False(result.Settings["OptionalFlag"].GetProperty("required").GetBoolean());
        Assert.False(result.Settings["OptionalFlag"].TryGetProperty("defaultValue", out _));
    }

    private static async Task<GeneratedSettingsResult> GenerateAsync(string source, string diagnosticsVerbosity = "concise")
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

        using var document = JsonDocument.Parse(artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0]
            .GetProperty("settings")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!, x => x.Clone());

        return new GeneratedSettingsResult(settings, diagnostics);
    }

    private sealed record GeneratedSettingsResult(IReadOnlyDictionary<string, JsonElement> Settings, GenerationDiagnostics Diagnostics);
}
