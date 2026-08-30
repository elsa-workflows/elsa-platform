namespace ElsaControl.RuntimeBuilder.Abstractions.Plans;

/// <summary>
/// Provider-neutral plan resolution boundary. Implementations may load governed
/// catalog and release-manifest data, but callers receive only the typed immutable
/// plan and safe references.
/// </summary>
public interface IElsaInstancePlanResolver
{
    Task<ElsaInstancePlanResolutionResult> ResolveAsync(
        ElsaInstancePlanResolutionRequest request,
        CancellationToken cancellationToken = default);
}
