using System.Collections.Concurrent;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed partial class AzureProviderRecoveryObservationPersistenceTests
{
    [Fact]
    public async Task Concurrent_recovery_requests_claim_one_real_operation_and_run_one_resume_step()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"elsa-control-recovery-concurrency-{Guid.NewGuid():N}.db");
        try
        {
            ObservationFixture fixture;
            AzureProviderRecoveryObservationBinding binding;
            await using (var setupConnection = new SqliteConnection(
                             $"Data Source={databasePath};Pooling=False;Default Timeout=30"))
            {
                await setupConnection.OpenAsync();
                await using var setupDb = CreateMigratedContext(setupConnection);
                await setupDb.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                await setupDb.Database.MigrateAsync();
                fixture = await SeedProviderObservationAsync(setupDb);
                binding = await AcceptRecoveryAsync(
                    setupDb,
                    fixture,
                    (IAzureProviderRecoveryObservationStore)fixture.OperationStore);
            }

            var runner = new ConcurrentRecoveryRunner();
            await using var firstDb = CreateMigratedContext(databasePath);
            await using var secondDb = CreateMigratedContext(databasePath);
            var firstStore = new AzureProviderOperationStore(firstDb);
            var secondStore = new AzureProviderOperationStore(secondDb);
            var firstProvider = CreateConcurrentProvider(
                fixture,
                firstStore,
                runner,
                "recovery-concurrency-first");
            var secondProvider = CreateConcurrentProvider(
                fixture,
                secondStore,
                runner,
                "recovery-concurrency-second");
            var request = CreateRecoveryRequest(fixture, binding);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var first = firstProvider.RecoverAsync(request, timeout.Token);
            var second = secondProvider.RecoverAsync(request, timeout.Token);
            await runner.BothObservations.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await runner.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            runner.ReleaseMutation();
            var results = await Task.WhenAll(first, second);

            Assert.Equal(2, runner.Observations.Count);
            Assert.Single(runner.Mutations);
            Assert.DoesNotContain(results, result =>
                result.Outcome == ElsaInstanceProviderRecoveryOutcome.Succeeded);
            Assert.Contains(results, result =>
                result.Code is "azure.operation.claim-lost" or "azure.operation.recovery-required");

            Assert.All(runner.Observations, observed =>
            {
                Assert.Equal(fixture.Operation.Id, observed.Operation.Id);
                Assert.Equal(fixture.Assignment.Id, observed.Assignment!.Id);
                Assert.Equal(fixture.Operation.PlanFingerprint, observed.Plan.Fingerprint);
                Assert.Equal(fixture.Operation.ProviderAssignmentId, observed.Operation.ProviderAssignmentId);
            });
            var mutation = Assert.Single(runner.Mutations);
            Assert.Equal(AzureProviderRunnerStep.AcrPull, mutation.Step);
            Assert.True(mutation.IsResume);
            Assert.Equal(fixture.Operation.Id, mutation.Context.OperationId);
            Assert.Equal(fixture.Operation.OperationIdentity, mutation.Context.OperationIdentity);
            Assert.Equal(fixture.Assignment.Id.ToString("D"), mutation.Context.ProviderAssignmentId);
            Assert.Equal(fixture.Operation.PlanFingerprint, mutation.Context.PlanFingerprint);
            Assert.Equal(fixture.Operation.TemplateFingerprint, mutation.Context.TemplateFingerprint);

            await using var verificationDb = CreateMigratedContext(databasePath);
            var persisted = Assert.Single(await verificationDb.AzureProviderOperations.AsNoTracking().ToListAsync());
            Assert.Equal(fixture.Operation.Id, persisted.Id);
            Assert.Equal(fixture.Assignment.Id, persisted.ProviderAssignmentId);
            Assert.Equal(fixture.Operation.PlanFingerprint, persisted.PlanFingerprint);
            Assert.Equal(fixture.Operation.AttemptNumber + 1, persisted.AttemptNumber);
            Assert.Equal(
                1,
                await verificationDb.AzureProviderOperationTransitions.AsNoTracking()
                    .CountAsync(x => x.OperationId == fixture.Operation.Id &&
                                     x.Code == "operation.recovery.claimed"));
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static AzureElsaInstanceProvider CreateConcurrentProvider(
        ObservationFixture fixture,
        AzureProviderOperationStore operationStore,
        ConcurrentRecoveryRunner runner,
        string workerId)
    {
        var options = new AzureElsaInstanceProviderOptions
        {
            Enabled = true,
            TemplateFingerprint = fixture.Operation.TemplateFingerprint,
            ProviderScopeFingerprint = fixture.Operation.ProviderScopeFingerprint,
            SubscriptionId = fixture.Assignment.SubscriptionId,
            ResourceGroupNamePrefix = "rg-recovery"
        };
        var executor = new AzureProviderExecutor(
            operationStore,
            runner,
            leaseDuration: TimeSpan.FromMinutes(5),
            workerId: workerId,
            assignmentStore: operationStore);
        return new AzureElsaInstanceProvider(
            new AzureProviderOperationService(operationStore),
            operationStore,
            operationStore,
            options: options,
            executor: executor,
            recoveryObserver: runner,
            recoveryObservationStore: operationStore);
    }

    private sealed class ConcurrentRecoveryRunner : IAzureProviderRunner, IAzureProviderRecoveryObserver
    {
        private readonly TaskCompletionSource _bothObservations = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _mutationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseMutation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _observationCount;

        public TaskCompletionSource BothObservations => _bothObservations;
        public TaskCompletionSource MutationStarted => _mutationStarted;
        public ConcurrentQueue<AzureProviderRecoveryRequest> Observations { get; } = [];
        public ConcurrentQueue<AzureProviderRunnerCommand> Mutations { get; } = [];

        public async Task<AzureProviderRecoveryObservation> ObserveAsync(
            AzureProviderRecoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            request.Validate();
            if (request.Assignment is null)
                throw new InvalidOperationException("The recovery observer requires the durable assignment.");

            Observations.Enqueue(request);
            if (Interlocked.Increment(ref _observationCount) == 2)
                _bothObservations.TrySetResult();
            await _bothObservations.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            return new(
                AzureProviderRecoveryObservationKind.Confirmed,
                AzureProviderRunnerStep.Foundation,
                request.Operation.Resources,
                AzureProviderHealth.Unknown,
                null,
                "azure.foundation.observed",
                "The retained Azure foundation was observed.");
        }

        public async Task<AzureProviderRunnerResult> RunAsync(
            AzureProviderRunnerCommand command,
            CancellationToken cancellationToken = default)
        {
            Mutations.Enqueue(command);
            _mutationStarted.TrySetResult();
            await _releaseMutation.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            return new(
                AzureProviderRunnerOutcome.Uncertain,
                AzureProviderOperationPhase.FoundationSubmitted,
                command.Resources,
                AzureProviderHealth.Unknown,
                null,
                [],
                "azure.step.uncertain",
                "The resumed Azure step remained uncertain.");
        }

        public void ReleaseMutation() => _releaseMutation.TrySetResult();
    }
}
