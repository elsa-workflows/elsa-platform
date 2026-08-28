using ElsaControl.PackageCatalog.Core.Sync;

namespace ElsaControl.Api.Admin.Sync;

public sealed class ScheduledSyncHostedService(IServiceProvider services, IConfiguration configuration, ILogger<ScheduledSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue("Sync:Scheduled:Enabled", false);
        if (!enabled)
            return;

        var interval = configuration.GetValue("Sync:Scheduled:Interval", TimeSpan.FromHours(1));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PackageSyncService>().SyncAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled package catalog sync failed.");
            }
        }
    }
}
