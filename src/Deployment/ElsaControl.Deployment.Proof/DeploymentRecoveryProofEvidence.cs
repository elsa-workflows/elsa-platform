using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Serializes the provider-neutral recovery proof without provider resource identifiers,
/// secret values, or arbitrary provider metadata.
/// </summary>
public static class DeploymentRecoveryProofEvidence
{
    public static string Serialize(DeploymentRecoveryProofReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var safePoint = report.RecoveryPoint;
        var safeReport = new
        {
            report.Outcome,
            report.Passed,
            report.CutoverEligible,
            RpoAge = report.RpoAge.ToString("c", CultureInfo.InvariantCulture),
            Rto = report.Rto.ToString("c", CultureInfo.InvariantCulture),
            RecoveryPoint = new
            {
                OrganizationId = SafeIdentity(safePoint.OrganizationId),
                WorkspaceId = SafeIdentity(safePoint.WorkspaceId),
                SourceInstanceId = SafeIdentity(safePoint.SourceInstanceId),
                RecoveryPointId = SafeIdentity(safePoint.RecoveryPointId),
                safePoint.CapturedAt,
                safePoint.SourceQuiescedAt,
                safePoint.RestorePointAt,
                SourceLifecycle = SafeIdentity(safePoint.SourceLifecycle),
                ManifestDigest = SafeDigest(safePoint.ManifestDigest),
                DesiredRevisionId = SafeIdentity(safePoint.DesiredRevisionId),
                DesiredRevisionHash = SafeDigest(safePoint.DesiredRevisionHash),
                ResolvedPlanReference = SafeReference(safePoint.ResolvedPlanReference),
                ResolvedPlanDigest = SafeDigest(safePoint.ResolvedPlanDigest),
                Artifacts = safePoint.Artifacts.Where(artifact =>
                    artifact is not null &&
                    DeploymentRecoveryProofContract.IsSafeReference(artifact.Reference) &&
                    DeploymentRecoveryProofContract.IsStrictSha256Digest(artifact.Digest)).Select(artifact => new
                {
                    Reference = SafeReference(artifact.Reference),
                    Digest = SafeDigest(artifact.Digest)
                }).ToArray(),
                ProviderSnapshotReference = SafeReference(safePoint.ProviderSnapshotReference),
                ProviderSnapshotDigest = SafeDigest(safePoint.ProviderSnapshotDigest),
                SecretReferenceKeys = safePoint.RequiredSecretReferenceKeys
                    .Where(DeploymentRecoveryProofContract.IsSafeSecretReferenceKey)
                    .Select(Safe)
                    .ToArray()
            },
            Target = report.Target is null
                ? null
                : new { InstanceId = SafeIdentity(report.Target.InstanceId) },
            Stages = report.Stages.Select(stage => new
            {
                stage.Stage,
                stage.Status,
                Code = Safe(stage.Code),
                Message = Safe(stage.Message),
                stage.StartedAt,
                stage.CompletedAt,
                Evidence = SanitizeStageEvidence(stage.Evidence)
            }).ToArray()
        };

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return JsonSerializer.Serialize(safeReport, options) + Environment.NewLine;
    }

    private static string Safe(string? value) =>
        DeploymentProofEvidence.SanitizeMessage(value ?? string.Empty);

    private static string SafeIdentity(string? value) =>
        DeploymentRecoveryProofContract.IsSafeIdentity(value) ? Safe(value) : string.Empty;

    private static string SafeDigest(string? value) =>
        DeploymentRecoveryProofContract.IsStrictSha256Digest(value) ? value! : string.Empty;

    private static string SafeReference(string? value) =>
        DeploymentRecoveryProofContract.IsSafeReference(value) ? Safe(value) : string.Empty;

    internal static IReadOnlyDictionary<string, string> SanitizeStageEvidence(IReadOnlyDictionary<string, string> evidence)
    {
        var safe = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in evidence)
        {
            var value = pair.Key switch
            {
                "sourceInstanceId" or "organizationId" or "workspaceId" or "recoveryPointId" or
                    "sourceLifecycle" or "desiredRevisionId" or "targetInstanceId" => SafeIdentity(pair.Value),
                "manifestDigest" or "desiredRevisionHash" or "resolvedPlanDigest" or
                    "providerSnapshotDigest" => SafeDigest(pair.Value),
                "resolvedPlanReference" or "providerSnapshotReference" => SafeReference(pair.Value),
                "artifactCount" or "secretReferenceKeyCount" when
                    int.TryParse(pair.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 0 =>
                    count.ToString(CultureInfo.InvariantCulture),
                "rpoAge" when TimeSpan.TryParseExact(pair.Value, "c", CultureInfo.InvariantCulture, out var duration) &&
                    duration >= TimeSpan.Zero => duration.ToString("c", CultureInfo.InvariantCulture),
                "valid" or "healthy" or "succeeded" or "eligible" when bool.TryParse(pair.Value, out var boolean) =>
                    boolean.ToString().ToLowerInvariant(),
                _ => string.Empty
            };
            if (value.Length > 0)
                safe[pair.Key] = value;
        }

        return safe;
    }
}
