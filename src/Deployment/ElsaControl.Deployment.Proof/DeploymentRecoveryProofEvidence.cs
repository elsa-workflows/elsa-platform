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
                Evidence = stage.Evidence
                    .Where(pair => !IsProviderIdentityOrSecret(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => Safe(pair.Value), StringComparer.Ordinal)
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

    private static bool IsProviderIdentityOrSecret(string key) =>
        key.Contains("provider", StringComparison.OrdinalIgnoreCase)
        || key.Contains("resource", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || key.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)
        || key.Contains("authorization", StringComparison.OrdinalIgnoreCase);
}
