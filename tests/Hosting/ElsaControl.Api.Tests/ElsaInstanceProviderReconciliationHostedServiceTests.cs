using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ElsaControl.Api.Tests;

public sealed class ElsaInstanceProviderReconciliationHostedServiceTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OperationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Provider_cancellation_is_propagated_from_the_per_operation_boundary()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new RecordingSubmissionPort(new OperationCanceledException());
        var reconciler = new RecordingReconciliationService();
        await using var services = CreateServices(provider, reconciler, [Pending(withSubmission: true)]);
        var hosted = CreateHostedService(services);

        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            hosted.ProcessPendingAsync(cancellation.Token));

        Assert.Same(provider.Cancellation, error);
        Assert.Equal(0, reconciler.Calls);
    }

    [Fact]
    public async Task Replay_submission_failure_still_reconciles_the_same_operation()
    {
        var provider = new RecordingSubmissionPort(new InvalidOperationException("provider detail must not escape"));
        var reconciler = new RecordingReconciliationService();
        await using var services = CreateServices(provider, reconciler, [Pending(withSubmission: true)]);
        var hosted = CreateHostedService(services);

        await hosted.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, reconciler.Calls);
        Assert.Equal((WorkspaceId, OperationId), Assert.Single(reconciler.Requests));
    }

    private static ServiceProvider CreateServices(
        IElsaInstanceProviderSubmissionPort provider,
        IElsaInstanceProviderReconciliationService reconciler,
        IReadOnlyList<ElsaInstanceProviderPendingOperation> pending)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IElsaInstanceProviderPendingOperationStore>(new PendingStore(pending));
        services.AddSingleton<IElsaInstanceProviderSubmissionPort>(provider);
        services.AddSingleton<IElsaInstanceProviderSubmissionStore>(new SubmissionStore());
        services.AddSingleton<IElsaInstanceProviderReconciliationService>(reconciler);
        return services.BuildServiceProvider();
    }

    private static ElsaInstanceProviderReconciliationHostedService CreateHostedService(ServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ElsaInstanceLifecycleWorkerOptions { Enabled = true }),
            NullLogger<ElsaInstanceProviderReconciliationHostedService>.Instance);

    private static ElsaInstanceProviderPendingOperation Pending(bool withSubmission) =>
        new(
            WorkspaceId,
            OperationId,
            withSubmission
                ? new ElsaInstanceProviderSubmission(
                    WorkspaceId,
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    OperationId,
                    1,
                    ElsaControl.Deployment.Abstractions.Instances.ElsaDesiredLifecycle.Running,
                    null!,
                    null!)
                : null);

    private sealed class PendingStore(IReadOnlyList<ElsaInstanceProviderPendingOperation> pending)
        : IElsaInstanceProviderPendingOperationStore
    {
        public Task<IReadOnlyList<ElsaInstanceProviderPendingOperation>> ListPendingProviderOperationsAsync(
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult(pending);
    }

    private sealed class SubmissionStore : IElsaInstanceProviderSubmissionStore
    {
        public Task CommitProviderSubmissionAsync(
            ElsaInstanceProviderSubmissionCommit commit,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingSubmissionPort(Exception? failure) : IElsaInstanceProviderSubmissionPort
    {
        public int Calls { get; private set; }
        public OperationCanceledException? Cancellation { get; } = failure as OperationCanceledException;

        public Task<ElsaInstanceProviderSubmissionResult> SubmitAsync(
            ElsaInstanceProviderSubmission request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (failure is not null)
                return Task.FromException<ElsaInstanceProviderSubmissionResult>(failure);
            return Task.FromResult(new ElsaInstanceProviderSubmissionResult("provider-operation-1", false));
        }
    }

    private sealed class RecordingReconciliationService : IElsaInstanceProviderReconciliationService
    {
        public int Calls { get; private set; }
        public List<(Guid WorkspaceId, Guid OperationId)> Requests { get; } = [];

        public Task<ElsaInstanceProviderReconciliationResult> ReconcileAsync(
            Guid workspaceId,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Requests.Add((workspaceId, operationId));
            return Task.FromResult<ElsaInstanceProviderReconciliationResult>(null!);
        }
    }
}
