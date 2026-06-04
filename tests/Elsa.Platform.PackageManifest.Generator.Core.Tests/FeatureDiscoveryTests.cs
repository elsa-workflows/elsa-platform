using System.Globalization;
using System.Text.Json;
using Elsa.Platform.PackageManifest.Generator.Core.Generation;
using Elsa.Platform.PackageManifest.Generator.Core.Overrides;
using Elsa.Platform.PackageManifest.Generator.Core.Validation;
using Elsa.Platform.PackageManifest.Generator.Testing;
using FluentAssertions;

namespace Elsa.Platform.PackageManifest.Generator.Core.Tests;

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
using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Sample.Features;

/// <summary>Adds Entity Framework Core persistence support.</summary>
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var feature = document.RootElement.GetProperty("features")[0];
        feature.GetProperty("id").GetString().Should().Be("Sample.Elsa.Package.EntityFrameworkCore");
        feature.GetProperty("displayName").GetString().Should().Be("Entity Framework Core Persistence");
        feature.GetProperty("description").GetString().Should().Be("Adds EF Core persistence.");

        var settings = feature.GetProperty("settings").EnumerateArray().ToDictionary(x => x.GetProperty("name").GetString()!);
        settings.Keys.Should().BeEquivalentTo("BatchSize", "Code", "OptionalBatchSize", "Provider", "Ratio", "RequiredName", "SupportedItems", "SupportedMap");
        settings["Provider"].GetProperty("jsonType").GetString().Should().Be("string");
        settings["Provider"].GetProperty("defaultValue").GetString().Should().Be("Sqlite");
        settings["BatchSize"].GetProperty("validation").GetProperty("minimum").GetDecimal().Should().Be(1);
        settings["BatchSize"].GetProperty("validation").GetProperty("maximum").GetDecimal().Should().Be(100);
        settings["Code"].GetProperty("validation").GetProperty("minLength").GetInt32().Should().Be(5);
        settings["Code"].GetProperty("validation").GetProperty("maxLength").GetInt32().Should().Be(100);
        settings["RequiredName"].GetProperty("required").GetBoolean().Should().BeTrue();
        settings["SupportedItems"].GetProperty("jsonType").GetString().Should().Be("array");
        settings["SupportedMap"].GetProperty("jsonType").GetString().Should().Be("object");
        settings["Ratio"].GetProperty("defaultValue").GetDecimal().Should().Be(3.14m);
        settings["OptionalBatchSize"].GetProperty("jsonType").GetString().Should().Be("integer");
        settings["OptionalBatchSize"].GetProperty("extensions").GetProperty("nullable").GetBoolean().Should().BeTrue();
        settings["OptionalBatchSize"].GetProperty("defaultValue").GetInt32().Should().Be(42);
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);

        result.diagnostics.Items.Should().Contain(x => x.Code == "EPMGEN_SETTING_TYPE_UNSUPPORTED" && x.Severity == GenerationDiagnosticSeverity.Info);
        result.diagnostics.Items.Should().NotContain(x => x.Severity == GenerationDiagnosticSeverity.Warning || x.Severity == GenerationDiagnosticSeverity.Error);

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0].GetProperty("settings").EnumerateArray();
        settings.Should().BeEmpty();
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project, packageId: "Elsa.Expressions.CSharp", title: "Elsa.Expressions.CSharp");

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        document.RootElement.GetProperty("displayName").GetString().Should().Be("Expressions.CSharp");
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var setting = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0];

        setting.GetProperty("validation").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).Should()
            .Equal("Postgres", "SqlServer", "Sqlite");
        setting.GetProperty("extensions").TryGetProperty("enumValues", out _).Should().BeFalse();

        var ui = setting.GetProperty("ui");
        ui.GetProperty("hint").GetString().Should().Be("select-list");
        var options = ui.GetProperty("options");
        options.GetProperty("source").GetString().Should().Be("static");
        options.GetProperty("items").EnumerateArray()
            .Select(x => (Value: x.GetProperty("value").GetString(), Label: x.GetProperty("label").GetString()))
            .Should()
            .Equal(("Postgres", "Postgres"), ("SqlServer", "Sql Server"), ("Sqlite", "Sqlite"));
    }

    [Fact]
    public async Task Generate_suppresses_enum_ui_options_when_explicit_non_list_hint_is_set()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

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
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var setting = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0];

        setting.GetProperty("validation").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).Should()
            .Equal("Postgres", "SqlServer", "Sqlite");
        var ui = setting.GetProperty("ui");
        ui.GetProperty("hint").GetString().Should().Be("text");
        ui.TryGetProperty("options", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_reads_code_first_static_and_dynamic_ui_options()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Search", DisplayName = "Search")]
public sealed class SearchFeature : IShellFeature
{
    [ManifestSetting(UIHint = "select-list")]
    [ManifestUIOption("simple", Label = "Simple")]
    [ManifestUIOption("advanced", Label = "Advanced", Description = "Use advanced search.")]
    public string Mode { get; set; } = "";

    [ManifestSetting(UIHint = "select-list")]
    [ManifestUIOptionsProvider("elsa.catalog.package-source-options", DependsOn = new[] { "WorkspaceId", "TenantId", "tenantid" }, Parameters = new[] { "kind=nuget-source" })]
    public string PackageSource { get; set; } = "";

    [ManifestUIOptionsProvider("elsa.catalog.package-type-options")]
    public string PackageType { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var settings = document.RootElement.GetProperty("features")[0]
            .GetProperty("settings")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!);

        var modeOptions = settings["Mode"].GetProperty("ui").GetProperty("options");
        modeOptions.GetProperty("source").GetString().Should().Be("static");
        modeOptions.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString())
            .Should()
            .Equal("advanced", "simple");

        var providerOptions = settings["PackageSource"].GetProperty("ui").GetProperty("options");
        providerOptions.GetProperty("source").GetString().Should().Be("provider");
        providerOptions.GetProperty("provider").GetString().Should().Be("elsa.catalog.package-source-options");
        providerOptions.GetProperty("dependsOn").EnumerateArray().Select(x => x.GetString()).Should().Equal("TenantId", "WorkspaceId");
        providerOptions.GetProperty("parameters").GetProperty("kind").GetString().Should().Be("nuget-source");

        var inferredProviderUI = settings["PackageType"].GetProperty("ui");
        inferredProviderUI.GetProperty("hint").GetString().Should().Be("select-list");
        var inferredProviderOptions = inferredProviderUI.GetProperty("options");
        inferredProviderOptions.GetProperty("source").GetString().Should().Be("provider");
        inferredProviderOptions.GetProperty("provider").GetString().Should().Be("elsa.catalog.package-type-options");
        inferredProviderOptions.TryGetProperty("dependsOn", out _).Should().BeFalse();
        inferredProviderOptions.TryGetProperty("parameters", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_defaults_provider_ui_hint_for_string_setting()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

namespace Sample.Features;

[ShellFeature("Search", DisplayName = "Search")]
public sealed class SearchFeature : IShellFeature
{
    [ManifestUIOptionsProvider("elsa.catalog.package-source-options")]
    public string PackageSource { get; set; } = "";
}
""");

        var build = await project.BuildAsync();
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var ui = document.RootElement.GetProperty("features")[0]
            .GetProperty("settings")[0]
            .GetProperty("ui");

        ui.GetProperty("hint").GetString().Should().Be("select-list");
        var options = ui.GetProperty("options");
        options.GetProperty("source").GetString().Should().Be("provider");
        options.GetProperty("provider").GetString().Should().Be("elsa.catalog.package-source-options");
        options.TryGetProperty("dependsOn", out _).Should().BeFalse();
        options.TryGetProperty("parameters", out _).Should().BeFalse();
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);
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
              "provider": "elsa.catalog.package-source-options",
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
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var ui = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0]
            .GetProperty("ui");

        ui.GetProperty("hint").GetString().Should().Be("select-list");
        var options = ui.GetProperty("options");
        options.GetProperty("source").GetString().Should().Be("provider");
        options.GetProperty("provider").GetString().Should().Be("elsa.catalog.package-source-options");
        options.GetProperty("dependsOn").EnumerateArray().Select(x => x.GetString()).Should().Equal("TenantId");
        options.GetProperty("parameters").GetProperty("kind").GetString().Should().Be("nuget-source");
    }

    [Fact]
    public async Task Generate_treats_source_less_override_ui_options_as_static_items()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

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
        build.ExitCode.Should().Be(0, build.CombinedOutput);
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
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var options = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0]
            .GetProperty("ui")
            .GetProperty("options");

        options.GetProperty("source").GetString().Should().Be("static");
        options.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("value").GetString())
            .Should()
            .Equal("advanced");
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);
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
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var setting = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("settings")[0];

        setting.GetProperty("validation").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).Should()
            .Equal("Postgres", "SqlServer", "Sqlite");
        var ui = setting.GetProperty("ui");
        ui.GetProperty("hint").GetString().Should().Be("text");
        ui.TryGetProperty("options", out _).Should().BeFalse();
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
        build.ExitCode.Should().Be(0, build.CombinedOutput);
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
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var requirement = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("infrastructure")[0];

        requirement.GetProperty("id").GetString().Should().Be("message-broker");
        requirement.GetProperty("kind").GetString().Should().Be("message-broker");
        requirement.GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("rabbitmq", "azure-service-bus");
        requirement.GetProperty("configurationKeys").EnumerateArray().Select(x => x.GetString()).Should().Contain("RabbitMq:ConnectionString");
    }

    [Fact]
    public async Task Generate_discovers_infrastructure_requirements_from_manifest_hints()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

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
        build.ExitCode.Should().Be(0, build.CombinedOutput);

        var result = Generate(project);
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var requirement = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("infrastructure")[0];

        requirement.GetProperty("id").GetString().Should().Be("database");
        requirement.GetProperty("kind").GetString().Should().Be("database");
        requirement.GetProperty("optional").GetBoolean().Should().BeTrue();
        requirement.GetProperty("reason").GetString().Should().Be("Stores workflow instances.");
        requirement.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("transactions", "json");
        requirement.GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("postgres", "sql-server");
        requirement.GetProperty("configurationKeys").EnumerateArray().Select(x => x.GetString()).Should().Contain("Postgres:ConnectionString");
        requirement.GetProperty("extensions").GetProperty("tier").GetString().Should().Be("stateful");
    }

    [Fact]
    public async Task Generate_merges_override_infrastructure_with_manifest_hints_by_id()
    {
        await using var project = new SampleProjectBuilder()
            .WithSource("""
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;

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
        build.ExitCode.Should().Be(0, build.CombinedOutput);
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
          "extensions": { "source": "override", "owner": "platform" }
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
        result.diagnostics.Items.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Should().BeEmpty();

        using var document = JsonDocument.Parse(result.artifact.ManifestJson);
        var requirements = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("infrastructure")
            .EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetString()!);

        requirements.Keys.Should().BeEquivalentTo("broker", "cache");
        requirements["broker"].GetProperty("reason").GetString().Should().Be("Override source reason.");
        requirements["broker"].GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).Should().Contain("queues");
        requirements["broker"].GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("rabbitmq", "azure-service-bus");
        requirements["broker"].GetProperty("configurationKeys").EnumerateArray().Select(x => x.GetString()).Should().Contain("Messaging:Broker");
        requirements["broker"].GetProperty("extensions").GetProperty("source").GetString().Should().Be("override");
        requirements["broker"].GetProperty("extensions").GetProperty("owner").GetString().Should().Be("platform");
        requirements["cache"].GetProperty("optional").GetBoolean().Should().BeTrue();
        requirements["cache"].GetProperty("providers").EnumerateArray().Select(x => x.GetString()).Should().Contain("redis");
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
            null,
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

        result[0].Infrastructure[0].Kind.Should().Be("message-broker");
        result[0].Infrastructure[0].Providers.Should().BeEquivalentTo("rabbitmq", "azure-service-bus");
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
        build.ExitCode.Should().Be(0, build.StandardOutput + build.StandardError);
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

        document.RootElement.GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("elsa.server", "elsa.studio");
        document.RootElement.GetProperty("features")[0].GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(x => x.GetString()).Should().Equal("elsa.studio");
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
