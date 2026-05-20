using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using System.Globalization;
using YamlDotNet.Serialization;

namespace Elsa.Platform.Deployment.Manifest;

public sealed class ManifestReader : IManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithAttemptingUnquotedStringTypeDeserialization()
        .Build();

    public ManifestParseResult Read(string text, ManifestFormat format)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Failed("Manifest text is empty.");

        try
        {
            var root = format switch
            {
                ManifestFormat.Json => JsonNode.Parse(text),
                ManifestFormat.Yaml => ConvertYaml(YamlDeserializer.Deserialize<object?>(text)),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };

            if (root is not JsonObject document)
                return Failed("Manifest root must be an object.");

            var resourcesNode = document["resources"]?.DeepClone() as JsonObject;
            var manifest = document.Deserialize<EnvironmentManifest>(JsonOptions);
            if (manifest is null)
                return Failed("Manifest could not be deserialized.");

            manifest = Normalize(manifest);
            manifest = manifest with
            {
                Resources = manifest.Resources with
                {
                    Extensions = ReadExtensions(resourcesNode)
                }
            };

            return new ManifestParseResult(manifest, ManifestValidator.ValidateManifestHeader(manifest).ToArray());
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

    private static EnvironmentManifest Normalize(EnvironmentManifest manifest)
    {
        return manifest with
        {
            Resources = manifest.Resources with
            {
                Variables = manifest.Resources.Variables
                    .Select(variable => variable with { Value = NormalizeScalarNode(variable.Value) })
                    .ToArray()
            }
        };
    }

    private static JsonNode? NormalizeScalarNode(JsonNode? node)
    {
        if (node is null)
            return null;

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return ConvertYamlScalar(text);

        return node;
    }

    private static JsonNode? ConvertYaml(object? value)
    {
        return value switch
        {
            null => null,
            IDictionary<object, object> dictionary => ConvertDictionary(dictionary),
            IDictionary<string, object> dictionary => ConvertDictionary(dictionary.ToDictionary(x => (object)x.Key, x => x.Value)),
            string text => ConvertYamlScalar(text),
            IEnumerable<object> sequence => ConvertSequence(sequence),
            bool boolean => JsonValue.Create(boolean),
            int number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            _ => JsonValue.Create(value.ToString())
        };
    }

    private static JsonObject ConvertDictionary(IDictionary<object, object> dictionary)
    {
        var result = new JsonObject();
        foreach (var item in dictionary)
            result[item.Key.ToString() ?? string.Empty] = ConvertYaml(item.Value);
        return result;
    }

    private static JsonArray ConvertSequence(IEnumerable<object> sequence)
    {
        var result = new JsonArray();
        foreach (var item in sequence)
            result.Add(ConvertYaml(item));
        return result;
    }

    private static JsonNode? ConvertYamlScalar(string text)
    {
        if (bool.TryParse(text, out var boolean))
            return JsonValue.Create(boolean);

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);

        return JsonValue.Create(text);
    }
}
