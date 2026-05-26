using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class DeploymentWorkspacePersistenceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly DeploymentWorkspaceStore _store;
    private readonly Guid _workspaceId;
    private readonly Guid _accountId;

    public DeploymentWorkspacePersistenceTests()
    {
        _db = CreateDbContext();
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        var workspace = new Workspace { Name = "Deployment Workspace" };
        var account = new Account { DisplayName = "Deployment User", Email = "deployment@example.test" };
        _db.Accounts.Add(account);
        _db.Workspaces.Add(workspace);
        _db.SaveChanges();
        _workspaceId = workspace.Id;
        _accountId = account.Id;
        _store = new DeploymentWorkspaceStore(_db);
    }

    [Fact]
    public async Task Persists_workspace_deployment_cockpit_records()
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", "Claims workflows", null));
        var environment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        await _store.RegisterEngineAsync(
            _workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));

        var revision = await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "Baseline", "abc123", "{\"records\":[]}", null));

        _db.ChangeTracker.Clear();
        var cockpit = await _store.GetCockpitAsync(_workspaceId);

        cockpit.Applications.Should().ContainSingle(x => x.Id == application.Id.ToString("D"));
        cockpit.Applications.Single().Environments.Should().ContainSingle(x =>
            x.Id == environment.Id.ToString("D")
            && x.DesiredRevision.Revision == revision.RevisionNumber
            && x.DeploymentStatus == DeploymentStatus.Blocked);
        cockpit.Engines.Should().ContainSingle(x =>
            x.Name == "claims-prod"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api"
            && x.CredentialReference.VerificationStatus == CredentialVerificationStatus.Unverified);
    }

    [Fact]
    public async Task Persists_workspace_permission_grants()
    {
        await _store.GrantPermissionAsync(_workspaceId, new GrantWorkspacePermissionRequest(_accountId, WorkspaceDeploymentPermissions.Read, null));
        var grants = await _store.GetPermissionGrantsAsync(_workspaceId, _accountId);

        grants.Should().ContainSingle(x => x.Permission == WorkspaceDeploymentPermissions.Read && x.RevokedAt == null);
    }

    [Fact]
    public async Task Persists_structured_desired_state_records_and_keeps_revisions_immutable()
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var environment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var first = await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "Baseline", "abc123", """
                {"records":[{"kind":"Workflow","name":"Payment Retry","payload":{"version":1}}]}
                """, null));
        var second = await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "Update", "def456", """
                {"records":[{"kind":"Workflow","name":"Payment Retry","payload":{"version":2}}]}
                """, null));

        _db.ChangeTracker.Clear();
        var loadedFirst = await _store.GetRevisionAsync(_workspaceId, first.Id);
        var latest = await _store.GetLatestRevisionAsync(_workspaceId, environment.Id);
        var recordCount = await CountStructuredDesiredStateRecordsAsync();

        loadedFirst!.RevisionNumber.Should().Be(1);
        loadedFirst.DesiredStateJson.Should().Contain("\"version\":1");
        latest!.Id.Should().Be(second.Id);
        latest.RevisionNumber.Should().Be(2);
        recordCount.Should().Be(2);
    }

    [Fact]
    public async Task Persists_confirmations_runs_and_append_only_history()
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var sourceEnvironment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Stage", EnvironmentTier.Stage));
        var targetEnvironment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await _store.RegisterEngineAsync(
            _workspaceId,
            new RegisterWorkflowEngineRequest(
                targetEnvironment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                null,
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [],
                [],
                null));
        var revision = await _store.CreateRevisionAsync(
            _workspaceId,
            new CreateDesiredStateRevisionRequest(application.Id, sourceEnvironment.Id, "Candidate", "abc123", "{\"records\":[]}", null));
        var mutationStore = (IWorkspaceDeploymentMutationStore)_store;
        var now = DateTimeOffset.UtcNow;
        var confirmation = await mutationStore.CreateConfirmationAsync(
            _workspaceId,
            new CreateActionConfirmationRequest(ConfirmationActionType.Deploy, revision.Id.ToString("D"), _accountId),
            now);
        var usedConfirmation = await mutationStore.MarkConfirmationUsedAsync(_workspaceId, confirmation.Id, now.AddSeconds(1));
        var run = await mutationStore.CreateRunAsync(
            _workspaceId,
            new QueueWorkspaceDeploymentRunRequest(revision.Id, targetEnvironment.Id, engine.Id, confirmation.Id, _accountId),
            now.AddSeconds(2));

        var claimed = await mutationStore.ClaimNextQueuedRunAsync("worker-1", now.AddSeconds(3));
        var completed = await mutationStore.UpdateRunStatusAsync(_workspaceId, run.Id, WorkspaceDeploymentRunStatus.Succeeded, "Deployment run completed.", now.AddSeconds(4));
        var loaded = await mutationStore.GetRunAsync(_workspaceId, run.Id);
        var history = await mutationStore.GetRunHistoryAsync(_workspaceId, run.Id);

        usedConfirmation.UsedAt.Should().Be(now.AddSeconds(1));
        claimed!.Id.Should().Be(run.Id);
        completed.Status.Should().Be(WorkspaceDeploymentRunStatus.Succeeded);
        loaded!.Status.Should().Be(WorkspaceDeploymentRunStatus.Succeeded);
        history.Select(x => x.Status).Should().Equal(
            WorkspaceDeploymentRunStatus.Queued,
            WorkspaceDeploymentRunStatus.Running,
            WorkspaceDeploymentRunStatus.Succeeded);
    }

    [Fact]
    public async Task Persists_runtime_control_audit_records()
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var environment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await _store.RegisterEngineAsync(
            _workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                null,
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));
        var mutationStore = (IWorkspaceDeploymentMutationStore)_store;

        var execution = await mutationStore.RecordRuntimeControlExecutionAsync(
            _workspaceId,
            new RuntimeControlExecution(
                Guid.NewGuid(),
                _workspaceId,
                engine.Id,
                environment.Id,
                "reload-configuration",
                "Reload Configuration",
                CapabilityBoundary.EngineApi,
                "engine.reload-configuration",
                Guid.NewGuid(),
                _accountId,
                RuntimeControlExecutionStatus.Succeeded,
                DateTimeOffset.UtcNow,
                "Reload Configuration executed for claims-prod."));

        execution.Status.Should().Be(RuntimeControlExecutionStatus.Succeeded);
        (await CountRuntimeControlExecutionsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Projects_persisted_observability_and_drift_metadata()
    {
        var application = await _store.CreateApplicationAsync(_workspaceId, new CreateWorkflowApplicationRequest("Claims", null, null));
        var environment = await _store.CreateEnvironmentAsync(_workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await _store.RegisterEngineAsync(
            _workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                null,
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [],
                [],
                null));
        Guid? correlatedRevisionId = null;
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ObservabilityBindings (Id, WorkspaceId, EnvironmentId, EngineId, Kind, Provider, Status, Scope, CorrelatedRevisionId, Sample)
            VALUES ({Guid.NewGuid()}, {_workspaceId}, {environment.Id}, {engine.Id}, {"Logs"}, {"Azure Monitor"}, {"Connected"}, {"workspace:/prod"}, {correlatedRevisionId}, {"Imported status"});
            """);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DriftReportItems (Id, WorkspaceId, EnvironmentId, EngineId, Area, Desired, Observed, Action, DetectedAt)
            VALUES ({Guid.NewGuid()}, {_workspaceId}, {environment.Id}, {engine.Id}, {"RuntimeConfiguration"}, {"Concurrency 32"}, {"Concurrency 16"}, {"Review"}, {DateTimeOffset.UtcNow.UtcTicks});
            """);

        var cockpit = await _store.GetCockpitAsync(_workspaceId);

        cockpit.ObservabilityBindings.Should().ContainSingle(x =>
            x.Kind == ObservabilityBindingKind.Logs
            && x.Provider == "Azure Monitor"
            && x.Sample == "Imported status");
        cockpit.DriftReport.Should().ContainSingle(x =>
            x.Area == "RuntimeConfiguration"
            && x.Desired == "Concurrency 32"
            && x.Observed == "Concurrency 16"
            && x.Action == DriftAction.Review);
    }

    public void Dispose() => _db.Dispose();

    private async Task<long> CountStructuredDesiredStateRecordsAsync()
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM StructuredDesiredStateRecords";
        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt64(count);
    }

    private async Task<long> CountRuntimeControlExecutionsAsync()
    {
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RuntimeControlExecutions";
        var count = await command.ExecuteScalarAsync();
        return Convert.ToInt64(count);
    }

    private static CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new CatalogDbContext(options);
    }
}
