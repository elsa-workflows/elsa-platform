using Elsa.Platform.Deployment.Core.Workspace;

namespace Elsa.Platform.Api.Workspace;

internal sealed class DeploymentQueueHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeploymentQueueHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleRunAfter = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverStaleRunsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Deployment queue processing failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task RecoverStaleRunsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<DeploymentQueueWorker>();
        await worker.RecoverStaleRunsAsync(StaleRunAfter, cancellationToken);
    }
}
