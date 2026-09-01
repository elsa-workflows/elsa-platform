using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Polls durable lifecycle reservations after provider submission. Provider
/// operation workers own mutation; this service only reads provider state and
/// applies the existing provider-neutral reconciliation projection.
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
                await using var scope = scopeFactory.CreateAsyncScope();
                var pending = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderPendingOperationStore>();
                var provider = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderSubmissionPort>();
                var submissionStore = scope.ServiceProvider.GetRequiredService<IElsaInstanceProviderSubmissionStore>();
                var reconciler = scope.ServiceProvider.GetRequiredService<ElsaInstanceProviderReconciliationService>();
                var operations = await pending.ListPendingProviderOperationsAsync(64, stoppingToken);
                foreach (var operation in operations)
                {
                    try
                    {
                        // A process can stop after the durable provider operation
                        // was created but before the lifecycle hand-off marker was
                        // committed. Replaying the reconstructed submission closes
                        // that boundary; the provider's operation identity makes
                        // the replay exactly-once.
                        if (operation.Submission is { } submission)
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
                        await reconciler.ReconcileAsync(operation.WorkspaceId, operation.OperationId, stoppingToken);
                    }
                    catch (KeyNotFoundException)
                    {
                        // A concurrent terminal/deletion transition removed the
                        // recovery target before this read; the next scan will
                        // observe the durable outcome.
                    }
                    catch (ElsaInstanceLifecycleConflictException)
                    {
                        // Another reconciler won the compare-and-set boundary.
                    }
                    catch (Exception)
                    {
                        // Provider submission and reconciliation are retried on
                        // the next poll. Keep provider exception text out of logs
                        // because adapters may receive sensitive SDK diagnostics.
                        logger.LogWarning(
                            "Managed Elsa instance provider hand-off is awaiting retry for operation {OperationId}.",
                            operation.OperationId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
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
}
