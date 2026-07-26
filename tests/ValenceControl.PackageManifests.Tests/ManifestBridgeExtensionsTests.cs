using System.Text.Json;
using ValenceControl.PackageManifests;
using FluentAssertions;

namespace ValenceControl.PackageManifests.Tests;

public sealed class ManifestBridgeExtensionsTests
{
    private static FeatureSettingManifest DeserializeSetting(string json) =>
        JsonSerializer.Deserialize<FeatureSettingManifest>(json, ManifestJsonSerializerOptions.Default)!;

    private static FeatureManifest DeserializeFeature(string json) =>
        JsonSerializer.Deserialize<FeatureManifest>(json, ManifestJsonSerializerOptions.Default)!;

    [Fact]
    public void Value_accessors_read_boxed_json_elements_from_bags()
    {
        var setting = DeserializeSetting("""
        {
          "name": "smtp",
          "jsonType": "string",
          "ui": { "group": "Mail", "advanced": true, "experimental": false }
        }
        """);

        setting.UI.GetString("group").Should().Be("Mail");
        setting.UI.GetBool("advanced").Should().BeTrue();
        setting.UI.GetBool("experimental").Should().BeFalse();
        setting.UI.GetElement("group").Should().NotBeNull();
    }

    [Fact]
    public void Value_accessors_return_defaults_for_missing_or_wrong_kind_keys()
    {
        var setting = DeserializeSetting("""
        {
          "name": "smtp",
          "jsonType": "string",
          "ui": { "hint": 42, "advanced": "yes" }
        }
        """);

        setting.UI.GetElement("missing").Should().BeNull();
        setting.UI.GetString("missing").Should().BeNull();
        setting.UI.GetString("hint").Should().BeNull();     // number, not a string
        setting.UI.GetBool("missing").Should().BeFalse();
        setting.UI.GetBool("advanced").Should().BeFalse();  // string "yes" is not the JSON literal true
    }

    [Fact]
    public void GetJsonString_reads_string_properties_from_object_elements_only()
    {
        var setting = DeserializeSetting("""
        {
          "name": "smtp",
          "jsonType": "string",
          "ui": { "obj": { "label": "Hello", "count": 3 }, "arr": [1, 2] }
        }
        """);

        var obj = setting.UI.GetElement("obj")!.Value;
        obj.GetJsonString("label").Should().Be("Hello");
        obj.GetJsonString("count").Should().BeNull();   // not a string
        obj.GetJsonString("missing").Should().BeNull();

        var arr = setting.UI.GetElement("arr")!.Value;
        arr.GetJsonString("label").Should().BeNull();   // not an object
    }

    [Fact]
    public void GetNormalizedDependencyIds_strips_same_package_prefix_case_insensitively_and_dedups()
    {
        var feature = DeserializeFeature("""
        {
          "id": "jint",
          "typeName": "T",
          "displayName": "Jint",
          "dependencies": [
            { "featureId": "Elsa.JavaScript.JintEngine" },
            { "featureId": "elsa.javascript.JintEngine" },
            { "featureId": "SomeOther.Feature" },
            { "featureId": "" },
            { "featureId": null }
          ]
        }
        """);

        feature.GetNormalizedDependencyIds("Elsa.JavaScript")
            .Should().Equal("JintEngine", "SomeOther.Feature");
    }

    [Fact]
    public void GetNormalizedDependencyIds_returns_ids_unchanged_when_package_id_is_blank()
    {
        var feature = DeserializeFeature("""
        {
          "id": "jint",
          "typeName": "T",
          "displayName": "Jint",
          "dependencies": [
            { "featureId": "Elsa.JavaScript.JintEngine" },
            { "featureId": "Other" }
          ]
        }
        """);

        feature.GetNormalizedDependencyIds(null)
            .Should().Equal("Elsa.JavaScript.JintEngine", "Other");
        feature.GetNormalizedDependencyIds("   ")
            .Should().Equal("Elsa.JavaScript.JintEngine", "Other");
    }

    [Fact]
    public void GetSettingOptions_maps_nested_ui_options_items()
    {
        var setting = DeserializeSetting("""
        {
          "name": "level",
          "jsonType": "string",
          "ui": { "options": { "items": [
            { "label": "Low", "value": "low", "description": "Quiet" },
            "high"
          ] } }
        }
        """);

        var (options, provider) = setting.GetSettingOptions();

        provider.Should().BeNull();
        options.Should().HaveCount(2);
        options[0].Should().BeEquivalentTo(new { Label = "Low", Description = "Quiet" });
        options[0].Value!.Value.GetString().Should().Be("low");
        options[1].Label.Should().Be("high");
        options[1].Value!.Value.GetString().Should().Be("high");
        options[1].Description.Should().BeNull();
    }

    [Fact]
    public void GetSettingOptions_returns_provider_when_source_is_provider()
    {
        var setting = DeserializeSetting("""
        {
          "name": "level",
          "jsonType": "string",
          "ui": { "options": { "source": "provider", "provider": "LogLevels" } }
        }
        """);

        var (options, provider) = setting.GetSettingOptions();

        options.Should().BeEmpty();
        provider.Should().Be("LogLevels");
    }

    [Fact]
    public void GetSettingOptions_accepts_flat_ui_options_array()
    {
        var setting = DeserializeSetting("""
        {
          "name": "level",
          "jsonType": "string",
          "ui": { "options": ["a", "b"] }
        }
        """);

        var (options, provider) = setting.GetSettingOptions();

        provider.Should().BeNull();
        options.Select(o => o.Label).Should().Equal("a", "b");
    }

    [Fact]
    public void GetSettingOptions_falls_back_to_validation_enum()
    {
        var setting = DeserializeSetting("""
        {
          "name": "level",
          "jsonType": "string",
          "validation": { "enum": ["x", "y", "z"] }
        }
        """);

        var (options, provider) = setting.GetSettingOptions();

        provider.Should().BeNull();
        options.Select(o => o.Label).Should().Equal("x", "y", "z");
    }

    [Fact]
    public void GetSettingOptions_returns_empty_when_no_options_present()
    {
        var setting = DeserializeSetting("""
        {
          "name": "level",
          "jsonType": "string"
        }
        """);

        var (options, provider) = setting.GetSettingOptions();

        options.Should().BeEmpty();
        provider.Should().BeNull();
    }
}
