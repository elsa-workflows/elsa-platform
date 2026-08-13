using System.Text.Json;
using ValenceControl.PackageManifests;

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

        Assert.Equal("Mail", setting.UI.GetString("group"));
        Assert.True(setting.UI.GetBool("advanced"));
        Assert.False(setting.UI.GetBool("experimental"));
        Assert.NotNull(setting.UI.GetElement("group"));
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

        Assert.Null(setting.UI.GetElement("missing"));
        Assert.Null(setting.UI.GetString("missing"));
        Assert.Null(setting.UI.GetString("hint"));     // number, not a string
        Assert.False(setting.UI.GetBool("missing"));
        Assert.False(setting.UI.GetBool("advanced"));  // string "yes" is not the JSON literal true
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
        Assert.Equal("Hello", obj.GetJsonString("label"));
        Assert.Null(obj.GetJsonString("count"));   // not a string
        Assert.Null(obj.GetJsonString("missing"));

        var arr = setting.UI.GetElement("arr")!.Value;
        Assert.Null(arr.GetJsonString("label"));   // not an object
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

        Assert.Equal(["JintEngine", "SomeOther.Feature"],
            feature.GetNormalizedDependencyIds("Elsa.JavaScript"));
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

        Assert.Equal(["Elsa.JavaScript.JintEngine", "Other"], feature.GetNormalizedDependencyIds(null));
        Assert.Equal(["Elsa.JavaScript.JintEngine", "Other"], feature.GetNormalizedDependencyIds("   "));
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

        Assert.Null(provider);
        Assert.Equal(2, options.Count());
        Assert.Equal("Low", options[0].Label);
        Assert.Equal("Quiet", options[0].Description);
        Assert.Equal("low", options[0].Value!.Value.GetString());
        Assert.Equal("high", options[1].Label);
        Assert.Equal("high", options[1].Value!.Value.GetString());
        Assert.Null(options[1].Description);
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

        Assert.Empty(options);
        Assert.Equal("LogLevels", provider);
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

        Assert.Null(provider);
        Assert.Equal(["a", "b"], options.Select(o => o.Label));
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

        Assert.Null(provider);
        Assert.Equal(["x", "y", "z"], options.Select(o => o.Label));
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

        Assert.Empty(options);
        Assert.Null(provider);
    }
}
