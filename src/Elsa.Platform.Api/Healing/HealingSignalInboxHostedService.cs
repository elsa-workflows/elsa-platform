using Elsa.Platform.Healing.Core.Incidents;
using Elsa.Platform.Healing.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Healing;

public sealed class HealingSignalInboxHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<HealingOptions> options,
    ILogger<HealingSignalInboxHostedService> logger) : BackgroundService
{
    private const int DueIncidentBatchSize = 100;
    private readonly HealingOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var concurrency = _options.Budgets.MaxConcurrentOperations;
        return Task.WhenAll(Enumerable.Range(0, concurrency).Select(index => RunWorkerAsync(index, stoppingToken)));
    }

    private async Task RunWorkerAsync(int index, CancellationToken stoppingToken)
    {
        var workerId = $"healing-inbox:{Environment.MachineName}:{index}:{Guid.NewGuid():N}";
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<HealingSignalInboxWorker>();
                var promoted = index == 0
                    ? await worker.PromoteDueIncidentsAsync(
                        timeProvider.GetUtcNow(),
                        DueIncidentBatchSize,
                        stoppingToken)
                    : 0;
                var result = await worker.RunOnceAsync(workerId, stoppingToken);
                if (result.Status == HealingInboxWorkerStatus.Idle && promoted == 0)
                    await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                logger.LogWarning(
                    "Healing inbox processing failed with code {FailureCode}; retrying after the idle delay.",
                    "healing-inbox-worker-failed");
                await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
