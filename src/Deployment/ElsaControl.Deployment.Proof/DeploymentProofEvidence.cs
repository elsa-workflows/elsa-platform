using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Serializes only the safe proof contract and redacts common accidental secret forms at the
/// provider boundary. Provider adapters must still return metadata, not raw credentials.
/// </summary>
public static partial class DeploymentProofEvidence
{
    public static string Serialize(DeploymentProofReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var safeReport = new
        {
            report.Outcome,
            Input = new
            {
                ElsaVersion = SanitizeScalar(report.Input.ElsaVersion),
                Topology = SanitizeScalar(report.Input.Topology),
                Features = report.Input.Features.Select(SanitizeScalar).ToArray(),
                ImageReference = SanitizeScalar(report.Input.ImageReference),
                ImageDigest = SanitizeScalar(report.Input.ImageDigest)
            },
            Environment = new
            {
                Name = SanitizeScalar(report.Environment.Name),
                Region = SanitizeScalar(report.Environment.Region),
                Provider = SanitizeScalar(report.Environment.Provider),
                SecretReferenceNames = report.Environment.SecretReferenceNames.Select(SanitizeScalar).ToArray()
            },
            Stages = report.Stages.Select(SanitizeStage).ToArray()
        };

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.Serialize(safeReport, options) + Environment.NewLine;
    }

    internal static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? evidence)
    {
        if (evidence is null || evidence.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        return evidence.ToDictionary(
            pair => pair.Key,
            pair => IsSensitiveKey(pair.Key) ? "<redacted>" : SanitizeScalar(pair.Value),
            StringComparer.Ordinal);
    }

    internal static string SanitizeMessage(string message) =>
        SanitizeScalar(message);

    private static string SanitizeScalar(string value) =>
        UserInfoRegex().Replace(SecretAssignmentRegex().Replace(value, "$1<redacted>"), "$1<redacted>@");

    private static DeploymentProofStageResult SanitizeStage(DeploymentProofStageResult stage) =>
        stage with
        {
            Code = SanitizeScalar(stage.Code),
            Message = SanitizeMessage(stage.Message),
            Evidence = Sanitize(stage.Evidence)
        };

    private static bool IsSensitiveKey(string key) =>
        key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)
        || key.Contains("authorization", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("(?<name>password|secret|token|credential|connection(?:string)?|authorization)\\s*[:=]\\s*[^,;\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(?<scheme>[a-z][a-z0-9+.-]*://)[^/@\\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserInfoRegex();
}
