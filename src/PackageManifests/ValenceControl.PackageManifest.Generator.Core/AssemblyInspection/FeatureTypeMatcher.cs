using System.Reflection;

namespace ValenceControl.PackageManifest.Generator.Core.AssemblyInspection;

public sealed class FeatureTypeMatcher(IReadOnlyList<string>? additionalFeatureInterfaceTypes = null)
{
    public const string ShellFeatureInterfaceName = "CShells.Features.IShellFeature";

    private readonly HashSet<string> _featureInterfaceNames = new(
        [ShellFeatureInterfaceName, .. additionalFeatureInterfaceTypes ?? []],
        StringComparer.Ordinal);

    public bool IsFeature(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false, IsPublic: true } &&
        !HasIgnoreAttribute(type) &&
        ImplementsFeatureInterface(type);

    public bool ImplementsFeatureInterface(Type type) =>
        type.GetInterfaces().Any(i => i.FullName is not null && _featureInterfaceNames.Contains(i.FullName));

    public static string ResolveFeatureName(Type type)
    {
        var attribute = GetShellFeatureAttribute(type);
        var name = GetConstructorString(attribute) ?? ReadNamedString(attribute, "Name");
        return string.IsNullOrWhiteSpace(name) ? StripFeatureSuffix(type.Name) : name!;
    }

    public static string StripFeatureSuffix(string typeName)
    {
        foreach (var suffix in new[] { "ShellFeature", "Feature" })
            if (typeName.EndsWith(suffix, StringComparison.Ordinal) && typeName.Length > suffix.Length)
                return typeName[..^suffix.Length];

        return typeName;
    }

    public static CustomAttributeData? GetShellFeatureAttribute(MemberInfo member) =>
        member.GetCustomAttributesData().FirstOrDefault(x => x.AttributeType.FullName == "CShells.Features.ShellFeatureAttribute");

    public static bool HasIgnoreAttribute(MemberInfo member) =>
        member.GetCustomAttributesData().Any(x => x.AttributeType.FullName == "ValenceControl.PackageManifest.Generator.Hints.ManifestIgnoreAttribute");

    public static string? ReadNamedString(CustomAttributeData? attribute, string propertyName)
    {
        var argument = attribute?.NamedArguments.FirstOrDefault(x => x.MemberName == propertyName);
        return argument?.TypedValue.Value as string;
    }

    public static bool ReadNamedBool(CustomAttributeData? attribute, string propertyName)
    {
        var argument = attribute?.NamedArguments.FirstOrDefault(x => x.MemberName == propertyName);
        return argument?.TypedValue.Value as bool? ?? false;
    }

    public static object? ReadNamedValue(CustomAttributeData? attribute, string propertyName)
    {
        var argument = attribute?.NamedArguments.FirstOrDefault(x => x.MemberName == propertyName);
        return argument?.TypedValue.Value;
    }

    public static IReadOnlyList<string> ReadNamedStringArray(CustomAttributeData? attribute, string propertyName)
    {
        var argument = attribute?.NamedArguments.FirstOrDefault(x => x.MemberName == propertyName);
        if (argument is null)
            return [];

        return ReadAttributeArray(argument.Value.TypedValue)
            .OfType<string>()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? GetConstructorString(CustomAttributeData? attribute)
    {
        if (attribute is null || attribute.ConstructorArguments.Count == 0)
            return null;

        return attribute.ConstructorArguments[0].Value as string;
    }

    public static IReadOnlyList<string> ReadDependsOn(CustomAttributeData? attribute)
    {
        var argument = attribute?.NamedArguments.FirstOrDefault(x => x.MemberName == "DependsOn");
        if (argument is null)
            return [];

        return ReadAttributeArray(argument.Value.TypedValue)
            .Select(x => x switch
            {
                Type type => ResolveFeatureName(type),
                string value => value,
                _ => null
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<object?> ReadAttributeArray(CustomAttributeTypedArgument typedArgument)
    {
        if (typedArgument.Value is not IReadOnlyCollection<CustomAttributeTypedArgument> values)
            yield break;

        foreach (var value in values)
            yield return value.Value;
    }
}
