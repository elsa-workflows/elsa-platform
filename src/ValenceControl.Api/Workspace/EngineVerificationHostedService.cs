using ValenceControl.Deployment.Core.Workspace;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Workspace;

public sealed class EngineVerificationOptions
{
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan VerificationInterval { get; set; } = TimeSpan.FromMinutes(15);

    public int BatchSize { get; set; } = 25;
}

internal sealed class EngineVerificationHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<EngineVerificationOptions> options,
    ILogger<EngineVerificationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = PositiveOrDefault(options.Value.PollInterval, new EngineVerificationOptions().PollInterval);
        using var timer = new PeriodicTimer(pollInterval);

        do
        {
            await VerifyDueEnginesAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task VerifyDueEnginesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
            var health = scope.ServiceProvider.GetRequiredService<EngineHealthService>();
            var configured = options.Value;
            var verificationInterval = PositiveOrDefault(configured.VerificationInterval, new EngineVerificationOptions().VerificationInterval);
            var verifyBefore = DateTimeOffset.UtcNow.Subtract(verificationInterval);
            var batchSize = configured.BatchSize > 0 ? configured.BatchSize : new EngineVerificationOptions().BatchSize;
            var engines = await store.ListEnginesDueForVerificationAsync(verifyBefore, batchSize, cancellationToken);

            foreach (var engine in engines)
                await VerifyEngineAsync(health, engine, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Engine verification cycle failed.");
        }
    }

    private async Task VerifyEngineAsync(
        EngineHealthService health,
        WorkspaceWorkflowEngine engine,
        CancellationToken cancellationToken)
    {
        try
        {
            await health.VerifyEngineAsync(
                engine.WorkspaceId,
                new EngineHealthVerificationRequest(engine.Id, Guid.Empty),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Engine verification failed for engine {EngineId} in workspace {WorkspaceId}.", engine.Id, engine.WorkspaceId);
        }
    }

    private static TimeSpan PositiveOrDefault(TimeSpan value, TimeSpan fallback) =>
        value > TimeSpan.Zero ? value : fallback;
}
