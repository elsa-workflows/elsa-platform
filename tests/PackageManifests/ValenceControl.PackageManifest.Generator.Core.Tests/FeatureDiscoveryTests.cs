using System.Globalization;
using System.Text.Json;
using ValenceControl.PackageManifest.Generator.Core.Generation;
using ValenceControl.PackageManifest.Generator.Core.Overrides;
using ValenceControl.PackageManifest.Generator.Core.Validation;
using ValenceControl.PackageManifest.Generator.Testing;

namespace ValenceControl.PackageManifest.Generator.Core.Tests;

public sealed class FeatureDiscoveryTests
{
    [Fact]
    public async Task Generate_discovers_cshells_feature_and_configurable_settings_without_runtime_execution()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

/// <summary>Adds Entity Framework Core persistence support.</summary>
[ManifestFeatureCategory("Persistence")]
[ManifestFeatureCategory("Data")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature("EntityFrameworkCore", DisplayName = "Entity Framework Core Persistence", Description = "Adds EF Core persistence.")]
public sealed class EntityFrameworkCoreFeature : IShellFeature
{
    /// <summary>The database provider name.</summary>
    [ManifestSetting(DisplayName = "Provider", Category = "Persistence", DefaultValue = "Sqlite")]
    public string? Provider { get; set; }

    [Range(1, 100)]
    public int BatchSize { get; set; }

    public string RequiredName { get; set; } = "";

    [StringLength(100, MinimumLength = 5)]
    public string? Code { get; set; }

    public List<string> SupportedItems { get; set; } = [];

    public Dictionary<string, int> SupportedMap { get; set; } = [];

    [ManifestSetting(DefaultValue = "3.14")]
    public decimal Ratio { get; set; }

    [ManifestSetting(DefaultValue = "42")]
    public int? OptionalBatchSize { get; set; }

    [ManifestIgnore]
    public string Ignored { get; set; } = "";

    public static string StaticSetting { get; set; } = "";

    public string ReadOnlySetting => "computed";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var feature = document.RootElement.GetProperty("features")[0];
        Assert.Equal("Sample.Elsa.Package.EntityFrameworkCore", feature.GetProperty("id").GetString());
        Assert.Equal("Entity Framework Core Persistence", feature.GetProperty("displayName").GetString());
        Assert.Equal("Adds EF Core persistence.", feature.GetProperty("description").GetString());
        Assert.Equal("Persistence", feature.GetProperty("category").GetString());
        Assert.Equal(["Persistence", "Data"], feature.GetProperty("categories").EnumerateArray().Select(x => x.GetString()));

        var settings = feature.GetProperty("settings").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.Equal(new[] { "BatchSize", "Code", "OptionalBatchSize", "Provider", "Ratio", "RequiredName", "SupportedItems", "SupportedMap" }.Order(), settings.Keys.Order());

        Assert.Equal("string", settings["Provider"].GetProperty("jsonType").GetString());
        Assert.Equal("Sqlite", settings["Provider"].GetProperty("defaultValue").GetString());
        Assert.Equal(1, settings["BatchSize"].GetProperty("validation").GetProperty("minimum").GetDecimal());
        Assert.Equal(100, settings["BatchSize"].GetProperty("validation").GetProperty("maximum").GetDecimal());
        Assert.Equal(5, settings["Code"].GetProperty("validation").GetProperty("minLength").GetInt32());
        Assert.Equal(100, settings["Code"].GetProperty("validation").GetProperty("maxLength").GetInt32());
        Assert.True(settings["RequiredName"].GetProperty("required").GetBoolean());
        Assert.Equal("array", settings["SupportedItems"].GetProperty("jsonType").GetString());
        Assert.Equal("object", settings["SupportedMap"].GetProperty("jsonType").GetString());
        Assert.Equal(3.14m, settings["Ratio"].GetProperty("defaultValue").GetDecimal());
        Assert.Equal("integer", settings["OptionalBatchSize"].GetProperty("jsonType").GetString());
        Assert.True(settings["OptionalBatchSize"].GetProperty("extensions").GetProperty("nullable").GetBoolean());
        Assert.Equal(42, settings["OptionalBatchSize"].GetProperty("defaultValue").GetInt32());
    }

    [Fact]
    public async Task Generate_omits_unsupported_complex_setting_types()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
#nullable enable
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Complex", DisplayName = "Complex Feature", Description = "Complex feature.")]
public sealed class ComplexFeature : IShellFeature
{
    public ComplexOptions Options { get; set; } = new();
}

public sealed class ComplexOptions
{
    public string Value { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);

        Assert.Contains(result.diagnostics.Items, x => x.Code == "EPMGEN_SETTING_TYPE_UNSUPPORTED" && x.Severity == GenerationDiagnosticSeverity.Info);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Warning || x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray();
        Assert.Empty(settings);
    }

    [Fact]
    public async Task Generate_uses_prefixless_package_display_name_when_title_matches_package_id()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("CSharp")]
public sealed class CSharpFeature : IShellFeature
{
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project, packageId: "Elsa.Expressions.CSharp", title: "Elsa.Expressions.CSharp");

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        Assert.Equal("Expressions.CSharp", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Generate_emits_enum_setting_as_select_list_ui_with_static_options()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Storage", DisplayName = "Storage")]
public sealed class StorageFeature : IShellFeature
{
    public StorageProvider Provider { get; set; }
}

public enum StorageProvider
{
    Sqlite,
    SqlServer,
    Postgres
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var setting = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0];

        Assert.Equal(new[] { "Postgres", "SqlServer", "Sqlite" }, setting.GetProperty("validation").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.False(setting.GetProperty("extensions").TryGetProperty("enumValues", out _));

        var ui = setting.GetProperty("ui");
        Assert.Equal("select-list", ui.GetProperty("hint").GetString());
        var options = ui.GetProperty("options");
        Assert.Equal("static", options.GetProperty("source").GetString());
        Assert.Equal(
            new[] { ("Postgres", "Postgres"), ("SqlServer", "Sql Server"), ("Sqlite", "Sqlite") },
            options.GetProperty("items").EnumerateArray()
                .Select(x => (x.GetProperty("value").GetString()!, x.GetProperty("label").GetString()!)));
    }

    [Fact]
    public async Task Generate_suppresses_enum_ui_options_when_explicit_non_list_hint_is_set()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Storage", DisplayName = "Storage")]
public sealed class StorageFeature : IShellFeature
{
    [ManifestSetting(UIHint = "text")]
    public StorageProvider Provider { get; set; }
}

public enum StorageProvider
{
    Sqlite,
    SqlServer,
    Postgres
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var setting = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0];

        Assert.Equal(new[] { "Postgres", "SqlServer", "Sqlite" }, setting.GetProperty("validation").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        var ui = setting.GetProperty("ui");
        Assert.Equal("text", ui.GetProperty("hint").GetString());
        Assert.False(ui.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task Generate_reads_code_first_static_and_dynamic_ui_options()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Search", DisplayName = "Search")]
public sealed class SearchFeature : IShellFeature
{
    [ManifestSetting(UIHint = "select-list")]
    [ManifestUIOption("simple", Label = "Simple")]
    [ManifestUIOption("advanced", Label = "Advanced", Description = "Use advanced search.")]
    public string Mode { get; set; } = "";

    [ManifestSetting(UIHint = "select-list")]
    [ManifestUIOptionsProvider("valence.control.catalog.package-source-options", DependsOn = new[] { "WorkspaceId", "TenantId", "tenantid" }, Parameters = new[] { "kind=nuget-source" })]
    public string PackageSource { get; set; } = "";

    [ManifestUIOptionsProvider("valence.control.catalog.package-type-options")]
    public string PackageType { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0]
            .GetProperty("settings")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!);

        var modeOptions = settings["Mode"].GetProperty("ui").GetProperty("options");
        Assert.Equal("static", modeOptions.GetProperty("source").GetString());
        Assert.Equal(new[] { "advanced", "simple" }, modeOptions.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString()));

        var providerOptions = settings["PackageSource"].GetProperty("ui").GetProperty("options");
        Assert.Equal("provider", providerOptions.GetProperty("source").GetString());
        Assert.Equal("valence.control.catalog.package-source-options", providerOptions.GetProperty("provider").GetString());
        Assert.Equal(["TenantId", "WorkspaceId"], providerOptions.GetProperty("dependsOn").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("nuget-source", providerOptions.GetProperty("parameters").GetProperty("kind").GetString());

        var inferredProviderUI = settings["PackageType"].GetProperty("ui");
        Assert.Equal("select-list", inferredProviderUI.GetProperty("hint").GetString());
        var inferredProviderOptions = inferredProviderUI.GetProperty("options");
        Assert.Equal("provider", inferredProviderOptions.GetProperty("source").GetString());
        Assert.Equal("valence.control.catalog.package-type-options", inferredProviderOptions.GetProperty("provider").GetString());
        Assert.False(inferredProviderOptions.TryGetProperty("dependsOn", out _));
        Assert.False(inferredProviderOptions.TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task Generate_defaults_provider_ui_hint_for_string_setting()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Search", DisplayName = "Search")]
public sealed class SearchFeature : IShellFeature
{
    [ManifestUIOptionsProvider("valence.control.catalog.package-source-options")]
    public string PackageSource { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var ui = document.RootElement.GetProperty("features")[0]
            .GetProperty("settings")[0]
            .GetProperty("ui");

        Assert.Equal("select-list", ui.GetProperty("hint").GetString());
        var options = ui.GetProperty("options");
        Assert.Equal("provider", options.GetProperty("source").GetString());
        Assert.Equal("valence.control.catalog.package-source-options", options.GetProperty("provider").GetString());
        Assert.False(options.TryGetProperty("dependsOn", out _));
        Assert.False(options.TryGetProperty("parameters", out _));
    }

    [Fact]
    public async Task Generate_applies_override_ui_options()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Search", DisplayName = "Search")]
public sealed class SearchFeature : IShellFeature
{
    public string PackageSource { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "features": [
    {
      "id": "Sample.Elsa.Package.Search",
      "settings": [
        {
          "name": "PackageSource",
          "ui": {
            "hint": "select-list",
            "options": {
              "source": "provider",
              "provider": "valence.control.catalog.package-source-options",
              "dependsOn": ["TenantId"],
              "parameters": { "kind": "nuget-source" }
            }
          }
        }
      ]
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var ui = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0]
            .GetProperty("ui");

        Assert.Equal("select-list", ui.GetProperty("hint").GetString());
        var options = ui.GetProperty("options");
        Assert.Equal("provider", options.GetProperty("source").GetString());
        Assert.Equal("valence.control.catalog.package-source-options", options.GetProperty("provider").GetString());
        Assert.Equal("TenantId", Assert.Single(options.GetProperty("dependsOn").EnumerateArray().Select(x => x.GetString())));
        Assert.Equal("nuget-source", options.GetProperty("parameters").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Generate_treats_source_less_override_ui_options_as_static_items()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Search", DisplayName = "Search")]
public sealed class SearchFeature : IShellFeature
{
    [ManifestSetting(UIHint = "select-list")]
    [ManifestUIOption("simple", Label = "Simple")]
    public string Mode { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "features": [
    {
      "id": "Sample.Elsa.Package.Search",
      "settings": [
        {
          "name": "Mode",
          "ui": {
            "options": {
              "items": [
                { "value": "advanced", "label": "Advanced" }
              ]
            }
          }
        }
      ]
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var options = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0]
            .GetProperty("ui")
            .GetProperty("options");

        Assert.Equal("static", options.GetProperty("source").GetString());
        Assert.Equal(new[] { "advanced" }, options.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString()));
    }

    [Fact]
    public async Task Generate_clears_enum_ui_options_when_legacy_override_sets_non_list_hint()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Storage", DisplayName = "Storage")]
public sealed class StorageFeature : IShellFeature
{
    public StorageProvider Provider { get; set; }
}

public enum StorageProvider
{
    Sqlite,
    SqlServer,
    Postgres
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "features": [
    {
      "id": "Sample.Elsa.Package.Storage",
      "settings": [
        {
          "name": "Provider",
          "uiHint": "text"
        }
      ]
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var setting = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0];

        Assert.Equal(new[] { "Postgres", "SqlServer", "Sqlite" }, setting.GetProperty("validation").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        var ui = setting.GetProperty("ui");
        Assert.Equal("text", ui.GetProperty("hint").GetString());
        Assert.False(ui.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task Generate_applies_infrastructure_requirements_from_override_file()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("RabbitMq", DisplayName = "RabbitMQ Messaging")]
public sealed class RabbitMqFeature : IShellFeature
{
    public string ConnectionString { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "features": [
    {
      "id": "Sample.Elsa.Package.RabbitMq",
      "infrastructure": [
        {
          "id": "message-broker",
          "kind": "message-broker",
          "reason": "RabbitMQ transport requires a broker.",
          "providers": ["rabbitmq", "azure-service-bus"],
          "configurationKeys": ["RabbitMq:ConnectionString"]
        }
      ]
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var requirement = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("infrastructure")[0];

        Assert.Equal("message-broker", requirement.GetProperty("id").GetString());
        Assert.Equal("message-broker", requirement.GetProperty("kind").GetString());
        Assert.Equal(new[] { "rabbitmq", "azure-service-bus" }.Order(), requirement.GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Order());

        Assert.Contains("RabbitMq:ConnectionString", requirement.GetProperty("configurationKeys").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Generate_discovers_infrastructure_requirements_from_manifest_hints()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Postgres", DisplayName = "Postgres Persistence")]
[ManifestInfrastructure(
    "database",
    "database",
    Optional = true,
    Reason = "Stores workflow instances.",
    Capabilities = new[] { "transactions", "json" },
    Providers = new[] { "postgres", "sql-server" },
    ConfigurationKeys = new[] { "Postgres:ConnectionString" },
    Extensions = new[] { "tier=stateful" })]
public sealed class PostgresFeature : IShellFeature
{
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var requirement = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("infrastructure")[0];

        Assert.Equal("database", requirement.GetProperty("id").GetString());
        Assert.Equal("database", requirement.GetProperty("kind").GetString());
        Assert.True(requirement.GetProperty("optional").GetBoolean());
        Assert.Equal("Stores workflow instances.", requirement.GetProperty("reason").GetString());
        Assert.Equal(new[] { "transactions", "json" }.Order(), requirement.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).Order());

        Assert.Equal(new[] { "postgres", "sql-server" }.Order(), requirement.GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Order());

        Assert.Contains("Postgres:ConnectionString", requirement.GetProperty("configurationKeys").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("stateful", requirement.GetProperty("extensions").GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Generate_merges_override_infrastructure_with_manifest_hints_by_id()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Messaging", DisplayName = "Messaging")]
[ManifestInfrastructure(
    "broker",
    "message-broker",
    Reason = "Default source reason.",
    Capabilities = new[] { "queues" },
    Providers = new[] { "rabbitmq" },
    ConfigurationKeys = new[] { "Messaging:Broker" },
    Extensions = new[] { "source=attribute" })]
public sealed class MessagingFeature : IShellFeature
{
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "features": [
    {
      "id": "Sample.Elsa.Package.Messaging",
      "infrastructure": [
        {
          "id": "broker",
          "kind": "message-broker",
          "reason": "Override source reason.",
          "providers": ["azure-service-bus"],
          "extensions": { "source": "override", "owner": "control" }
        },
        {
          "id": "cache",
          "kind": "cache",
          "optional": true,
          "providers": ["redis"]
        }
      ]
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var requirements = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("infrastructure")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetString()!);

        Assert.Equal(new[] { "broker", "cache" }.Order(), requirements.Keys.Order());

        Assert.Equal("Override source reason.", requirements["broker"].GetProperty("reason").GetString());
        Assert.Contains("queues", requirements["broker"].GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "rabbitmq", "azure-service-bus" }.Order(), requirements["broker"].GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Order());

        Assert.Contains("Messaging:Broker", requirements["broker"].GetProperty("configurationKeys").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("override", requirements["broker"].GetProperty("extensions").GetProperty("source").GetString());
        Assert.Equal("control", requirements["broker"].GetProperty("extensions").GetProperty("owner").GetString());
        Assert.True(requirements["cache"].GetProperty("optional").GetBoolean());
        Assert.Contains("redis", requirements["cache"].GetProperty("providers").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void ApplyOverrides_preserves_infrastructure_kind_when_override_kind_is_empty()
    {
        var feature = new DiscoveredFeature(
            "Sample.Elsa.Package.Messaging",
            "Messaging",
            "Sample.Features.MessagingFeature",
            "Messaging",
            null,
            [],
            FeatureDiscoverySource.IShellFeature,
            true,
            false,
            false,
            false,
            false,
            [],
            [],
            [],
            [new ManifestInfrastructureRequirementReference("broker", "message-broker", false, null, [], ["rabbitmq"], [], new Dictionary<string, object?>())],
            null,
            new Dictionary<string, object?>(),
            []);
        var manifestOverride = new ManifestOverride
        {
            Features =
            [
                new FeatureOverride
                {
                    Id = feature.FeatureId,
                    Infrastructure =
                    [
                        new InfrastructureRequirementOverride
                        {
                            Id = "broker",
                            Kind = "",
                            Providers = ["azure-service-bus"]
                        }
                    ]
                }
            ]
        };

        var result = new ManifestMetadataMerger().ApplyOverrides([feature], manifestOverride);

        Assert.Equal("message-broker", result[0].Infrastructure[0].Kind);
        Assert.Equal(new[] { "rabbitmq", "azure-service-bus" }.Order(), result[0].Infrastructure[0].Providers.Order());

    }

    [Fact]
    public async Task Generate_applies_runtime_kind_compatibility_from_override_file()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("StudioWidget", DisplayName = "Studio Widget")]
public sealed class StudioWidgetFeature : IShellFeature
{
}
""");
        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "package": {
    "compatibility": {
      "runtimeKinds": [ "elsa.server", "elsa.studio" ]
    }
  },
  "features": [
    {
      "id": "Sample.Elsa.Package.StudioWidget",
      "compatibility": {
        "runtimeKinds": [ "elsa.studio" ]
      }
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        using var document = JsonDocument.Parse(result.artifact.ManifestJson);

        Assert.Equal(new[] { "elsa.server", "elsa.studio" }.Order(), document.RootElement.GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString()).Order());

        Assert.Equal("elsa.studio", Assert.Single(document.RootElement.GetProperty("features")[0].GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString())));
    }

    [Fact]
    public async Task Generate_applies_runtime_kind_compatibility_from_assembly_and_feature_attributes()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

[assembly: ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[assembly: ManifestRuntimeKind("acme.custom-host")]

namespace Sample.Features;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ShellFeature("StudioWidget", DisplayName = "Studio Widget")]
public sealed class StudioWidgetFeature : IShellFeature
{
}
""");
        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        using var document = JsonDocument.Parse(result.artifact.ManifestJson);

        Assert.Equal(new[] { "elsa.server", "acme.custom-host" }.Order(), document.RootElement.GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString()).Order());

        Assert.Equal("elsa.studio", Assert.Single(document.RootElement.GetProperty("features")[0].GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString())));
    }

    [Fact]
    public async Task Generate_uses_override_runtime_kinds_over_attribute_runtime_kinds()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using ValenceControl.PackageManifest.Generator.Hints;

[assembly: ManifestRuntimeKind("elsa.server")]

namespace Sample.Features;

[ManifestRuntimeKind("elsa.studio")]
[ShellFeature("StudioWidget", DisplayName = "Studio Widget")]
public sealed class StudioWidgetFeature : IShellFeature
{
}
""");
        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);
        var overridePath = Path.Combine(project.ProjectDirectory, "elsa-package.overrides.json");
        await File.WriteAllTextAsync(overridePath, """
{
  "package": {
    "compatibility": {
      "runtimeKinds": [ "elsa.studio" ]
    }
  },
  "features": [
    {
      "id": "Sample.Elsa.Package.StudioWidget",
      "compatibility": {
        "runtimeKinds": [ "elsa.server" ]
      }
    }
  ]
}
""");

        var result = Generate(project, overridePath);
        using var document = JsonDocument.Parse(result.artifact.ManifestJson);

        Assert.Equal("elsa.studio", Assert.Single(document.RootElement.GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString())));
        Assert.Equal("elsa.server", Assert.Single(document.RootElement.GetProperty("features")[0].GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString())));
    }

    [Fact]
    public async Task Generate_emits_bare_cshells_feature_name_for_dependencies()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;

namespace Sample.Features;

[ShellFeature("JintEngine", DisplayName = "Jint Engine")]
public sealed class JintEngineFeature : IShellFeature
{
}

[ShellFeature("JavaScript", DisplayName = "JavaScript", DependsOn = new[] { typeof(JintEngineFeature) })]
public sealed class JavaScriptFeature : IShellFeature
{
}
""");

        var build = await project.BuildAsync();
        Assert.Equal(0, build.ExitCode);

        var result = Generate(project);
        Assert.DoesNotContain(result.diagnostics.Items, x => x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var javaScriptFeature = document.RootElement.GetProperty("features")
            .EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "Sample.Elsa.Package.JavaScript");

        var dependency = javaScriptFeature.GetProperty("dependencies").EnumerateArray().Single();

        // The dependency references the bare CShells feature name that the runtime resolver keys on,
        // not the package-qualified feature id.
        Assert.Equal("JintEngine", dependency.GetProperty("featureId").GetString());
        Assert.False(dependency.TryGetProperty("packageId", out _));
    }

    private static (GeneratedManifestArtifact artifact, GenerationDiagnostics diagnostics) Generate(
        SampleProjectBuilder project,
        string? overridePath = null,
        string packageId = "Sample.Elsa.Package",
        string title = "Sample")
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
        var diagnostics = new GenerationDiagnostics();
        var generator = new ManifestGenerator();
        try
        {
            var artifact = generator.Generate(
                new GeneratorOptions(true, Path.Combine(project.ProjectDirectory, "obj", "elsa-package.json"), true, "elsa-package.json", overridePath, "Error", false, false, false, "concise", []),
                ProjectPackageMetadataMapper.Map(packageId, "1.2.3", title, "Sample package.", "Elsa", null, null, "elsa", null, null, "net10.0", null),
                new AssemblyInspectionInput(project.AssemblyPath, project.XmlDocumentationPath, "net10.0", [], true),
                diagnostics);

            return (artifact, diagnostics);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
