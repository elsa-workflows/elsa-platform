using ValenceControl.Deployment.Core.Workspace;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Workspace;

public sealed class DeploymentWebhookDispatchHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DeploymentWebhookDispatchOptions> options,
    ILogger<DeploymentWebhookDispatchHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = options.Value.PollInterval > TimeSpan.Zero
            ? options.Value.PollInterval
            : new DeploymentWebhookDispatchOptions().PollInterval;
        using var timer = new PeriodicTimer(pollInterval);

        do
        {
            await DispatchOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<DeploymentWebhookDispatchService>();
            var processed = await dispatcher.DispatchPendingAsync(cancellationToken);
            if (processed > 0)
                logger.LogInformation("Dispatched {WebhookNotificationCount} deployment webhook notifications.", processed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deployment webhook dispatch cycle failed.");
        }
    }
}
