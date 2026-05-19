using System.Reflection;

namespace Elsa.PackageManifest.Generator.Core.AssemblyInspection;

public sealed class NullableMetadataReader
{
    public bool IsNullable(PropertyInfo property)
    {
        var type = property.PropertyType;
        if (!type.IsValueType)
            return ReadNullableState(property) switch
            {
                1 => false,
                2 => true,
                _ => true
            };

        return TypeMetadataHelpers.IsNullableValueType(type);
    }

    private static byte? ReadNullableState(PropertyInfo property)
    {
        var propertyState = ReadNullableAttribute(property.GetCustomAttributesData());
        if (propertyState is not null)
            return propertyState;

        var contextState = ReadNullableContextAttribute(property.GetCustomAttributesData())
            ?? ReadNullableContextAttribute(property.DeclaringType?.GetCustomAttributesData() ?? [])
            ?? ReadNullableContextAttribute(property.DeclaringType?.Module.GetCustomAttributesData() ?? []);

        return contextState;
    }

    private static byte? ReadNullableAttribute(IEnumerable<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.FullName != "System.Runtime.CompilerServices.NullableAttribute")
                continue;

            if (attribute.ConstructorArguments.Count == 0)
                continue;

            var value = attribute.ConstructorArguments[0].Value;
            if (value is byte b)
                return b;

            if (value is IReadOnlyCollection<CustomAttributeTypedArgument> values && values.FirstOrDefault().Value is byte first)
                return first;
        }

        return null;
    }

    private static byte? ReadNullableContextAttribute(IEnumerable<CustomAttributeData> attributes)
    {
        var attribute = attributes.FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
        if (attribute?.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is byte b)
            return b;

        return null;
    }
}
