using System.Xml.Linq;
using Elsa.Platform.PackageManifest.Generator.Core.Generation;

namespace Elsa.Platform.PackageManifest.Generator.Core.Documentation;

public sealed class XmlDocumentationReader
{
    public IReadOnlyDictionary<string, XmlDocumentationEntry> Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new Dictionary<string, XmlDocumentationEntry>(StringComparer.Ordinal);

        var document = XDocument.Load(path);
        return document.Descendants("member")
            .Select(ReadEntry)
            .Where(x => !string.IsNullOrWhiteSpace(x.MemberName))
            .ToDictionary(x => x.MemberName, x => x, StringComparer.Ordinal);
    }

    private static XmlDocumentationEntry ReadEntry(XElement member)
    {
        var name = member.Attribute("name")?.Value ?? "";
        var summary = Normalize(member.Element("summary")?.Value);
        var remarks = Normalize(member.Element("remarks")?.Value);
        var examples = member.Elements("example")
            .Select(x => Normalize(x.Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToArray();

        return new XmlDocumentationEntry(name, summary, remarks, examples);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
