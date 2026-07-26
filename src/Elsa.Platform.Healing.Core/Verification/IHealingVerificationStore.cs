namespace Elsa.Platform.Healing.Core.Verification;

public sealed record HealingVerificationAppendResult<T>(T Value, bool IsReplay);

public sealed record HealingVerificationScope(
    HealingIncident Incident,
    IncidentEpisode Episode,
    EnvironmentImpact EnvironmentImpact,
    HealingConfiguration Configuration,
    string RepairedRevision,
    VerificationResult? Verification);

public interface IHealingVerificationStore
{
    ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default) => operation(cancellationToken);

    ValueTask<HealingVerificationAppendResult<DeploymentObservation>> AppendDeploymentObservationAsync(
        DeploymentObservation observation,
        CancellationToken cancellationToken = default);

    ValueTask<VerificationResult> UpsertVerificationAsync(
        VerificationResult verification,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<HealingVerificationScope>> ListDeploymentScopesAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingVerificationScope?> GetScopeAsync(
        Guid workspaceId,
        Guid episodeId,
        Guid environmentId,
        string repairedRevision,
        CancellationToken cancellationToken = default);

    ValueTask<HealingVerificationScope?> GetEpisodeScopeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingVerificationScope?> FindActiveScopeAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string repairedRevision,
        string operationName,
        CancellationToken cancellationToken = default);

    ValueTask<HealingVerificationScope?> FindScopeForOccurrenceAsync(
        IncidentOccurrence occurrence,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<HealingVerificationScope>> ListDueScopesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<HealingVerificationScope>> ListExpiredWaiverScopesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<EnvironmentImpact>> ListEpisodeImpactsAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<VerificationResult>> ListEpisodeVerificationsAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        CancellationToken cancellationToken = default);

    ValueTask SaveAsync(CancellationToken cancellationToken = default);
}
