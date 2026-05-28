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
    }

    [Fact]
    public async Task Stale_command_recovery_marks_run_recovery_required()
    {
        var topology = await SeedTopologyAsync();
        var run = await QueueRunAsync(topology);
        var command = (await _store.PollPendingCommandsAsync(_workspaceId, topology.Engine.Id, 10, DateTimeOffset.UtcNow)).Single();
        var claimedAt = DateTimeOffset.Parse("2026-05-28T10:00:00Z");
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

    public void Dispose() => _db.Dispose();

    private async Task<WorkspaceDeploymentRun> QueueRunAsync(DeploymentTopology topology) =>
        await _store.CreateRunAsync(
            _workspaceId,
            new QueueWorkspaceDeploymentRunRequest(
                topology.Revision.Id,
                topology.Environment.Id,
                topology.Engine.Id,
                Guid.NewGuid(),
                _accountId,
                null),
            DateTimeOffset.UtcNow);

    private async Task<DeploymentTopology> SeedTopologyAsync(bool createRevision = true)
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, _accountId));
        var environment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await _store.RegisterEngineAsync(
            _workspaceId,
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
            ? await _store.CreateRevisionAsync(
                _workspaceId,
                new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "v1", "abc123", "{\"records\":[]}", _accountId))
            : null;
        return new DeploymentTopology(application, environment, engine, revision!);
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
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new CatalogDbContext(options);
    }

    private sealed record DeploymentTopology(
        WorkspaceDeploymentApplication Application,
        WorkspaceDeploymentEnvironment Environment,
        WorkspaceWorkflowEngine Engine,
        WorkspaceDesiredStateRevision Revision);
}
