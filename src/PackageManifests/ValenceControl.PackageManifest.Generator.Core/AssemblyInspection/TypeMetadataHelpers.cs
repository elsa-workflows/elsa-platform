namespace ValenceControl.PackageManifest.Generator.Core.AssemblyInspection;

internal static class TypeMetadataHelpers
{
    public static Type GetNonNullableType(Type type) =>
        IsNullableValueType(type) ? type.GetGenericArguments()[0] : type;

    public static bool IsNullableValueType(Type type) =>
        type is { IsValueType: true, IsGenericType: true } &&
        string.Equals(type.GetGenericTypeDefinition().FullName, "System.Nullable`1", StringComparison.Ordinal);

    public static bool IsNonNullableBoolean(Type type) =>
        string.Equals(type.FullName, "System.Boolean", StringComparison.Ordinal);

    public static bool IsDelegateOrContainsDelegate(Type type)
    {
        var visited = new HashSet<Type>();
        return IsDelegateOrContainsDelegate(type, visited);
    }

    private static bool IsDelegateOrContainsDelegate(Type type, ISet<Type> visited)
    {
        type = GetNonNullableType(type);
        if (!visited.Add(type))
            return false;

        if (IsDelegate(type))
            return true;

        if (type.IsArray)
            return IsDelegateOrContainsDelegate(type.GetElementType()!, visited);

        if (!type.IsGenericType)
            return false;

        return type.GetGenericArguments().Any(argument => IsDelegateOrContainsDelegate(argument, visited));
    }

    private static bool IsDelegate(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (string.Equals(current.FullName, "System.MulticastDelegate", StringComparison.Ordinal) ||
                string.Equals(current.FullName, "System.Delegate", StringComparison.Ordinal))
                return true;

        return false;
    }
}
