using System.Text;
using System.Security.Cryptography;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed partial class AzureProviderRecoveryObservationPersistenceTests
{
    [Fact]
    public async Task Sql_script_observation_rehydrates_through_lifecycle_and_provider_without_reexecution_or_early_success()
    {
        if (OperatingSystem.IsWindows())
            throw SkipException.ForSkip("The fake Azure command process uses POSIX executable scripts.");

        var root = Path.Combine(Path.GetTempPath(), $"elsa-sql-recovery-proof-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var templates = Path.Combine(root, "templates");
            Directory.CreateDirectory(templates);
            foreach (var name in new[] { "main.bicep", "acr-pull-role.bicep", "sql-bootstrap.sql" })
                await File.WriteAllTextAsync(Path.Combine(templates, name), "targetScope = 'resourceGroup'\n");

            var firewallDeleted = Path.Combine(root, "firewall-deleted");
            var sqlObservations = Path.Combine(root, "sql-observations");
            var azPath = Path.Combine(root, "az");
            var sqlCmdPath = Path.Combine(root, "sqlcmd");
            var curlPath = Path.Combine(root, "curl");
            await WriteExecutableAsync(azPath, $$"""
#!/bin/sh
set -eu
state={{ShellQuote(firewallDeleted)}}
case " $* " in
  *" firewall-rule delete "*) : > "$state"; exit 0 ;;
  *" firewall-rule list "*)
    if [ -f "$state" ]; then printf '%s' '[]'; else printf '%s' '[{"name":"elsa-bootstrap","startIpAddress":"203.0.113.10","endIpAddress":"203.0.113.10"}]'; fi
    exit 0 ;;
  *" resource list "*) printf '%s' 'not-a-number'; exit 0 ;;
  *) printf '%s' '[]'; exit 0 ;;
esac
""");
            await WriteExecutableAsync(sqlCmdPath, $"""
#!/bin/sh
set -eu
printf x >> {ShellQuote(sqlObservations)}
printf '%s' 'complete'
""");
            await WriteExecutableAsync(curlPath, "#!/bin/sh\nprintf '%s' 'unused'\n");

            var options = new AzureProviderRunnerOptions
            {
                Enabled = true,
                AzureCliPath = azPath,
                AzureCliClientId = "11111111-1111-1111-1111-111111111111",
                SqlCmdPath = sqlCmdPath,
                CurlPath = curlPath,
                TemplateRoot = templates,
                SqlBootstrapObjectId = "22222222-2222-2222-2222-222222222222",
                SqlBootstrapLogin = "elsa-bootstrap",
                SqlBootstrapIp = "203.0.113.10",
                RuntimeAdminUsername = "elsa-admin",
                CommandTimeout = TimeSpan.FromSeconds(30),
                ObservationAttempts = 1,
                ObservationDelay = TimeSpan.Zero
            };
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var db = CreateMigratedContext(connection);
            await db.Database.MigrateAsync();
            var (workspace, instanceId, lifecycleOperationId, resolvedPlan, providerPlan) = await SeedLifecycleAuthorityAsync(db);

            var assignmentResourceGroup = AzureProviderResourceAssignmentNaming.ResourceGroupName(
                "rg-recovery", instanceId);
            var scope = new AzureProviderTargetScope(
                "11111111-1111-1111-1111-111111111111",
                assignmentResourceGroup,
                "22222222-2222-2222-2222-222222222222",
                "rg-registry",
                "registry",
                "westeurope");
            var templateFingerprint = options.ComputeTemplateAuthorityFingerprint();
            var scopeFingerprint = options.ComputeProviderScopeFingerprint(scope);
            var operationStore = new AzureProviderOperationStore(db);
            var assignment = await ((IAzureProviderResourceAssignmentStore)operationStore).CreateOrGetAsync(
                new(
                    workspace.Id,
                    workspace.OrganizationId,
                    instanceId,
                    scopeFingerprint,
                    scope.SubscriptionId,
                    "rg-recovery",
                    providerPlan.WorkloadName,
                    scope.Location),
                DateTimeOffset.Parse("2026-09-05T16:00:00Z"));
            var operation = await operationStore.CreateOrGetAsync(
                new(
                    workspace.Id,
                    assignment.WorkloadName,
                    AzureProviderOperationAction.Reconcile,
                    $"elsa-instance-operation:{lifecycleOperationId:D}",
                    providerPlan.Fingerprint,
                    templateFingerprint,
                    providerPlan.ElsaVersion,
                    providerPlan.ReleaseLine,
                    providerPlan.Topology,
                    providerPlan.Isolation,
                    providerPlan.Location,
                    providerPlan.ImageRepository,
                    $"sha256:{providerPlan.ImageDigest}",
                    providerPlan.ReleaseManifestDigest,
                    providerPlan.ReleaseManifestSignatureDigest,
                    providerPlan.ReleaseManifestReference,
                    providerPlan.ReleaseManifestSignatureReference,
                    providerPlan.SecretReferences,
                    scopeFingerprint,
                    providerPlan.SqlWorkflowPackageVersion,
                    providerPlan.SqlQuartzPackageVersion,
                    workspace.OrganizationId,
                    instanceId,
                    ElsaInstanceOperationAction.Reconcile,
                    assignment.Id),
                DateTimeOffset.Parse("2026-09-05T16:00:00Z"));
            var now = DateTimeOffset.Parse("2026-09-05T16:00:00Z");
            var claimed = Assert.IsType<AzureProviderOperation>(await operationStore.ClaimAsync(
                workspace.Id, operation.Id, "proof-worker", "proof-lease", TimeSpan.FromMinutes(5), now, operation.Version));
            operation = Assert.IsType<AzureProviderOperation>(await operationStore.FinalizeAsync(
                workspace.Id, operation.Id, "proof-lease", AzureProviderOperationStatus.RecoveryRequired,
                "operation.recovery.required", now, claimed.Version));

            var registryId = $"/subscriptions/{scope.RegistrySubscriptionId}/resourceGroups/{scope.RegistryResourceGroupName}/providers/Microsoft.ContainerRegistry/registries/{scope.RegistryName}";
            var acrDeploymentName = $"elsa-{providerPlan.WorkloadName}-{ShortHash($"{scope.SubscriptionId}/{assignment.ResourceGroupName}/22222222-2222-2222-2222-222222222222/{scope.RegistrySubscriptionId}/{scope.RegistryResourceGroupName}/{scope.RegistryName}")}-acr";
            var stagedResources = FoundationResources(assignment, providerPlan.Fingerprint) with
            {
                RegistryResourceId = registryId,
                AcrPullDeploymentId = $"/subscriptions/{scope.RegistrySubscriptionId}/resourceGroups/{scope.RegistryResourceGroupName}/providers/Microsoft.Resources/deployments/{acrDeploymentName}",
                AcrPullRoleAssignmentId = $"{registryId}/providers/Microsoft.Authorization/roleAssignments/33333333-3333-3333-3333-333333333333"
            };
            claimed = Assert.IsType<AzureProviderOperation>(await operationStore.ClaimRecoveryAsync(
                workspace.Id, operation.Id, "proof-worker", "proof-sql-lease", TimeSpan.FromMinutes(5), now, operation.Version));
            operation = Assert.IsType<AzureProviderOperation>(await operationStore.CheckpointAsync(
                workspace.Id,
                operation.Id,
                "proof-sql-lease",
                new(
                    AzureProviderOperationPhase.SqlBootstrapReady,
                    "azure.sql.bootstrap-observed",
                    "The SQL bootstrap postcondition was observed before cleanup.",
                    stagedResources,
                    null,
                    AzureProviderHealth.Unknown,
                    [],
                    AttemptedStep: AzureProviderRunnerStep.SqlFirewallCleanup),
                now,
                claimed.Version));
            operation = Assert.IsType<AzureProviderOperation>(await operationStore.FinalizeAsync(
                workspace.Id,
                operation.Id,
                "proof-sql-lease",
                AzureProviderOperationStatus.RecoveryRequired,
                "azure.step.uncertain",
                now,
                operation.Version));
            db.ChangeTracker.Clear();
            assignment = Assert.IsType<AzureProviderResourceAssignment>(await
                ((IAzureProviderResourceAssignmentStore)operationStore).GetAsync(workspace.Id, assignment.Id));
            operation = Assert.IsType<AzureProviderOperation>(await operationStore.GetAsync(workspace.Id, operation.Id));

            // Seed the lifecycle run before invoking the real provider observation producer.
            // The setup fixture's observation is not persisted; the provider below creates the
            // durable record from the actual runner result and its correlated operation.
            var setupFixture = new ObservationFixture(
                workspace,
                instanceId,
                lifecycleOperationId,
                resolvedPlan,
                assignment,
                operation,
                operationStore,
                CreateObservation(workspace, instanceId, lifecycleOperationId, resolvedPlan, assignment, operation, now));
            await AddLifecycleRunAsync(db, setupFixture);

            var runner = new AzureBicepProviderRunner(options, scope);
            var provider = new AzureElsaInstanceProvider(
                new AzureProviderOperationService(operationStore),
                operationStore,
                operationStore,
                options: new AzureElsaInstanceProviderOptions
                {
                    Enabled = true,
                    TemplateFingerprint = templateFingerprint,
                    ProviderScopeFingerprint = scopeFingerprint,
                    SubscriptionId = scope.SubscriptionId,
                    ResourceGroupNamePrefix = "rg-recovery"
                },
                recoveryObserver: runner,
                recoveryObservationStore: operationStore);
            var lifecycleStore = new EfCoreElsaInstanceLifecycleStore(
                db,
                EmptyResolutionInputSource.Instance,
                new FixedTimeProvider(now.AddMinutes(1)),
                recoveryObservationStore: operationStore);
            var target = Assert.IsType<ElsaInstanceProviderReconciliationTarget>(
                await lifecycleStore.GetTargetAsync(workspace.Id, lifecycleOperationId));
            var providerObservation = await provider.ObserveAsync(new(
                workspace.Id,
                instanceId,
                lifecycleOperationId,
                1,
                ElsaDesiredLifecycle.Running,
                new(
                    resolvedPlan.PlanId,
                    resolvedPlan.SchemaVersion,
                    resolvedPlan.ContentHash,
                    resolvedPlan.PlanUri),
                null,
                target.Instance.Version));
            Assert.Equal(ElsaInstanceProviderObservationKind.Confirmed, providerObservation.Kind);
            Assert.NotNull(providerObservation.RetryEvidence);
            var evidence = providerObservation.RetryEvidence!;
            var observation = Assert.IsType<AzureProviderRecoveryObservationRecord>(
                await ((IAzureProviderRecoveryObservationStore)operationStore).GetAndValidateRecordedAsync(
                    workspace.OrganizationId,
                    workspace.Id,
                    instanceId,
                    lifecycleOperationId,
                    1,
                    evidence.Reference,
                    evidence.Digest));
            Assert.Equal(AzureProviderRunnerStep.SqlBootstrapScript, observation.CompletedStep);
            Assert.Equal(target.Instance.Version, observation.ObservedInstanceVersion);

            var fixture = new ObservationFixture(
                workspace, instanceId, lifecycleOperationId, resolvedPlan, assignment, operation, operationStore, observation);
            var observationStore = (IAzureProviderRecoveryObservationStore)operationStore;
            var binding = await AcceptRecoveryAsync(db, fixture, observationStore, lifecycleRunAlreadyAdded: true);

            db.ChangeTracker.Clear();
            await using var providerDb = CreateMigratedContext(connection);
            var providerStore = new AzureProviderOperationStore(providerDb);
            var executor = new AzureProviderExecutor(
                providerStore,
                runner,
                leaseDuration: TimeSpan.FromMinutes(5),
                workerId: "sql-proof-executor",
                assignmentStore: providerStore);
            var providerAfterRestart = new AzureElsaInstanceProvider(
                new AzureProviderOperationService(providerStore),
                providerStore,
                providerStore,
                options: new AzureElsaInstanceProviderOptions
                {
                    Enabled = true,
                    TemplateFingerprint = templateFingerprint,
                    ProviderScopeFingerprint = scopeFingerprint,
                    SubscriptionId = scope.SubscriptionId,
                    ResourceGroupNamePrefix = "rg-recovery"
                },
                executor: executor,
                recoveryObserver: runner,
                recoveryObservationStore: providerStore);
            var result = await providerAfterRestart.RecoverAsync(CreateRecoveryRequest(fixture, binding));

            Assert.Equal(ElsaInstanceProviderRecoveryOutcome.RecoveryRequired, result.Outcome);
            Assert.Equal("azure.step.uncertain", result.Code);
            Assert.Equal(2, await CountAsync(sqlObservations));
            var persisted = Assert.IsType<AzureProviderOperation>(await providerStore.GetAsync(workspace.Id, operation.Id));
            Assert.Equal(AzureProviderOperationStatus.RecoveryRequired, persisted.Status);
            // The failed workload observation cannot advance the last confirmed checkpoint.
            Assert.Equal(AzureProviderOperationPhase.FoundationReady, persisted.Phase);
            Assert.Equal(AzureProviderRunnerStep.Workload, persisted.AttemptedStep);
            var lifecycle = await providerDb.ElsaInstanceOperations
                .AsNoTracking()
                .SingleAsync(x => x.Id == lifecycleOperationId);
            Assert.NotEqual(ElsaInstanceOperationState.Succeeded, lifecycle.State);
            Assert.NotEqual(WorkspaceDeploymentRunStatus.Succeeded,
                await providerDb.DeploymentRuns.AsNoTracking()
                    .Where(x => x.ElsaInstanceId == instanceId)
                    .Select(x => x.Status)
                    .SingleAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        static async Task<int> CountAsync(string path)
        {
            for (var attempt = 0; attempt < 20 && !File.Exists(path); attempt++)
                await Task.Delay(10);
            return File.Exists(path) ? (await File.ReadAllTextAsync(path)).Length : 0;
        }

        static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

        static string ShortHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];

        static async Task WriteExecutableAsync(string path, string contents)
        {
            await File.WriteAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
