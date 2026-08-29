using System.Text.Json;
using System.Text.Json.Serialization;
using System.Buffers;

namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>
/// JSON boundary for a resolved plan. Callers serialize the normalized copy so equivalent
/// plans cannot produce different desired-state bytes merely because input collections differed
/// in order.
/// </summary>
public static class ResolvedElsaApplicationPlanSerialization
{
    public static string Serialize(ResolvedElsaApplicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(plan.Normalize(), Options);
    }

    public static ResolvedElsaApplicationPlan Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Plan JSON is required.", nameof(json));

        return JsonSerializer.Deserialize<ResolvedElsaApplicationPlan>(json, Options)
            ?? throw new JsonException("Plan JSON did not contain a resolved application plan.");
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal static class ResolvedPlanJsonCanonicalizer
{
    public static JsonElement Canonicalize(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(value, writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(item, writer);
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: false);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new JsonException($"Unsupported JSON value kind {value.ValueKind}.");
        }
    }
}
