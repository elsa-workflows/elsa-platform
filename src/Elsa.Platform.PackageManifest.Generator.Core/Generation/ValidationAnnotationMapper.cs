using System.Globalization;
using System.Reflection;

namespace Elsa.Platform.PackageManifest.Generator.Core.Generation;

public sealed class ValidationAnnotationMapper
{
    public IReadOnlyDictionary<string, object?> Map(PropertyInfo property)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in property.GetCustomAttributesData())
        {
            switch (attribute.AttributeType.FullName)
            {
                case "System.ComponentModel.DataAnnotations.RequiredAttribute":
                    values["required"] = true;
                    break;
                case "System.ComponentModel.DataAnnotations.RangeAttribute":
                    AddRange(values, attribute);
                    break;
                case "System.ComponentModel.DataAnnotations.StringLengthAttribute":
                    AddFirstConstructorArgument(values, attribute, "maxLength");
                    AddNamedArgument(values, attribute, "MinimumLength", "minLength", x => x is int length && length > 0);
                    break;
                case "System.ComponentModel.DataAnnotations.MinLengthAttribute":
                    AddFirstConstructorArgument(values, attribute, "minLength");
                    break;
                case "System.ComponentModel.DataAnnotations.MaxLengthAttribute":
                    AddFirstConstructorArgument(values, attribute, "maxLength");
                    break;
                case "System.ComponentModel.DataAnnotations.RegularExpressionAttribute":
                    AddFirstConstructorArgument(values, attribute, "pattern");
                    break;
            }
        }

        return values;
    }

    private static void AddRange(IDictionary<string, object?> values, CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count < 2)
            return;

        var offset = attribute.ConstructorArguments.Count >= 3 && attribute.ConstructorArguments[0].ArgumentType.FullName == "System.Type" ? 1 : 0;
        if (attribute.ConstructorArguments.Count <= offset + 1)
            return;

        values["minimum"] = ConvertAttributeValue(attribute.ConstructorArguments[offset].Value);
        values["maximum"] = ConvertAttributeValue(attribute.ConstructorArguments[offset + 1].Value);
    }

    private static void AddFirstConstructorArgument(IDictionary<string, object?> values, CustomAttributeData attribute, string key)
    {
        if (attribute.ConstructorArguments.Count > 0)
            values[key] = ConvertAttributeValue(attribute.ConstructorArguments[0].Value);
    }

    private static void AddNamedArgument(
        IDictionary<string, object?> values,
        CustomAttributeData attribute,
        string memberName,
        string key,
        Func<object?, bool>? predicate = null)
    {
        var argument = attribute.NamedArguments.FirstOrDefault(x => x.MemberName == memberName);
        if (argument.MemberName is null)
            return;

        var value = ConvertAttributeValue(argument.TypedValue.Value);
        if (predicate is null || predicate(value))
            values[key] = value;
    }

    private static object? ConvertAttributeValue(object? value) => value switch
    {
        null => null,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
        _ => value
    };
}
