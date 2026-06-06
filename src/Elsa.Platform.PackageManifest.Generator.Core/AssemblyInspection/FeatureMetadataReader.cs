using System.Reflection;
using Elsa.Platform.PackageManifest.Generator.Core.Generation;

namespace Elsa.Platform.PackageManifest.Generator.Core.AssemblyInspection;

public sealed class FeatureMetadataReader
{
    private const string ManifestRuntimeKindAttributeName = "Elsa.Platform.PackageManifest.Generator.Hints.ManifestRuntimeKindAttribute";

    public PackageHintMetadata ReadPackageMetadata(Assembly assembly) =>
        new(ReadRuntimeKinds(assembly));

    public FeatureMetadata ReadFeatureMetadata(Type type)
    {
        var shellFeature = FeatureTypeMatcher.GetShellFeatureAttribute(type);
        var extensions = ReadExtensions(type);
        var attributeCategories = ReadFeatureCategoryAttributes(type);
        var shellFeatureCategories = ReadShellFeatureCategories(shellFeature);
        return new FeatureMetadata(
            FeatureTypeMatcher.ResolveFeatureName(type),
            FeatureTypeMatcher.ReadNamedString(shellFeature, "DisplayName"),
            FeatureTypeMatcher.ReadNamedString(shellFeature, "Description"),
            attributeCategories.Count > 0 ? attributeCategories : shellFeatureCategories,
            FeatureTypeMatcher.ReadDependsOn(shellFeature),
            ReadInfrastructure(type),
            ReadRuntimeKinds(type),
            extensions);
    }

    public SettingHintMetadata ReadSettingMetadata(PropertyInfo property)
    {
        var hint = property.GetCustomAttributesData()
            .FirstOrDefault(x => x.AttributeType.FullName == "Elsa.Platform.PackageManifest.Generator.Hints.ManifestSettingAttribute");

        return new SettingHintMetadata(
            FeatureTypeMatcher.ReadNamedString(hint, "DisplayName"),
            FeatureTypeMatcher.ReadNamedString(hint, "Description"),
            FeatureTypeMatcher.ReadNamedString(hint, "Category"),
            FeatureTypeMatcher.ReadNamedString(hint, "Group"),
            FeatureTypeMatcher.ReadNamedBool(hint, "Required"),
            FeatureTypeMatcher.ReadNamedString(hint, "DefaultValue"),
            FeatureTypeMatcher.ReadNamedString(hint, "UIHint") ?? FeatureTypeMatcher.ReadNamedString(hint, "UiHint"),
            FeatureTypeMatcher.ReadNamedBool(hint, "Secret"),
            FeatureTypeMatcher.ReadNamedBool(hint, "Sensitive"),
            FeatureTypeMatcher.ReadNamedBool(hint, "RestartRequired"),
            FeatureTypeMatcher.ReadNamedBool(hint, "Advanced"),
            FeatureTypeMatcher.ReadNamedBool(hint, "Experimental"),
            ReadUIOptions(property),
            ReadUIOptionsProvider(property),
            ReadExtensions(property));
    }

    private static IReadOnlyList<ManifestUIOptionReference> ReadUIOptions(MemberInfo member)
    {
        return member.GetCustomAttributesData()
            .Where(x => x.AttributeType.FullName == "Elsa.Platform.PackageManifest.Generator.Hints.ManifestUIOptionAttribute")
            .Select(attribute => new ManifestUIOptionReference(
                attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string ?? "" : "",
                FeatureTypeMatcher.ReadNamedString(attribute, "Label"),
                FeatureTypeMatcher.ReadNamedString(attribute, "Description")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static ManifestUIOptionsProviderReference? ReadUIOptionsProvider(MemberInfo member)
    {
        var attribute = member.GetCustomAttributesData()
            .FirstOrDefault(x => x.AttributeType.FullName == "Elsa.Platform.PackageManifest.Generator.Hints.ManifestUIOptionsProviderAttribute");
        if (attribute is null)
            return null;

        var provider = FeatureTypeMatcher.GetConstructorString(attribute);
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        return new ManifestUIOptionsProviderReference(
            provider,
            FeatureTypeMatcher.ReadNamedStringArray(attribute, "DependsOn"),
            ReadExtensionPairs(FeatureTypeMatcher.ReadNamedStringArray(attribute, "Parameters")));
    }

    private static IReadOnlyDictionary<string, object?> ReadExtensions(MemberInfo member)
    {
        return member.GetCustomAttributesData()
            .Where(x => x.AttributeType.FullName == "Elsa.Platform.PackageManifest.Generator.Hints.ManifestExtensionAttribute")
            .Select(x => new
            {
                Key = x.ConstructorArguments.Count > 0 ? x.ConstructorArguments[0].Value as string : null,
                Value = x.ConstructorArguments.Count > 1 ? x.ConstructorArguments[1].Value as string : null
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (object?)x.Last().Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadFeatureCategoryAttributes(MemberInfo member) =>
        NormalizeCategories(member.GetCustomAttributesData()
            .Where(x => x.AttributeType.FullName == "Elsa.Platform.PackageManifest.Generator.Hints.ManifestFeatureCategoryAttribute")
            .Select(x => x.ConstructorArguments.Count > 0 ? x.ConstructorArguments[0].Value as string : null));

    private static IReadOnlyList<string> ReadShellFeatureCategories(CustomAttributeData? attribute)
    {
        var categories = FeatureTypeMatcher.ReadNamedStringArray(attribute, "Categories");
        if (categories.Count > 0)
            return NormalizeCategories(categories);

        var category = FeatureTypeMatcher.ReadNamedString(attribute, "Category");
        return string.IsNullOrWhiteSpace(category) ? [] : [category.Trim()];
    }

    private static IReadOnlyList<string> NormalizeCategories(IEnumerable<string?> categories) =>
        categories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> ReadRuntimeKinds(ICustomAttributeProvider provider)
    {
        var attributes = provider switch
        {
            Assembly assembly => assembly.GetCustomAttributesData(),
            MemberInfo member => member.GetCustomAttributesData(),
            _ => []
        };

        return attributes
            .Where(x => x.AttributeType.FullName == ManifestRuntimeKindAttributeName)
            .Select(x => x.ConstructorArguments.Count > 0 ? x.ConstructorArguments[0].Value as string : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last()!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ManifestInfrastructureRequirementReference> ReadInfrastructure(MemberInfo member)
    {
        return member.GetCustomAttributesData()
            .Where(x => x.AttributeType.FullName == "Elsa.Platform.PackageManifest.Generator.Hints.ManifestInfrastructureAttribute")
            .Select(ReadInfrastructureRequirement)
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Kind))
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ManifestInfrastructureRequirementReference ReadInfrastructureRequirement(CustomAttributeData attribute)
    {
        return new ManifestInfrastructureRequirementReference(
            attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string ?? "" : "",
            attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value as string ?? "" : "",
            FeatureTypeMatcher.ReadNamedBool(attribute, "Optional"),
            FeatureTypeMatcher.ReadNamedString(attribute, "Reason"),
            FeatureTypeMatcher.ReadNamedStringArray(attribute, "Capabilities"),
            FeatureTypeMatcher.ReadNamedStringArray(attribute, "Providers"),
            FeatureTypeMatcher.ReadNamedStringArray(attribute, "ConfigurationKeys"),
            ReadExtensionPairs(FeatureTypeMatcher.ReadNamedStringArray(attribute, "Extensions")));
    }

    private static IReadOnlyDictionary<string, object?> ReadExtensionPairs(IReadOnlyList<string> values)
    {
        return values
            .Select(x =>
            {
                var separatorIndex = x.IndexOf('=', StringComparison.Ordinal);
                return new
                {
                    Key = separatorIndex > 0 ? x[..separatorIndex] : null,
                    Value = separatorIndex > 0 ? x[(separatorIndex + 1)..] : null
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => (object?)x.Last().Value, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record PackageHintMetadata(IReadOnlyList<string> RuntimeKinds);

public sealed record FeatureMetadata(
    string FeatureName,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<ManifestInfrastructureRequirementReference> Infrastructure,
    IReadOnlyList<string> RuntimeKinds,
    IReadOnlyDictionary<string, object?> Extensions);

public sealed record SettingHintMetadata(
    string? DisplayName,
    string? Description,
    string? Category,
    string? Group,
    bool Required,
    string? DefaultValue,
    string? UIHint,
    bool Secret,
    bool Sensitive,
    bool RestartRequired,
    bool Advanced,
    bool Experimental,
    IReadOnlyList<ManifestUIOptionReference> UIOptions,
    ManifestUIOptionsProviderReference? UIOptionsProvider,
    IReadOnlyDictionary<string, object?> Extensions);
