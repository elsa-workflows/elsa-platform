using Elsa.Catalog.Core.Sync;

namespace Elsa.Catalog.Api.Admin.Sync;

public sealed class ManualSyncHostedService(IServiceProvider services, ManualSyncQueue queue, ILogger<ManualSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PackageSyncService>().ExecuteManualWorkItemAsync(workItem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await MarkWorkItemFailedAsync(workItem, "Manual package catalog sync was canceled because the host is stopping.");
                workItem.Dispose();
                return;
            }
            catch (Exception ex)
            {
                await MarkWorkItemFailedAsync(workItem, ex.Message);
                workItem.Dispose();
                logger.LogError(ex, "Manual package catalog sync failed.");
            }
        }
    }

    private async Task MarkWorkItemFailedAsync(PackageSyncWorkItem workItem, string error)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PackageSyncService>().MarkManualWorkItemFailedAsync(workItem, error, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual package catalog sync run {SyncRunId} could not be marked as failed.", workItem.RunId);
        }
    }
}
