using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Verification;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Healing;

public sealed class HealingVerificationHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<HealingOptions> options,
    ILogger<HealingVerificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var changed = await scope.ServiceProvider.GetRequiredService<HealingVerificationWorker>()
                    .RunOnceAsync(stoppingToken);
                if (!changed)
                    await Task.Delay(options.Value.IdleDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Healing verification polling failed; retrying after the idle delay.");
                await Task.Delay(options.Value.IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
