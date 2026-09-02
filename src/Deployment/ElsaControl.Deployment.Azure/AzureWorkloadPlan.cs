using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Azure placement facts supplied below the provider-neutral resolved-plan boundary.
/// </summary>
public sealed record AzureWorkloadTarget(string WorkloadName, string Location);

/// <summary>
/// Deterministic, secret-safe intent consumed by the Azure Bicep lifecycle adapter.
/// It deliberately describes inputs rather than Azure resource shape.
/// </summary>
public sealed record AzureWorkloadPlan(
    string WorkloadName,
    string Location,
    string ElsaVersion,
    string ReleaseLine,
    string Topology,
    string Isolation,
    string ImageRepository,
    string ImageDigest,
    string ReleaseManifestReference,
    string ReleaseManifestDigest,
    string ReleaseManifestSignatureReference,
    string ReleaseManifestSignatureDigest,
    IReadOnlyDictionary<string, string> SecretReferences,
    string Fingerprint,
    string? SqlWorkflowPackageVersion = null,
    string? SqlQuartzPackageVersion = null);

public sealed record AzureWorkloadPlanTranslation(
    AzureWorkloadPlan? Plan,
    IReadOnlyList<ResolvedPlanValidationFinding> Findings)
{
    public bool IsAccepted => Plan is not null && Findings.Count == 0;
}
