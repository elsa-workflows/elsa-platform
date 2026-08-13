using System.Globalization;
using System.Reflection;
using ValenceControl.PackageManifest.Generator.Core.AssemblyInspection;

namespace ValenceControl.PackageManifest.Generator.Core.Generation;

public sealed class SettingDefaultValueResolver
{
    public ResolvedSettingDefaultValue Resolve(PropertyInfo property, string? hintDefaultValue)
    {
        if (!string.IsNullOrWhiteSpace(hintDefaultValue))
            return new ResolvedSettingDefaultValue(ConvertString(hintDefaultValue, property.PropertyType), false);

        var defaultValueAttribute = property.GetCustomAttributesData()
            .FirstOrDefault(x => x.AttributeType.FullName == "System.ComponentModel.DefaultValueAttribute");

        if (defaultValueAttribute?.ConstructorArguments.Count > 0)
            return new ResolvedSettingDefaultValue(defaultValueAttribute.ConstructorArguments[0].Value, false);

        if (TypeMetadataHelpers.IsNonNullableBoolean(property.PropertyType))
            return new ResolvedSettingDefaultValue(false, true);

        return new ResolvedSettingDefaultValue(null, false);
    }

    private static object? ConvertString(string value, Type targetType)
    {
        var type = TypeMetadataHelpers.GetNonNullableType(targetType);

        if (IsClrType(type, "System.String"))
            return value;
        if (IsClrType(type, "System.Boolean") && bool.TryParse(value, out var b))
            return b;
        if (IsClrType(type, "System.Int32") && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return i;
        if (IsClrType(type, "System.Int64") && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
            return l;
        if (IsClrType(type, "System.Double") && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        if (IsClrType(type, "System.Decimal") && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var m))
            return m;

        return value;
    }

    private static bool IsClrType(Type type, string fullName) => string.Equals(type.FullName, fullName, StringComparison.Ordinal);
}

public sealed record ResolvedSettingDefaultValue(object? Value, bool Inferred);
