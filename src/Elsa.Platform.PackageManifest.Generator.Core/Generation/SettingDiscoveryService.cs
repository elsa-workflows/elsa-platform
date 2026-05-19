using System.Reflection;
using Elsa.Platform.PackageManifest.Generator.Core.AssemblyInspection;
using Elsa.Platform.PackageManifest.Generator.Core.SchemaGeneration;
using Elsa.Platform.PackageManifest.Generator.Core.Validation;

namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

public sealed class SettingDiscoveryService(
    FeatureMetadataReader metadataReader,
    NullableMetadataReader nullableMetadataReader,
    ValidationAnnotationMapper validationAnnotationMapper,
    SettingDefaultValueResolver defaultValueResolver,
    SettingSchemaGenerator schemaGenerator,
    GenerationDiagnostics? diagnostics = null,
    bool verboseDiagnostics = false)
{
    public IReadOnlyList<DiscoveredSetting> Discover(Type featureType, string featureId, string featureName)
    {
        var settings = new List<DiscoveredSetting>();
        foreach (var property in featureType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(IsConfigurableProperty))
        {
            if (IsIgnoredCodeHook(property, featureId))
                continue;

            var setting = TryCreateSetting(property, featureId, featureName);
            if (setting is not null)
                settings.Add(setting);
        }

        return settings
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsConfigurableProperty(PropertyInfo property) =>
        property.GetIndexParameters().Length == 0 &&
        property.GetSetMethod() is { IsPublic: true } &&
        !FeatureTypeMatcher.HasIgnoreAttribute(property);

    private bool IsIgnoredCodeHook(PropertyInfo property, string featureId)
    {
        if (!TypeMetadataHelpers.IsDelegateOrContainsDelegate(property.PropertyType))
            return false;

        if (verboseDiagnostics)
        {
            diagnostics?.Verbose(
                "EPMGEN_SETTING_CODE_HOOK_IGNORED",
                $"Setting candidate '{featureId}.{property.Name}' was ignored because it is a code configuration hook.",
                property.PropertyType.FullName ?? property.PropertyType.Name);
        }

        return true;
    }

    private DiscoveredSetting? TryCreateSetting(PropertyInfo property, string featureId, string featureName)
    {
        var hint = metadataReader.ReadSettingMetadata(property);
        var nullable = nullableMetadataReader.IsNullable(property);
        var validation = new Dictionary<string, object?>(validationAnnotationMapper.Map(property), StringComparer.OrdinalIgnoreCase);
        var schema = schemaGenerator.Generate(property.PropertyType, nullable, validation);
        if (IsUnsupportedSchema(schema))
        {
            ReportUnsupportedSetting(property, featureId);
            return null;
        }

        var explicitRequired = hint.Required || validation.ContainsKey("required");
        var resolvedDefaultValue = defaultValueResolver.Resolve(property, hint.DefaultValue);
        var defaultValue = resolvedDefaultValue.Value;
        if (explicitRequired && resolvedDefaultValue.Inferred && TypeMetadataHelpers.IsNonNullableBoolean(property.PropertyType))
            defaultValue = null;

        var required = explicitRequired || (!nullable && defaultValue is null);
        var enumValues = property.PropertyType.IsEnum
            ? Enum.GetNames(property.PropertyType).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (enumValues.Length > 0)
            validation["enum"] = enumValues;

        var displayName = hint.DisplayName ?? NamingHelpers.ToDisplayName(property.Name);
        var uiHint = hint.UIHint ?? (enumValues.Length > 0 || hint.UIOptions.Count > 0 || hint.UIOptionsProvider is not null ? "select-list" : null);
        var uiOptions = hint.UIOptions.Count > 0
            ? hint.UIOptions
            : ShouldUseEnumUIOptions(enumValues, uiHint, hint.UIOptionsProvider)
                ? enumValues.Select(x => new ManifestUIOptionReference(x, NamingHelpers.ToDisplayName(x), null)).ToArray()
                : [];

        return new DiscoveredSetting(
            featureId,
            property.Name,
            property.Name,
            property.PropertyType.FullName ?? property.PropertyType.Name,
            schema.JsonType,
            $"{featureName}:{property.Name}",
            required,
            nullable,
            defaultValue,
            displayName,
            hint.Description,
            hint.Category,
            hint.Group,
            validation,
            enumValues,
            hint.Secret,
            hint.Sensitive,
            hint.RestartRequired,
            uiHint,
            uiOptions,
            hint.UIOptionsProvider,
            hint.Advanced,
            hint.Experimental,
            hint.Extensions);
    }

    private void ReportUnsupportedSetting(PropertyInfo property, string featureId)
    {
        var clrType = property.PropertyType.FullName ?? property.PropertyType.Name;
        diagnostics?.Add(UnsupportedTypeDiagnosticFactory.Create(featureId, property.Name, clrType));
    }

    private static bool ShouldUseEnumUIOptions(
        IReadOnlyCollection<string> enumValues,
        string? uiHint,
        ManifestUIOptionsProviderReference? uiOptionsProvider) =>
        enumValues.Count > 0 &&
        uiOptionsProvider is null &&
        IsOptionsUIHint(uiHint);

    private static bool IsOptionsUIHint(string? uiHint) =>
        string.Equals(uiHint, "select-list", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uiHint, "multi-select-list", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uiHint, "radio-list", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsupportedSchema(SettingSchemaResult schema) =>
        string.Equals(schema.JsonType, "unsupported", StringComparison.OrdinalIgnoreCase);
}
