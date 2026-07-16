namespace Elsa.Platform.Healing.Core.OpenTelemetry;

/// <summary>Durable server-authoritative telemetry source registry.</summary>
public interface IHealingTelemetrySourceStore
{
    ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    ValueTask<HealingTelemetrySource> AddTelemetrySourceAsync(
        HealingTelemetrySource source,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<HealingTelemetrySource>> ListTelemetrySourcesAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingTelemetrySource?> GetTelemetrySourceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an active source by its opaque credential identifier. This unscoped lookup is reserved for
    /// authentication; successful callers must take all routing scope from the returned server record.
    /// </summary>
    ValueTask<HealingTelemetrySource?> GetActiveTelemetrySourceForAuthenticationAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default);

    ValueTask<HealingTelemetrySource?> RotateTelemetrySourceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        byte[] expectedVersion,
        byte[] credentialSalt,
        byte[] credentialHash,
        DateTimeOffset rotatedAt,
        CancellationToken cancellationToken = default);

    ValueTask<HealingTelemetrySource?> RevokeTelemetrySourceAsync(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        Guid sourceId,
        byte[] expectedVersion,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);
}
