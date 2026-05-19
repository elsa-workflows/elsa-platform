using System.IO.Compression;

namespace Elsa.Platform.PackageManifest.Generator.Testing;

public static class NuGetPackageInspector
{
    public static IReadOnlyList<string> ListEntries(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.Select(x => x.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> FindEntries(string packagePath, string entryName) =>
        ListEntries(packagePath).Where(x => string.Equals(x, entryName, StringComparison.Ordinal)).ToArray();

    public static void AssertSingleEntry(string packagePath, string entryName)
    {
        var entries = FindEntries(packagePath, entryName);
        if (entries.Count != 1)
            throw new InvalidOperationException($"Expected exactly one package entry '{entryName}', but found {entries.Count}.");
    }

    public static string ReadEntry(string packagePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.GetEntry(entryName) ?? throw new FileNotFoundException($"Package entry '{entryName}' was not found.", entryName);
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
