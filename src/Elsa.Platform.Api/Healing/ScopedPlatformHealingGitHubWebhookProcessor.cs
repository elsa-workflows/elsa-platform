using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Healing;

public interface IPlatformHealingGitHubWebhookProcessorRunner
{
    ValueTask<string> ProcessAsync(
        ProviderConnection connection,
        string deliveryId,
        string eventName,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);

    ValueTask RecordFailureAsync(
        Guid workspaceId,
        string deliveryId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Gives every independently verified workspace its own EF scope. A failed processor therefore cannot
/// leak tracked changes or a failed transaction into the next tenant in the same provider delivery.
/// </summary>
public sealed class ScopedPlatformHealingGitHubWebhookProcessorRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IPlatformHealingGitHubWebhookProcessorRunner
{
    public async ValueTask<string> ProcessAsync(
        ProviderConnection connection,
        string deliveryId,
        string eventName,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IPlatformHealingGitHubWebhookProcessor>();
        return await processor.ProcessAsync(connection, deliveryId, eventName, body, cancellationToken);
    }

    public async ValueTask RecordFailureAsync(
        Guid workspaceId,
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var now = timeProvider.GetUtcNow();
        await dbContext.ProviderWebhookDeliveries
            .Where(x => x.WorkspaceId == workspaceId &&
                        x.ProviderDeliveryId == deliveryId &&
                        x.Status != ProviderWebhookDeliveryStatus.Completed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ProviderWebhookDeliveryStatus.Failed)
                .SetProperty(x => x.OutcomeCode, "processing-failed")
                .SetProperty(x => x.ProcessedAt, now)
                .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
    }
}
