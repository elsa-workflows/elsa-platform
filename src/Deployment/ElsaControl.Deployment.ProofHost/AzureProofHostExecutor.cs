using System.Text.Json;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Proof;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.Deployment.ProofHost;

/// <summary>Explicit local-only composition for the opt-in disposable Azure proof.</summary>
public sealed class AzureProofHostExecutor(TextWriter output, TextWriter error) : IProofHostExecutor
{
    private const int ProofFailedExitCode = 5;
    private static readonly Guid ProofOrganizationId = Guid.Parse("19519519-5195-4195-8195-195195195195");
    private static readonly Guid ProofInstanceId = Guid.Parse("29529529-5295-4295-8295-295295295295");

    public async Task<int> ExecuteAsync(ProofHostOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            options.EnsureValid();
            if (!options.MutationAuthorized || options.Mode is ProofHostMode.Validate)
                throw new InvalidOperationException("Mutation authority is unavailable.");
            if (options.Mode == ProofHostMode.Run && File.Exists(options.StatePath))
                throw new InvalidOperationException("A prior proof run requires cleanup before another run.");

            await using var db = await CreateDatabaseAsync(options, cancellationToken);
            var store = new AzureProviderOperationStore(db);
            var service = new AzureProviderOperationService(store);
            using var secrets = new DisposableAzureProofSecrets();
            var runnerOptions = options.CreateRunnerOptions();
            var targetScope = options.CreateTargetScope();
            var runner = new AzureBicepProviderRunner(runnerOptions, targetScope, secrets);
            var executor = new AzureProviderExecutor(store, runner, workerId: $"proof-host-{options.ProofName}");
            var resolution = Elsa38CombinedProofResolutionFactory.Create(options.CreateAdmission());
            var templateFingerprint = runnerOptions.ComputeTemplateAuthorityFingerprint();
            var scopeFingerprint = runnerOptions.ComputeProviderScopeFingerprint(targetScope);
            var planFactory = new AdmittedAzureProofPlanFactory(
                resolution, new(options.ProofName, options.Location), templateFingerprint, scopeFingerprint, options.Features,
                ProofOrganizationId, ProofInstanceId);

            return options.Mode switch
            {
                ProofHostMode.Run => await RunProofAsync(
                    options, store, service, executor, planFactory, templateFingerprint, secrets, cancellationToken),
                ProofHostMode.Cleanup => await RunCleanupAsync(
                    options, store, service, executor, planFactory, templateFingerprint, cancellationToken),
                _ => throw new InvalidOperationException("The proof host mode is invalid.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("proof-host.cancelled");
            return ProofFailedExitCode;
        }
        catch (Exception)
        {
            await error.WriteLineAsync("proof-host.execution.failed");
            return ProofFailedExitCode;
        }
    }

    private async Task<int> RunProofAsync(
        ProofHostOptions options,
        AzureProviderOperationStore store,
        IAzureProviderOperationService service,
        AzureProviderExecutor executor,
        IAzureProviderProofPlanFactory planFactory,
        string templateFingerprint,
        DisposableAzureProofSecrets secrets,
        CancellationToken cancellationToken)
    {
        using var probe = new ElsaHttpWorkflowProbe(
            new(options.WorkflowUsername, requestTimeout: options.RequestTimeout,
                workflowTimeout: options.WorkflowTimeout, pollInterval: options.PollInterval),
            secrets);
        var adapter = new AzureProviderProofAdapter(
            options.WorkspaceId, templateFingerprint, service, executor, planFactory, probe,
            ct => PrepareCleanupRecoveryAsync(options, store, ct));
        var report = await new DeploymentProofHarness(cleanupTimeout: options.CleanupTimeout).RunAsync(
            options.CreateProofInput(), options.CreateProofEnvironment(), adapter, cancellationToken);
        await output.WriteAsync(report.ToJson());
        return report.Outcome == DeploymentProofOutcome.Passed ? 0 : ProofFailedExitCode;
    }

    private async Task<int> RunCleanupAsync(
        ProofHostOptions options,
        AzureProviderOperationStore store,
        IAzureProviderOperationService service,
        AzureProviderExecutor executor,
        IAzureProviderProofPlanFactory planFactory,
        string templateFingerprint,
        CancellationToken cancellationToken)
    {
        await PrepareCleanupRecoveryAsync(options, store, cancellationToken);
        var input = options.CreateProofInput();
        var environment = options.CreateProofEnvironment();
        var selector = new AzureProviderProofAdapter(
            options.WorkspaceId, templateFingerprint, service, executor, planFactory);
        var selection = await selector.SelectAsync(input, environment, cancellationToken);
        var submission = planFactory.Create(selection, environment) with
        {
            LifecycleAction = ElsaInstanceOperationAction.Delete
        };
        var operation = await service.SubmitDeleteAsync(options.WorkspaceId, submission, cancellationToken);
        var request = AzureProviderOperationService.CreateOperationRequest(
            options.WorkspaceId, operation.IdempotencyKey, templateFingerprint, submission.Plan,
            AzureProviderOperationAction.Delete, submission.ProviderScopeFingerprint,
            submission.OrganizationId, submission.InstanceId, submission.LifecycleAction);
        using var cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cleanupCts.CancelAfter(options.CleanupTimeout);
        var execution = await executor.DeleteAsync(request, submission.Plan, cleanupCts.Token);
        var absent = execution.Succeeded && ReferencesAreEmpty(execution.Operation.Resources) &&
                     execution.Operation.Endpoint is null;
        var evidence = new
        {
            outcome = absent ? "passed" : "failed",
            mode = "cleanup",
            operationId = execution.Operation.Id.ToString("N"),
            status = execution.Operation.Status.ToString(),
            providerOutcome = execution.Outcome.ToString(),
            ownedResourcesAbsent = absent
        };
        await output.WriteLineAsync(JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return absent ? 0 : ProofFailedExitCode;
    }

    private static async Task PrepareCleanupRecoveryAsync(
        ProofHostOptions options,
        AzureProviderOperationStore store,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await store.RecoverStaleAsync(now, cancellationToken);
        var targetScope = options.CreateTargetScope();
        var providerScopeFingerprint = options.CreateRunnerOptions().ComputeProviderScopeFingerprint(targetScope);
        var reconcile = await store.GetLatestActiveReconcileAsync(
            options.WorkspaceId, options.ProofName, providerScopeFingerprint, cancellationToken);
        if (reconcile?.Status != AzureProviderOperationStatus.RecoveryRequired)
            return;

        const string workerId = "proof-host-cleanup-recovery";
        var leaseToken = Guid.NewGuid().ToString("N");
        var claimed = await store.ClaimRecoveryAsync(
            reconcile.WorkspaceId, reconcile.Id, workerId, leaseToken, TimeSpan.FromMinutes(1), now,
            reconcile.Version, cancellationToken);
        if (claimed is null)
            throw new InvalidOperationException("The stale proof operation could not be claimed for cleanup.");
        var finalized = await store.FinalizeAsync(
            claimed.WorkspaceId, claimed.Id, leaseToken, AzureProviderOperationStatus.Cancelled,
            "azure.proof.cleanup-recovery", now, claimed.Version, cancellationToken);
        if (finalized is null)
            throw new InvalidOperationException("The stale proof operation could not be released for cleanup.");
    }

    private static async Task<CatalogDbContext> CreateDatabaseAsync(
        ProofHostOptions options,
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = options.StatePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();
        var dbOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite =>
                sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        var db = new CatalogDbContext(dbOptions);
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            if (!await db.Organizations.AnyAsync(x => x.Id == ProofOrganizationId, cancellationToken))
                db.Organizations.Add(new Organization { Id = ProofOrganizationId, Name = "Disposable Azure proof" });
            var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == options.WorkspaceId, cancellationToken);
            if (workspace is null)
            {
                db.Workspaces.Add(new Workspace
                {
                    Id = options.WorkspaceId,
                    OrganizationId = ProofOrganizationId,
                    Name = options.ProofName,
                    Kind = WorkspaceKind.Shared
                });
            }
            else if (workspace.OrganizationId != ProofOrganizationId ||
                     !string.Equals(workspace.Name, options.ProofName, StringComparison.Ordinal) ||
                     workspace.Kind != WorkspaceKind.Shared || workspace.SoftDeletedAt is not null)
                throw new InvalidOperationException("The proof workspace identity does not match.");
            await db.SaveChangesAsync(cancellationToken);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static bool ReferencesAreEmpty(AzureProviderResourceReferences references) =>
        references == new AzureProviderResourceReferences();
}
