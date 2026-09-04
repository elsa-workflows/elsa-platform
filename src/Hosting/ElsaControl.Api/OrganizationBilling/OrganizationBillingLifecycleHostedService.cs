using ElsaControl.PackageCatalog.Core.Accounts;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.OrganizationBilling;

public sealed class OrganizationBillingLifecycleWorkerOptions
{
    public const string ConfigurationSection = "Billing:Lifecycle";
    public bool Enabled { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Runs the durable commercial clock and optional provider cleanup pass.</summary>
public sealed class OrganizationBillingLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<OrganizationBillingLifecycleWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<OrganizationBillingLifecycleHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumPollInterval = TimeSpan.FromSeconds(1);
    private readonly string _workerId = $"billing-lifecycle-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return;

        using var timer = new PeriodicTimer(NormalizePollInterval(configured.PollInterval), timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<OrganizationBillingLifecycleWorker>();
                var result = await worker.ProcessAvailableAsync(_workerId, stoppingToken);
                if (result.Advances.Count > 0 || result.CleanupAttempts > 0)
                    logger.LogInformation("Billing lifecycle pass advanced {AdvanceCount} organization subscriptions and attempted {CleanupCount} cleanup operations.", result.Advances.Count, result.CleanupAttempts);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Billing lifecycle processing failed.");
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
}
