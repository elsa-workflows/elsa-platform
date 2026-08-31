using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

public sealed class ElsaInstanceLifecycleWorkerOptions
{
    public const string ConfigurationSection = "Deployment:ElsaInstanceLifecycle";
    public bool Enabled { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Wakes the durable instance resolver without holding an HTTP request open.</summary>
public sealed class ElsaInstanceLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ElsaInstanceLifecycleWorkerOptions> options,
    ILogger<ElsaInstanceLifecycleHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(1);
    private readonly string _workerId = CreateWorkerId();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return;
        using var timer = new PeriodicTimer(NormalizePollInterval(configured.PollInterval));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<ElsaInstanceLifecycleWorker>();
                await worker.ProcessAvailableAsync(_workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Managed Elsa instance lifecycle processing failed.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal static TimeSpan NormalizePollInterval(TimeSpan configured) =>
        configured < MinimumPollInterval ? MinimumPollInterval : configured;

    internal static string CreateWorkerId() =>
        $"api-instance-lifecycle-{Environment.ProcessId}-{Guid.NewGuid():N}";
}
