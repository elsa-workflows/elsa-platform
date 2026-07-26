using Elsa.Platform.Healing.Core.Configuration;
using Elsa.Platform.Healing.Core.Operations;
using Elsa.Platform.Healing.Core.Providers;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Api.Healing;

public sealed class HealingProviderOperationHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<HealingOptions> options,
    ILogger<HealingProviderOperationHostedService> logger) : BackgroundService
{
    private readonly HealingOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, _options.Budgets.MaxConcurrentOperations)
            .Select(index => RunWorkerAsync(index == 0, stoppingToken)));

    private async Task RunWorkerAsync(bool coordinatesMerges, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinated = await scope.ServiceProvider
                    .GetRequiredService<HealingRepairCoordinator>()
                    .RunOnceAsync(stoppingToken);
                var result = await scope.ServiceProvider
                    .GetRequiredService<ProviderOperationService>()
                    .RunOnceAsync(stoppingToken);
                var merged = coordinatesMerges && await scope.ServiceProvider
                    .GetRequiredService<HealingAutoMergeCoordinator>()
                    .RunOnceAsync(stoppingToken);
                var commanded = coordinatesMerges && await scope.ServiceProvider
                    .GetRequiredService<HealingHumanCommandCoordinator>()
                    .RunOnceAsync(stoppingToken);
                if (coordinated == HealingRepairCoordinatorStatus.Idle &&
                    result.Status is HealingWorkerRunStatus.Idle or HealingWorkerRunStatus.Paused &&
                    !merged && !commanded)
                    await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                logger.LogWarning(
                    "Healing provider dispatch failed with code {FailureCode}; retrying after the idle delay.",
                    "healing-provider-worker-failed");
                await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
