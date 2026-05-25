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

    private static CatalogDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new CatalogDbContext(options);
    }
}
