using System.Text.Json;
using ValenceControl.PackageManifests;
using Json.Schema;

namespace ValenceControl.PackageManifest.Generator.Core.Overrides;

public sealed class ManifestOverrideReader
{
    public const long MaxOverrideBytes = 262_144;
    private static readonly Lazy<JsonSchema> OverrideSchema = new(() => JsonSchema.FromText(ReadSchemaJson()));

    public ManifestOverride? Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var file = new FileInfo(path);
        if (file.Length > MaxOverrideBytes)
            throw new InvalidOperationException($"Override file '{path}' exceeds the 256 KB limit.");

        var json = File.ReadAllText(path);
        ValidateSchema(json);
        return JsonSerializer.Deserialize<ManifestOverride>(json, ManifestJsonSerializerOptions.Default);
    }

    private static void ValidateSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = OverrideSchema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        if (results.IsValid)
            return;

        var errors = (results.Details ?? [])
            .Where(x => x.Errors is { Count: > 0 })
            .SelectMany(x => x.Errors!.Select(error => $"{x.InstanceLocation}: {error.Value}"))
            .DefaultIfEmpty("Override file does not match the override schema.");

        throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    private static string ReadSchemaJson()
    {
        var assembly = typeof(ManifestOverrideReader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(x => x.EndsWith("elsa-package.overrides.schema.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException("Embedded override schema was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
