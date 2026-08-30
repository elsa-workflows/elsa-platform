using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>
/// Inputs to provider-neutral instance resolution. The release-manifest admission
/// result is supplied by the trust boundary; the resolver never accepts a raw payload.
/// </summary>
public sealed record ElsaInstancePlanResolutionRequest(
    ElsaInstanceIntent InstanceIntent,
    RuntimeBuilderIntent BuilderIntent,
    ReleaseManifestAdmissionResult ReleaseManifest,
    string PlanId,
    string PlanUri,
    Guid? WorkspaceId = null,
    IReadOnlyList<ResolvedPlanEvidence>? ExistingEvidence = null);

/// <summary>Safe, typed resolver diagnostic. Messages are fixed and value-free.</summary>
public sealed record ElsaInstancePlanResolutionFinding(
    string Severity,
    string Code,
    string Message,
    string Scope)
{
    public static ElsaInstancePlanResolutionFinding Error(string code, string message, string scope) =>
        new("error", code, message, scope);

    public static ElsaInstancePlanResolutionFinding Warning(string code, string message, string scope) =>
        new("warning", code, message, scope);
}

/// <summary>
/// An immutable resolved plan and the exact projections needed by an instance.
/// </summary>
public sealed record ElsaInstancePlanResolutionResult(
    bool Succeeded,
    ResolvedElsaApplicationPlan? Plan,
    ElsaResolvedPlanReference? Reference,
    ElsaCurrentResolvedRelease? CurrentResolvedRelease,
    IReadOnlyList<ElsaInstancePlanResolutionFinding> Findings)
{
    public static ElsaInstancePlanResolutionResult Failed(IReadOnlyList<ElsaInstancePlanResolutionFinding> findings) =>
        new(false, null, null, null, findings);
}
