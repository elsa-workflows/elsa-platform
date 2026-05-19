using System.Reflection;

namespace Elsa.PackageManifest.Generator.Core.AssemblyInspection;

public sealed class AssemblyMetadataReader
{
    public T Read<T>(string assemblyPath, IReadOnlyList<string> referenceAssemblyPaths, Func<Assembly, T> read)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("Assembly path is required.", nameof(assemblyPath));

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException("Assembly path does not exist.", assemblyPath);

        var resolverPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddTrustedPlatformAssemblies(resolverPaths);
        AddExisting(resolverPaths, referenceAssemblyPaths);
        AddAssemblyPath(resolverPaths, assemblyPath);

        var resolver = new PathAssemblyResolver(resolverPaths.Values);
        using var context = new MetadataLoadContext(resolver);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        return read(assembly);
    }

    private static void AddTrustedPlatformAssemblies(IDictionary<string, string> paths)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
            return;

        foreach (var path in tpa.Split(Path.PathSeparator))
            if (File.Exists(path))
                AddAssemblyPath(paths, path);
    }

    private static void AddExisting(IDictionary<string, string> paths, IEnumerable<string> values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
                AddAssemblyPath(paths, value);
    }

    private static void AddAssemblyPath(IDictionary<string, string> paths, string path)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(path).Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
                paths[assemblyName] = path;
        }
        catch (BadImageFormatException)
        {
        }
    }
}
