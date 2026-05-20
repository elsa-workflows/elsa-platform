using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Elsa.Platform.Deployment.Manifest;

public sealed class ManifestReader : IManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ManifestParseResult Read(string text, ManifestFormat format)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Failed("Manifest text is empty.");

        try
        {
            var root = format switch
            {
                ManifestFormat.Json => JsonNode.Parse(text),
                ManifestFormat.Yaml => ConvertYaml(text),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };

            if (root is not JsonObject document)
                return Failed("Manifest root must be an object.");

            var resourcesNode = document["resources"]?.DeepClone() as JsonObject;
            var manifest = document.Deserialize<EnvironmentManifest>(JsonOptions);
            if (manifest is null)
                return Failed("Manifest could not be deserialized.");

            manifest = manifest with
            {
                Resources = manifest.Resources with
                {
                    Extensions = ReadExtensions(resourcesNode)
                }
            };

            return new ManifestParseResult(manifest, []);
        }
        catch (Exception ex) when (ex is JsonException or YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            return Failed(ex.Message);
        }
    }

    private static ManifestParseResult Failed(string message) =>
        new(null, [new DeploymentDiagnostic(ManifestDiagnosticCodes.Parse, DeploymentDiagnosticSeverity.Error, message)]);

    private static IReadOnlyDictionary<string, JsonNode?> ReadExtensions(JsonObject? resources)
    {
        if (resources is null)
            return ManifestEmpty.JsonNodeDictionary;

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "workflows",
            "variables",
            "features",
            "packages",
            "recipes"
        };
        return resources
            .Where(x => !known.Contains(x.Key))
            .ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private static JsonNode? ConvertYaml(string text)
    {
        using var reader = new StringReader(text);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return yaml.Documents.Count == 0 ? null : ConvertYamlNode(yaml.Documents[0].RootNode);
    }

    private static JsonNode? ConvertYamlNode(YamlNode node)
    {
        return node switch
        {
            YamlMappingNode mapping => ConvertMapping(mapping),
            YamlSequenceNode sequence => ConvertSequence(sequence),
            YamlScalarNode scalar => ConvertScalar(scalar),
            _ => JsonValue.Create(node.ToString())
        };
    }

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var item in mapping.Children)
            result[GetMappingKey(item.Key)] = ConvertYamlNode(item.Value);
        return result;
    }

    private static string GetMappingKey(YamlNode key) =>
        key is YamlScalarNode scalar ? scalar.Value ?? string.Empty : key.ToString();

    private static JsonArray ConvertSequence(YamlSequenceNode sequence)
    {
        var result = new JsonArray();
        foreach (var item in sequence)
            result.Add(ConvertYamlNode(item));
        return result;
    }

    private static JsonNode? ConvertScalar(YamlScalarNode scalar)
    {
        var text = scalar.Value;
        if (text is null)
            return null;

        if (scalar.Style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded)
            return JsonValue.Create(text);

        if (bool.TryParse(text, out var boolean))
            return JsonValue.Create(boolean);

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);

        return JsonValue.Create(text);
    }
}
