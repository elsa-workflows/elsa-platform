using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Drains durable managed-instance deletion reservations. Finalization remains
/// fail closed until the provider returns correlated positive absence evidence.
/// </summary>
public sealed class ElsaInstanceDeletionHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ElsaInstanceLifecycleWorkerOptions> options,
    ILogger<ElsaInstanceDeletionHostedService> logger) : BackgroundService
{
    private readonly string _workerId = $"api-instance-deletion-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            ElsaInstanceLifecycleHostedService.NormalizePollInterval(options.Value.PollInterval));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<ElsaInstanceDeletionWorker>();
                var batch = await worker.ProcessAvailableAsync(_workerId, stoppingToken);
                foreach (var result in batch.Results)
                    logger.LogInformation(
                        "Managed Elsa deletion completed for workspace {WorkspaceId}, instance {InstanceId}, operation {OperationId}, outcome {Outcome}, diagnostic {DiagnosticCode}.",
                        result.Instance.WorkspaceId,
                        result.Instance.Id,
                        result.Operation.Id,
                        result.Outcome,
                        ManagedLifecycleOperationalHealthDiagnosticCodes.IsSafe(result.FailureCode)
                            ? result.FailureCode
                            : ManagedLifecycleOperationalHealthDiagnosticCodes.Unknown);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Managed Elsa instance deletion processing failed.");
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
}
