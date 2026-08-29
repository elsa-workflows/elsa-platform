using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

public sealed class AzureProviderOperationOptions
{
    public const string ConfigurationSection = "Deployment:AzureProvider";

    public bool WorkerEnabled { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 10;
}

/// <summary>
/// Periodically wakes the durable Azure operation worker. All ownership and recovery decisions
/// remain in the provider executor/store; this host service only supplies process lifetime and
/// bounded polling.
/// </summary>
public sealed class AzureProviderOperationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AzureProviderOperationOptions> options,
    ILogger<AzureProviderOperationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        var interval = configured.PollInterval > TimeSpan.Zero
            ? configured.PollInterval
            : TimeSpan.FromSeconds(5);
        var batchSize = configured.BatchSize is >= 1 and <= 100 ? configured.BatchSize : 10;
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<AzureProviderOperationWorker>();
                var processed = await worker.ProcessOnceAsync(batchSize, stoppingToken);
                if (processed > 0)
                    logger.LogInformation("Processed {AzureProviderOperationCount} Azure provider operations.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Azure provider operation processing failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
