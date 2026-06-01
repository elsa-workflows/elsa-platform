using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class DeploymentCommandPersistenceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly DeploymentWorkspaceStore _store;
    private readonly Guid _workspaceId;
    private readonly Guid _accountId;

    public DeploymentCommandPersistenceTests()
    {
        _db = CreateDbContext();
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        var workspace = new Workspace { Name = "Runtime Command Workspace" };
        var account = new Account { DisplayName = "Runtime User", Email = "runtime@example.test" };
        _db.Workspaces.Add(workspace);
        _db.Accounts.Add(account);
        _db.SaveChanges();
        _workspaceId = workspace.Id;
        _accountId = account.Id;
        _store = new DeploymentWorkspaceStore(_db);
    }

    [Fact]
    public async Task Creating_run_creates_pending_command_for_target_engine()
    {
        var topology = await SeedTopologyAsync();

        var run = await QueueRunAsync(topology);
        var commands = await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow);

        commands.Should().ContainSingle(x =>
            x.RunId == run.Id
            && x.Action == DeploymentCommandAction.Deploy
            && x.Revision!.RevisionId == topology.Revision.Id);
    }

    [Fact]
    public async Task Artifact_backed_revision_projects_artifact_reference_into_command()
    {
        var topology = await SeedTopologyAsync(createRevision: false);
        var artifact = await _store.RegisterArtifactAsync(
            _workspaceId,
            ArtifactRegistration("sha256:payment-retry", "/tmp/payment-retry"));
        var desiredStateJson = $$"""
            {
              "records": [
                {
                  "kind": "ArtifactReference",
                  "name": "Payment Retry",
                  "payload": {
                    "artifactRecordId": "{{artifact.Id:D}}",
                    "artifactId": "{{artifact.ArtifactId}}",
                    "artifactTypeId": "{{artifact.ArtifactTypeId}}",
                    "contentDigest": {
                      "algorithm": "{{artifact.ContentDigest.Algorithm}}",
                      "value": "{{artifact.ContentDigest.Value}}"
                    }
                  }
                }
              ]
            }
            """;
        var revision = await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(topology.Application.Id, topology.Environment.Id, "Artifact v1", "abc123", desiredStateJson, _accountId));

        await QueueRunAsync(topology with { Revision = revision });
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();

        command.Artifact.Should().NotBeNull();
        command.Artifact!.ArtifactRecordId.Should().Be(artifact.Id);
        command.Artifact.ArtifactId.Should().Be(artifact.ArtifactId);
        command.Artifact.ArtifactTypeId.Should().Be(ArtifactTypeIds.ElsaWorkflowDefinition);
        command.Artifact.ContentDigest.Should().Be(artifact.ContentDigest);
    }

    [Fact]
    public async Task Artifact_backed_revision_rejects_missing_artifact_reference()
    {
        var topology = await SeedTopologyAsync(createRevision: false);
        var desiredStateJson = """
            {
              "records": [
                {
                  "kind": "ArtifactReference",
                  "name": "Payment Retry",
                  "payload": {
                    "artifactId": "sha256:missing"
                  }
                }
              ]
            }
            """;
        var revision = await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(topology.Application.Id, topology.Environment.Id, "Missing artifact", "abc123", desiredStateJson, _accountId));

        var act = () => QueueRunAsync(topology with { Revision = revision });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Artifact-backed revision references an artifact that is not visible in the workspace.");
    }

    [Fact]
    public async Task Completing_artifact_command_rejects_observed_digest_mismatch()
    {
        var topology = await SeedTopologyAsync(createRevision: false);
        var artifact = await _store.RegisterArtifactAsync(
            _workspaceId,
            ArtifactRegistration("sha256:payment-retry", "/tmp/payment-retry"));
        var revision = await CreateArtifactBackedRevisionAsync(topology, artifact);
        await QueueRunAsync(topology with { Revision = revision });
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            DateTimeOffset.UtcNow);

        var complete = () => _store.CompleteCommandAsync(
            _workspaceId,
            command.Id,
            new CompleteDeploymentCommandRequest("lease-1", new WorkspaceArtifactDigest("sha256", "wrong"), "elsa://workflow/payment-retry", []),
            DateTimeOffset.UtcNow);

        await complete.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Observed artifact digest does not match command artifact digest.");
    }

    [Fact]
    public async Task Claim_enforces_single_active_lease()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();

        var claimed = await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            DateTimeOffset.UtcNow);
        var duplicate = () => _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-2", TimeSpan.FromMinutes(5)),
            "lease-2",
            DateTimeOffset.UtcNow);

        claimed.Status.Should().Be(DeploymentCommandStatus.Claimed);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Claim_is_atomic_across_db_contexts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-command-claim-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        try
        {
            await using (var seedDb = CreateDbContext(connectionString))
            {
                await seedDb.Database.EnsureCreatedAsync();
                var workspace = new Workspace { Name = "Runtime Command Workspace" };
                var account = new Account { DisplayName = "Runtime User", Email = "runtime-concurrent@example.test" };
                seedDb.Workspaces.Add(workspace);
                seedDb.Accounts.Add(account);
                await seedDb.SaveChangesAsync();
                var seedStore = new DeploymentWorkspaceStore(seedDb);
                var topology = await SeedTopologyAsync(seedStore, workspace.Id, account.Id);
                await QueueRunAsync(seedStore, workspace.Id, account.Id, topology);
                var command = (await seedStore.PollPendingCommandsAsync(workspace.Id, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();

                await using var firstDb = CreateDbContext(connectionString);
                await using var secondDb = CreateDbContext(connectionString);
                var firstStore = new DeploymentWorkspaceStore(firstDb);
                var secondStore = new DeploymentWorkspaceStore(secondDb);
                var readyCount = 0;
                var readyGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                var firstClaim = StartClaimContender(firstStore, workspace.Id, topology.Engine.Id, command.Id, "worker-1", "lease-1");
                var secondClaim = StartClaimContender(secondStore, workspace.Id, topology.Engine.Id, command.Id, "worker-2", "lease-2");
                await readyGate.Task.WaitAsync(TimeSpan.FromSeconds(5));
                startGate.SetResult();
                var claims = await Task.WhenAll(firstClaim, secondClaim);

                claims.Count(x => x is not null).Should().Be(1);

                Task<DeploymentCommand?> StartClaimContender(
                    DeploymentWorkspaceStore store,
                    Guid contenderWorkspaceId,
                    Guid contenderEngineId,
                    Guid contenderCommandId,
                    string workerId,
                    string leaseToken) =>
                    Task.Run(async () =>
                    {
                        if (Interlocked.Increment(ref readyCount) == 2)
                            readyGate.SetResult();
                        await startGate.Task;
                        return await TryClaimAsync(store, contenderWorkspaceId, contenderEngineId, contenderCommandId, workerId, leaseToken);
                    });
            }
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Claim_rejects_command_before_available_at()
    {
        var topology = await SeedTopologyAsync();
        var run = await QueueRunAsync(topology);
        var now = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
        var delayed = await _store.CreateCommandAsync(
            _workspaceId,
            new CreateDeploymentCommandRequest(
                run.Id,
                topology.Environment.Id,
                topology.Engine.Id,
                DeploymentCommandAction.Deploy,
                null,
                new DeploymentCommandRevisionReference(topology.Revision.Id),
                $"delayed-{Guid.NewGuid():N}",
                now.AddMinutes(5),
                null),
            now);

        var claim = () => _store.ClaimCommandAsync(
            _workspaceId,
            delayed.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            now);

        await claim.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Command is not available.");
    }

    [Fact]
    public async Task Heartbeat_refreshes_parent_run_worker_heartbeat()
    {
        var topology = await SeedTopologyAsync();
        var run = await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        var claimedAt = run.QueuedAt.AddSeconds(1);
        var heartbeatAt = claimedAt.AddMinutes(4);
        await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(10)),
            "lease-1",
            claimedAt);

        await _store.HeartbeatCommandAsync(
            _workspaceId,
            command.Id,
            new DeploymentCommandHeartbeatRequest("lease-1", "worker-1"),
            heartbeatAt);
        var refreshed = await _store.GetRunAsync(_workspaceId, run.Id);

        refreshed!.WorkerHeartbeatAt.Should().Be(heartbeatAt);
        refreshed.WorkerId.Should().Be("worker-1");
    }

    [Fact]
    public async Task Redeploying_same_revision_creates_distinct_command_idempotency_keys()
    {
        var topology = await SeedTopologyAsync();

        await QueueRunAsync(topology);
        await QueueRunAsync(topology);
        var commands = await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow);

        commands.Should().HaveCount(2);
        commands.Select(x => x.IdempotencyKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Completing_command_updates_run_history_and_deployed_revision()
    {
        var topology = await SeedTopologyAsync();
        var run = await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            DateTimeOffset.UtcNow);

        var completed = await _store.CompleteCommandAsync(
            _workspaceId,
            command.Id,
            new CompleteDeploymentCommandRequest("lease-1", new WorkspaceArtifactDigest("sha256", "observed"), "elsa://workflow/payment-retry", []),
            DateTimeOffset.UtcNow);
        var completedRun = await _store.GetRunAsync(_workspaceId, run.Id);
        var history = await _store.GetRunHistoryAsync(_workspaceId, run.Id);
        _db.ChangeTracker.Clear();
        var cockpit = await _store.GetCockpitAsync(_workspaceId);

        completed.Status.Should().Be(DeploymentCommandStatus.Completed);
        completedRun!.Status.Should().Be(WorkspaceDeploymentRunStatus.Succeeded);
        history.Should().Contain(x => x.Message == "Runtime command completed.");
        cockpit.Applications.Single().Environments.Single().DeployedRevision.Should().Be(topology.Revision.RevisionNumber);
        var commandSummary = cockpit.History.Single().Commands.Single();
        commandSummary.Id.Should().Be(command.Id);
        commandSummary.Status.Should().Be(DeploymentCommandStatus.Completed);
        commandSummary.ProgressMessage.Should().BeNull();
        commandSummary.RuntimeReference.Should().Be("elsa://workflow/payment-retry");
    }

    [Fact]
    public async Task Completed_command_requires_same_lease_for_idempotent_replay()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            DateTimeOffset.UtcNow);
        await _store.CompleteCommandAsync(
            _workspaceId,
            command.Id,
            new CompleteDeploymentCommandRequest("lease-1", null, "elsa://workflow/payment-retry", []),
            DateTimeOffset.UtcNow);

        var replay = await _store.CompleteCommandAsync(
            _workspaceId,
            command.Id,
            new CompleteDeploymentCommandRequest("lease-1", null, "elsa://workflow/payment-retry", []),
            DateTimeOffset.UtcNow);
        var wrongLease = () => _store.CompleteCommandAsync(
            _workspaceId,
            command.Id,
            new CompleteDeploymentCommandRequest("lease-2", null, "elsa://workflow/payment-retry", []),
            DateTimeOffset.UtcNow);

        replay.Status.Should().Be(DeploymentCommandStatus.Completed);
        await wrongLease.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Command lease token is invalid.");
    }

    [Fact]
    public async Task Stale_command_recovery_marks_run_recovery_required()
    {
        var topology = await SeedTopologyAsync();
        var run = await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        var claimedAt = run.QueuedAt.AddSeconds(1);
        await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            claimedAt);

        var recovered = await _store.MarkStaleCommandsRecoveryRequiredAsync(claimedAt.AddMinutes(20), TimeSpan.FromMinutes(10));
        var recoveredRun = await _store.GetRunAsync(_workspaceId, run.Id);

        recovered.Should().Be(1);
        recoveredRun!.Status.Should().Be(WorkspaceDeploymentRunStatus.RecoveryRequired);
    }

    [Fact]
    public async Task Webhook_notification_persists_safe_trigger_metadata()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();

        var notification = await _store.CreateWebhookNotificationAsync(
            _workspaceId,
            topology.Engine.Id,
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);
        await using var commandReader = _db.Database.GetDbConnection().CreateCommand();
        commandReader.CommandText = "SELECT SafePayloadJson FROM DeploymentCommandWebhookNotifications WHERE Id = $id";
        var parameter = commandReader.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = notification.Id;
        commandReader.Parameters.Add(parameter);
        var rawPayload = (string)(await commandReader.ExecuteScalarAsync())!;

        notification.Status.Should().Be(WebhookNotificationStatus.Pending);
        rawPayload.Should().Be("{\"reason\":\"command-available\"}");
    }

    [Fact]
    public async Task Webhook_dispatch_targets_include_runtime_endpoint_and_safe_payload()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        var notification = await _store.CreateWebhookNotificationAsync(
            _workspaceId,
            topology.Engine.Id,
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);

        var targets = await _store.ListPendingWebhookNotificationTargetsAsync(10, DateTimeOffset.UtcNow);

        targets.Should().ContainSingle(x =>
            x.Id == notification.Id
            && x.WorkspaceId == _workspaceId
            && x.EngineId == topology.Engine.Id
            && x.CommandId == command.Id
            && x.EngineBaseUrl == topology.Engine.BaseUrl
            && x.SafePayloadJson == "{\"reason\":\"command-available\"}");
    }

    [Fact]
    public async Task Webhook_dispatch_status_transitions_remove_notification_from_pending_targets()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        var sentAt = DateTimeOffset.Parse("2026-05-28T12:00:00Z");
        var sent = await _store.CreateWebhookNotificationAsync(
            _workspaceId,
            topology.Engine.Id,
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);
        var failed = await _store.CreateWebhookNotificationAsync(
            _workspaceId,
            topology.Engine.Id,
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);
        var skipped = await _store.CreateWebhookNotificationAsync(
            _workspaceId,
            topology.Engine.Id,
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);

        var sentResult = await _store.MarkWebhookNotificationSentAsync(_workspaceId, sent.Id, sentAt);
        var failedResult = await _store.MarkWebhookNotificationFailedAsync(_workspaceId, failed.Id, sentAt);
        var skippedResult = await _store.MarkWebhookNotificationSkippedAsync(_workspaceId, skipped.Id, sentAt);
        var pendingTargets = await _store.ListPendingWebhookNotificationTargetsAsync(10, DateTimeOffset.UtcNow);

        sentResult.Status.Should().Be(WebhookNotificationStatus.Sent);
        sentResult.SentAt.Should().Be(sentAt);
        failedResult.Status.Should().Be(WebhookNotificationStatus.Failed);
        skippedResult.Status.Should().Be(WebhookNotificationStatus.Skipped);
        pendingTargets.Should().NotContain(x => x.Id == sent.Id || x.Id == failed.Id || x.Id == skipped.Id);
    }

    [Fact]
    public async Task Webhook_notification_rejects_wrong_engine()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();

        var create = () => _store.CreateWebhookNotificationAsync(
            _workspaceId,
            Guid.NewGuid(),
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);

        await create.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Command does not target the requested runtime engine.");
    }

    [Fact]
    public async Task Webhook_notification_rejects_non_pending_command()
    {
        var topology = await SeedTopologyAsync();
        await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        await _store.ClaimCommandAsync(
            _workspaceId,
            command.Id,
            new ClaimDeploymentCommandRequest(topology.Engine.Id, "worker-1", TimeSpan.FromMinutes(5)),
            "lease-1",
            DateTimeOffset.UtcNow);

        var create = () => _store.CreateWebhookNotificationAsync(
            _workspaceId,
            topology.Engine.Id,
            command.Id,
            "{\"reason\":\"command-available\"}",
            DateTimeOffset.UtcNow);

        await create.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Command is not pending.");
    }

    public void Dispose() => _db.Dispose();

    private async Task<WorkspaceDeploymentRun> QueueRunAsync(DeploymentTopology topology) =>
        await QueueRunAsync(_store, _workspaceId, _accountId, topology);

    private async Task<DeploymentTopology> SeedTopologyAsync(bool createRevision = true)
    {
        return await SeedTopologyAsync(_store, _workspaceId, _accountId, createRevision);
    }

    private async Task<WorkspaceDesiredStateRevision> CreateArtifactBackedRevisionAsync(
        DeploymentTopology topology,
        WorkspaceArtifact artifact)
    {
        var desiredStateJson = $$"""
            {
              "records": [
                {
                  "kind": "ArtifactReference",
                  "name": "Payment Retry",
                  "payload": {
                    "artifactRecordId": "{{artifact.Id:D}}",
                    "artifactId": "{{artifact.ArtifactId}}",
                    "artifactTypeId": "{{artifact.ArtifactTypeId}}",
                    "contentDigest": {
                      "algorithm": "{{artifact.ContentDigest.Algorithm}}",
                      "value": "{{artifact.ContentDigest.Value}}"
                    }
                  }
                }
              ]
            }
            """;
        return await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(topology.Application.Id, topology.Environment.Id, "Artifact v1", "abc123", desiredStateJson, _accountId));
    }

    private static async Task<WorkspaceDeploymentRun> QueueRunAsync(
        DeploymentWorkspaceStore store,
        Guid workspaceId,
        Guid accountId,
        DeploymentTopology topology) =>
        await store.CreateRunAsync(
            workspaceId,
            new QueueWorkspaceDeploymentRunRequest(
                topology.Revision.Id,
                topology.Environment.Id,
                topology.Engine.Id,
                Guid.NewGuid(),
                accountId,
                null),
            DateTimeOffset.UtcNow);

    private static async Task<DeploymentTopology> SeedTopologyAsync(
        DeploymentWorkspaceStore store,
        Guid workspaceId,
        Guid accountId,
        bool createRevision = true)
    {
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims", null, accountId));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://runtime.example.test",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/runtime",
                [new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)],
                [],
                "container-apps"));
        var revision = createRevision
            ? await store.CreateRevisionAsync(
                workspaceId,
                new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "v1", "abc123", "{\"records\":[]}", accountId))
            : null;
        return new DeploymentTopology(application, environment, engine, revision!);
    }

    private static async Task<DeploymentCommand?> TryClaimAsync(
        DeploymentWorkspaceStore store,
        Guid workspaceId,
        Guid engineId,
        Guid commandId,
        string workerId,
        string leaseToken)
    {
        try
        {
            return await store.ClaimCommandAsync(
                workspaceId,
                commandId,
                new ClaimDeploymentCommandRequest(engineId, workerId, TimeSpan.FromMinutes(5)),
                leaseToken,
                DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private RegisterWorkspaceArtifactRequest ArtifactRegistration(string artifactId, string reference) =>
        new(
            artifactId,
            ArtifactLayoutConstants.LayoutVersion,
            new WorkspaceArtifactDigest("sha256", artifactId.Replace("sha256:", "", StringComparison.Ordinal)),
            WorkspaceArtifactFormat.Folder,
            "local",
            reference,
            new WorkspaceArtifactManifestSummary("claims", "1.0.0", "prod"),
            [new WorkspaceArtifactResourceSummary("workflowDefinition", "payment-retry", null, "1", new WorkspaceArtifactDigest("sha256", "workflow-hash"))],
            [],
            _accountId,
            ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeIds.ElsaWorkflowDefinition);

    private static CatalogDbContext CreateDbContext()
    {
        return CreateDbContext("Data Source=:memory:");
    }

    private static CatalogDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new CatalogDbContext(options);
    }

    private sealed record DeploymentTopology(
        WorkspaceDeploymentApplication Application,
        WorkspaceDeploymentEnvironment Environment,
        WorkspaceWorkflowEngine Engine,
        WorkspaceDesiredStateRevision Revision);
}
