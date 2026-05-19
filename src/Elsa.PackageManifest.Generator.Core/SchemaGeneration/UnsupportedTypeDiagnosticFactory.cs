using Elsa.PackageManifest.Generator.Core.Validation;

namespace Elsa.PackageManifest.Generator.Core.SchemaGeneration;

public static class UnsupportedTypeDiagnosticFactory
{
    public static GenerationDiagnostic Create(string featureId, string settingName, string clrType) =>
        new(
            "EPMGEN_SETTING_TYPE_UNSUPPORTED",
            GenerationDiagnosticSeverity.Info,
            $"Setting '{settingName}' on feature '{featureId}' was ignored because type '{clrType}' is not supported by package manifests.",
            $"{featureId}.{settingName}",
            "$.features[*].settings[*]",
            "setting.type.unsupported",
            GenerationDiagnosticCategory.SettingDiscovery);
}
