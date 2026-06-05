using Elsa.Platform.PackageManifest.Generator.Core.Overrides;
using System.Text.Json;

namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

public sealed class ManifestMetadataMerger
{
    public IReadOnlyList<DiscoveredFeature> ApplyOverrides(IReadOnlyList<DiscoveredFeature> features, ManifestOverride? manifestOverride)
    {
        if (manifestOverride is null)
            return features;

        return features.Select(feature =>
        {
            var featureOverride = ManifestOverrideReferenceResolver.ResolveFeatureOverride(feature, manifestOverride);
            if (featureOverride is null)
                return feature;

            var settings = feature.Settings.Select(setting =>
            {
                var settingOverride = featureOverride.Settings?.FirstOrDefault(x => string.Equals(x.Name, setting.Name, StringComparison.OrdinalIgnoreCase));
                if (settingOverride is null)
                    return setting;

                var structuredUIHint = ResolveUIHint(settingOverride.UI);
                var uiHint = settingOverride.UIHint ?? structuredUIHint ?? setting.UIHint;
                var hasUIHintOverride = settingOverride.UIHint is not null || structuredUIHint is not null;
                return setting with
                {
                    DisplayName = settingOverride.DisplayName ?? setting.DisplayName,
                    Description = settingOverride.Description ?? setting.Description,
                    Category = settingOverride.Category ?? setting.Category,
                    Group = settingOverride.Group ?? setting.Group,
                    Required = settingOverride.Required ?? setting.Required,
                    Nullable = settingOverride.Nullable ?? setting.Nullable,
                    DefaultValue = settingOverride.DefaultValue ?? setting.DefaultValue,
                    Secret = settingOverride.Secret ?? setting.Secret,
                    Sensitive = settingOverride.Sensitive ?? setting.Sensitive,
                    RestartRequired = settingOverride.RestartRequired ?? setting.RestartRequired,
                    UIHint = uiHint,
                    UIOptions = ResolveUIOptions(setting.UIOptions, settingOverride.UI, uiHint, hasUIHintOverride),
                    UIOptionsProvider = ResolveUIOptionsProvider(setting.UIOptionsProvider, settingOverride.UI, uiHint, hasUIHintOverride),
                    Advanced = settingOverride.Advanced ?? setting.Advanced,
                    Experimental = settingOverride.Experimental ?? setting.Experimental,
                    ExtensionMetadata = Merge(setting.ExtensionMetadata, settingOverride.Extensions)
                };
            }).ToArray();

            return feature with
            {
                DisplayName = featureOverride.DisplayName ?? feature.DisplayName,
                Description = featureOverride.Description ?? feature.Description,
                Category = featureOverride.Category ?? feature.Category,
                Advanced = featureOverride.Advanced ?? feature.Advanced,
                Experimental = featureOverride.Experimental ?? feature.Experimental,
                Dependencies = featureOverride.Dependencies?.Select(ToDependencyReference).ToArray() ?? feature.Dependencies,
                Conflicts = featureOverride.Conflicts?.Select(ToConflictReference).ToArray() ?? feature.Conflicts,
                RequiredCapabilities = featureOverride.RequiredCapabilities ?? feature.RequiredCapabilities,
                Infrastructure = MergeInfrastructure(feature.Infrastructure, featureOverride.Infrastructure),
                Compatibility = MergeCompatibility(feature.Compatibility, featureOverride.Compatibility),
                ExtensionMetadata = Merge(feature.ExtensionMetadata, featureOverride.Extensions),
                Settings = settings
            };
        }).ToArray();
    }

    private static IReadOnlyDictionary<string, object?> Merge(IReadOnlyDictionary<string, object?> first, IReadOnlyDictionary<string, object?>? second)
    {
        var result = new Dictionary<string, object?>(first, StringComparer.OrdinalIgnoreCase);
        if (second is not null)
        {
            foreach (var item in second)
                result[item.Key] = item.Value;
        }

        return result;
    }

    private static CompatibilityOverride? MergeCompatibility(CompatibilityOverride? discovered, CompatibilityOverride? overrideCompatibility)
    {
        if (discovered is null)
            return overrideCompatibility;

        if (overrideCompatibility is null)
            return discovered;

        return new CompatibilityOverride
        {
            RuntimeKinds = overrideCompatibility.RuntimeKinds ?? discovered.RuntimeKinds,
            ElsaVersionRange = overrideCompatibility.ElsaVersionRange ?? discovered.ElsaVersionRange,
            DockerImageVersionRange = overrideCompatibility.DockerImageVersionRange ?? discovered.DockerImageVersionRange,
            RuntimeCapabilities = overrideCompatibility.RuntimeCapabilities ?? discovered.RuntimeCapabilities,
            Extensions = Merge(discovered.Extensions ?? new Dictionary<string, object?>(), overrideCompatibility.Extensions)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<ManifestUIOptionReference> ResolveUIOptions(
        IReadOnlyList<ManifestUIOptionReference> current,
        JsonElement? ui,
        string? uiHint,
        bool hasUIHintOverride)
    {
        if (!TryReadOptions(ui, out var options))
            return hasUIHintOverride && !IsOptionsUIHint(uiHint) ? [] : current;

        if (!TryReadString(options, "source", out var source))
            source = "static";

        if (!string.Equals(source, "static", StringComparison.OrdinalIgnoreCase))
            return [];

        if (!TryReadProperty(options, "items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        return items
            .EnumerateArray()
            .Select(ReadUIOption)
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ResolveUIHint(JsonElement? ui)
    {
        if (ui is null || ui.Value.ValueKind != JsonValueKind.Object)
            return null;

        return TryReadString(ui.Value, "hint", out var hint) ? hint : null;
    }

    private static ManifestUIOptionsProviderReference? ResolveUIOptionsProvider(
        ManifestUIOptionsProviderReference? current,
        JsonElement? ui,
        string? uiHint,
        bool hasUIHintOverride)
    {
        if (!TryReadOptions(ui, out var options))
            return hasUIHintOverride && !IsOptionsUIHint(uiHint) ? null : current;

        if (!TryReadString(options, "source", out var source) || !string.Equals(source, "provider", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!TryReadString(options, "provider", out var provider) || string.IsNullOrWhiteSpace(provider))
            return null;

        return new ManifestUIOptionsProviderReference(
            provider,
            TryReadStringList(options, "dependsOn"),
            TryReadDictionary(options, "parameters"));
    }

    private static ManifestUIOptionReference ReadUIOption(JsonElement item)
    {
        TryReadString(item, "value", out var value);
        TryReadString(item, "label", out var label);
        TryReadString(item, "description", out var description);
        return new ManifestUIOptionReference(value ?? "", label, description);
    }

    private static bool TryReadOptions(JsonElement? ui, out JsonElement options)
    {
        options = default;
        if (ui is null || ui.Value.ValueKind != JsonValueKind.Object)
            return false;

        return TryReadProperty(ui.Value, "options", out options) && options.ValueKind == JsonValueKind.Object;
    }

    private static bool IsOptionsUIHint(string? uiHint) =>
        string.IsNullOrWhiteSpace(uiHint) ||
        string.Equals(uiHint, "select-list", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uiHint, "multi-select-list", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uiHint, "radio-list", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadProperty(JsonElement values, string key, out JsonElement result)
    {
        foreach (var property in values.EnumerateObject())
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                result = property.Value;
                return true;
            }

        result = default;
        return false;
    }

    private static bool TryReadString(JsonElement values, string key, out string? result)
    {
        result = null;
        if (!TryReadProperty(values, key, out var value) || value.ValueKind != JsonValueKind.String)
            return false;

        result = value.GetString();
        return true;
    }

    private static IReadOnlyList<string> TryReadStringList(JsonElement values, string key)
    {
        if (!TryReadProperty(values, key, out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        return items
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> TryReadDictionary(JsonElement values, string key)
    {
        if (!TryReadProperty(values, key, out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>();

        return value.EnumerateObject()
            .ToDictionary(x => x.Name, x => ToObject(x.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static object? ToObject(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => ToObject(x.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => value.EnumerateArray().Select(ToObject).ToArray(),
        _ => null
    };

    private static ManifestDependencyReference ToDependencyReference(DependencyOverride dependency) =>
        new(
            string.IsNullOrWhiteSpace(dependency.PackageId) ? null : dependency.PackageId,
            dependency.VersionRange,
            dependency.FeatureId);

    private static ManifestConflictReference ToConflictReference(ConflictOverride conflict) =>
        new(
            string.IsNullOrWhiteSpace(conflict.PackageId) ? null : conflict.PackageId,
            conflict.VersionRange,
            conflict.FeatureId,
            conflict.Reason);

    private static ManifestInfrastructureRequirementReference ToInfrastructureRequirementReference(InfrastructureRequirementOverride requirement) =>
        new(
            requirement.Id,
            requirement.Kind,
            requirement.Optional ?? false,
            requirement.Reason,
            requirement.Capabilities ?? [],
            requirement.Providers ?? [],
            requirement.ConfigurationKeys ?? [],
            requirement.Extensions ?? new Dictionary<string, object?>());

    private static IReadOnlyList<ManifestInfrastructureRequirementReference> MergeInfrastructure(
        IReadOnlyList<ManifestInfrastructureRequirementReference> first,
        IReadOnlyList<InfrastructureRequirementOverride>? second)
    {
        if (second is null || second.Count == 0)
            return first;

        var result = first.ToList();
        foreach (var requirement in second)
        {
            var index = result.FindIndex(x => string.Equals(x.Id, requirement.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                result.Add(ToInfrastructureRequirementReference(requirement));
                continue;
            }

            var existing = result[index];
            result[index] = existing with
            {
                Kind = string.IsNullOrWhiteSpace(requirement.Kind) ? existing.Kind : requirement.Kind,
                Optional = requirement.Optional ?? existing.Optional,
                Reason = requirement.Reason ?? existing.Reason,
                Capabilities = Merge(existing.Capabilities, requirement.Capabilities),
                Providers = Merge(existing.Providers, requirement.Providers),
                ConfigurationKeys = Merge(existing.ConfigurationKeys, requirement.ConfigurationKeys),
                Extensions = Merge(existing.Extensions, requirement.Extensions)
            };
        }

        return result.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> Merge(IReadOnlyList<string> first, IReadOnlyList<string>? second)
    {
        if (second is null)
            return first;

        return first
            .Concat(second)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
