using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Polls durable lifecycle reservations after provider submission. Provider
/// operation workers own mutation; this service submits/replays safe provider
/// work and applies the existing provider-neutral reconciliation projection.
/// </summary>
public sealed class ElsaInstanceProviderReconciliationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ElsaInstanceLifecycleWorkerOptions> options,
    ILogger<ElsaInstanceProviderReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ElsaInstanceLifecycleHostedService.NormalizePollInterval(options.Value.PollInterval));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // Cancellation from a provider or reconciliation dependency is
                // not a retryable provider failure and must reach the host.
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Managed Elsa instance provider reconciliation failed.");
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

    /// <summary>
    /// Processes one durable snapshot without waiting on the scheduling timer.
    /// This is also the deterministic seam for proving cancellation and replay
    /// behavior independently of hosted-service timing.
    /// </summary>
    internal async Task ProcessPendingAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var pending = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderPendingOperationStore>();
        var provider = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderSubmissionPort>();
        var submissionStore = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderSubmissionStore>();
        var reconciler = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderReconciliationService>();
        var operations = await pending.ListPendingProviderOperationsAsync(64, stoppingToken);
        foreach (var operation in operations)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await ProcessOperationAsync(operation, provider, submissionStore, reconciler, stoppingToken);
        }
    }

    private async Task ProcessOperationAsync(
        ElsaInstanceProviderPendingOperation operation,
        IElsaInstanceProviderSubmissionPort provider,
        IElsaInstanceProviderSubmissionStore submissionStore,
        IElsaInstanceProviderReconciliationService reconciler,
        CancellationToken stoppingToken)
    {
        // A process can stop after the durable provider operation was created but
        // before the lifecycle hand-off marker was committed. Replaying the
        // reconstructed submission closes that boundary; the provider's operation
        // identity makes the replay exactly-once.
        if (operation.Submission is { } submission)
        {
            try
            {
                var submitted = await provider.SubmitAsync(submission, stoppingToken);
                submitted.Validate();
                await submissionStore.CommitProviderSubmissionAsync(new(
                    submission.WorkspaceId,
                    submission.InstanceId,
                    submission.OperationId,
                    submission.AttemptNumber,
                    submitted.CorrelationId,
                    DateTimeOffset.UtcNow), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Cancellation must never be converted into a retry warning.
                throw;
            }
            catch (Exception)
            {
                // The provider may have accepted the idempotent replay before the
                // client observed an error. Always continue to reconciliation so
                // observation can discover and converge that remote work. Keep
                // provider exception text out of logs because adapters may receive
                // sensitive SDK diagnostics.
                logger.LogWarning(
                    "Managed Elsa instance provider hand-off is awaiting retry for operation {OperationId}.",
                    operation.OperationId);
            }
        }

        try
        {
            await reconciler.ReconcileAsync(operation.WorkspaceId, operation.OperationId, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            // A concurrent terminal/deletion transition removed the recovery target
            // before this read; the next scan will observe the durable outcome.
        }
        catch (ElsaInstanceLifecycleConflictException)
        {
            // Another reconciler won the compare-and-set boundary.
        }
        catch (Exception)
        {
            // Reconciliation is retried on the next poll. Keep provider exception
            // text out of logs because adapters may receive sensitive diagnostics.
            logger.LogWarning(
                "Managed Elsa instance provider reconciliation is awaiting retry for operation {OperationId}.",
                operation.OperationId);
        }
    }
}
