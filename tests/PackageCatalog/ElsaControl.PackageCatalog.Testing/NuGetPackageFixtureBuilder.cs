using System.IO.Compression;
using System.Text;

namespace ElsaControl.PackageCatalog.Testing;

public sealed class NuGetPackageFixtureBuilder
{
    private readonly Dictionary<string, string> _entries = [];
    private string? _manifestJson;
    private string _manifestPath = "elsa-package.json";

    public NuGetPackageFixtureBuilder WithManifest(string manifestJson, string path = "elsa-package.json")
    {
        _manifestJson = manifestJson;
        _manifestPath = path;
        return this;
    }

    public NuGetPackageFixtureBuilder WithFile(string path, string contents)
    {
        _entries[path] = contents;
        return this;
    }

    public MemoryStream Build()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in _entries)
                WriteEntry(archive, entry.Key, entry.Value);

            if (_manifestJson is not null)
                WriteEntry(archive, _manifestPath, _manifestJson);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string contents)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(contents);
    }
}
