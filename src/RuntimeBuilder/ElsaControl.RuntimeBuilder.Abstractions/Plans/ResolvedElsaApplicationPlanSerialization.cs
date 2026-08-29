using System.Text.Json;
using System.Text.Json.Serialization;

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
