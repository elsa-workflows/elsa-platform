using System.Reflection;
using ValenceControl.PackageManifest.Generator.Core.AssemblyInspection;

namespace ValenceControl.PackageManifest.Generator.Core.SchemaGeneration;

public sealed class SettingSchemaGenerator
{
    public SettingSchemaResult Generate(Type type, bool nullable, IReadOnlyDictionary<string, object?> validation)
    {
        var nonNullableType = TypeMetadataHelpers.GetNonNullableType(type);
        var schema = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var jsonType = GetJsonType(nonNullableType);

        schema["type"] = nullable ? new[] { jsonType, "null" } : jsonType;
        foreach (var item in validation.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            schema[item.Key] = item.Value;

        if (nonNullableType.IsEnum)
        {
            schema["type"] = nullable ? new[] { "string", "null" } : "string";
            schema["enum"] = Enum.GetNames(nonNullableType).Order(StringComparer.Ordinal).ToArray();
            return new SettingSchemaResult("string", null, schema, []);
        }

        if (IsClrType(nonNullableType, "System.TimeSpan"))
            schema["format"] = "duration";
        else if (IsClrType(nonNullableType, "System.DateTime") || IsClrType(nonNullableType, "System.DateTimeOffset"))
            schema["format"] = "date-time";
        else if (IsClrType(nonNullableType, "System.Uri"))
            schema["format"] = "uri";

        var diagnostics = jsonType == "unsupported"
            ? new[] { $"Unsupported setting type '{type.FullName}'." }
            : [];

        return new SettingSchemaResult(jsonType, schema.TryGetValue("format", out var format) ? format as string : null, schema, diagnostics);
    }

    private static string GetJsonType(Type type)
    {
        if (IsClrType(type, "System.String") || IsClrType(type, "System.Guid") || IsClrType(type, "System.TimeSpan") || IsClrType(type, "System.DateTime") || IsClrType(type, "System.DateTimeOffset") || IsClrType(type, "System.Uri"))
            return "string";
        if (IsClrType(type, "System.Boolean"))
            return "boolean";
        if (type.IsEnum)
            return "string";
        if (IsIntegral(type))
            return "integer";
        if (IsClrType(type, "System.Single") || IsClrType(type, "System.Double") || IsClrType(type, "System.Decimal"))
            return "number";
        if (ImplementsGeneric(type, typeof(IDictionary<,>)) || ImplementsGeneric(type, typeof(IReadOnlyDictionary<,>)))
            return "object";
        if (type.IsArray || ImplementsGeneric(type, typeof(IEnumerable<>)))
            return "array";

        return "unsupported";
    }

    private static bool IsIntegral(Type type) =>
        IsClrType(type, "System.Byte") || IsClrType(type, "System.SByte") || IsClrType(type, "System.Int16") || IsClrType(type, "System.UInt16") ||
        IsClrType(type, "System.Int32") || IsClrType(type, "System.UInt32") || IsClrType(type, "System.Int64") || IsClrType(type, "System.UInt64");

    private static bool IsClrType(Type type, string fullName) => string.Equals(type.FullName, fullName, StringComparison.Ordinal);

    private static bool ImplementsGeneric(Type type, Type genericType) =>
        IsGenericTypeDefinition(type, genericType) ||
        type.GetInterfaces().Any(x => IsGenericTypeDefinition(x, genericType));

    private static bool IsGenericTypeDefinition(Type type, Type genericType) =>
        type.IsGenericType &&
        string.Equals(type.GetGenericTypeDefinition().FullName, genericType.FullName, StringComparison.Ordinal);
}

public sealed record SettingSchemaResult(
    string JsonType,
    string? Format,
    IReadOnlyDictionary<string, object?> Schema,
    IReadOnlyList<string> Diagnostics);
