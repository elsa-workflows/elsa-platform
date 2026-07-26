using Elsa.Platform.Api.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class TrustedDeploymentSafetyCapabilitySourceTests
{
    [Fact]
    public async Task Every_open_affected_environment_with_active_platform_rollback_capability_is_satisfied()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddImpact(fixture.EnvironmentId);
        fixture.AddImpact(fixture.SecondEnvironmentId);
        await fixture.Db.SaveChangesAsync();
        fixture.Deployments.Cockpit = Cockpit(fixture.ApplicationId,
            Environment(fixture.EnvironmentId, rollback: true),
            Environment(fixture.SecondEnvironmentId, rollback: true));

        var snapshot = await fixture.Source.GetAsync(
            fixture.WorkspaceId, fixture.ApplicationId, fixture.EpisodeId);

        snapshot.State.Should().Be(RepairPolicyObservationState.Satisfied);
        snapshot.ReasonCode.Should().Be("trusted-deployment-rollback-available");
        snapshot.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Theory]
    [InlineData(false, "Active", RepairPolicyObservationState.Failed, "trusted-deployment-rollback-unavailable")]
    [InlineData(true, "Archived", RepairPolicyObservationState.Failed, "trusted-deployment-tier-inactive")]
    public async Task Missing_capability_or_inactive_tier_fails_closed(
        bool rollback,
        string tierStatus,
        RepairPolicyObservationState expectedState,
        string expectedReason)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddImpact(fixture.EnvironmentId);
        await fixture.Db.SaveChangesAsync();
        fixture.Deployments.Cockpit = Cockpit(fixture.ApplicationId,
            Environment(fixture.EnvironmentId, rollback, tierStatus));

        var snapshot = await fixture.Source.GetAsync(
            fixture.WorkspaceId, fixture.ApplicationId, fixture.EpisodeId);

        snapshot.State.Should().Be(expectedState);
        snapshot.ReasonCode.Should().Be(expectedReason);
    }

    [Fact]
    public async Task Missing_or_ambiguous_deployment_authority_is_never_treated_as_available()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddImpact(fixture.EnvironmentId);
        await fixture.Db.SaveChangesAsync();

        fixture.Deployments.Cockpit = Cockpit(Guid.NewGuid(), Environment(fixture.EnvironmentId, rollback: true));
        var missingApplication = await fixture.Source.GetAsync(
            fixture.WorkspaceId, fixture.ApplicationId, fixture.EpisodeId);

        fixture.Deployments.Cockpit = Cockpit(fixture.ApplicationId,
            Environment(fixture.EnvironmentId, rollback: true),
            Environment(fixture.EnvironmentId, rollback: true));
        var ambiguousEnvironment = await fixture.Source.GetAsync(
            fixture.WorkspaceId, fixture.ApplicationId, fixture.EpisodeId);

        missingApplication.State.Should().Be(RepairPolicyObservationState.Missing);
        missingApplication.ReasonCode.Should().Be("trusted-deployment-application-missing");
        ambiguousEnvironment.State.Should().Be(RepairPolicyObservationState.Ambiguous);
        ambiguousEnvironment.ReasonCode.Should().Be("trusted-deployment-environment-ambiguous");
    }

    [Fact]
    public async Task Closed_environment_impacts_do_not_weaken_current_rollback_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.AddImpact(fixture.EnvironmentId);
        fixture.AddImpact(fixture.SecondEnvironmentId, closed: true);
        await fixture.Db.SaveChangesAsync();
        fixture.Deployments.Cockpit = Cockpit(fixture.ApplicationId,
            Environment(fixture.EnvironmentId, rollback: true),
            Environment(fixture.SecondEnvironmentId, rollback: false));

        var snapshot = await fixture.Source.GetAsync(
            fixture.WorkspaceId, fixture.ApplicationId, fixture.EpisodeId);

        snapshot.State.Should().Be(RepairPolicyObservationState.Satisfied);
    }

    [Fact]
    public async Task No_open_affected_environment_is_missing_trusted_authority()
    {
        await using var fixture = await Fixture.CreateAsync();

        var snapshot = await fixture.Source.GetAsync(
            fixture.WorkspaceId, fixture.ApplicationId, fixture.EpisodeId);

        snapshot.State.Should().Be(RepairPolicyObservationState.Missing);
        snapshot.ReasonCode.Should().Be("affected-environment-missing");
    }

    private static DeploymentCockpit Cockpit(Guid applicationId, params EnvironmentSummary[] environments) =>
        new(
            [new WorkflowApplication(applicationId.ToString("D"), "app", "workspace", environments)],
            [], [], [], [], [], []);

    private static EnvironmentSummary Environment(Guid id, bool rollback, string tierStatus = "Active") =>
        new(
            id.ToString("D"), "environment", EnvironmentTier.Production, DeploymentHealth.Healthy,
            new DesiredStateRevision(Guid.NewGuid().ToString("D"), 1, new string('a', 40), "revision", DateTimeOffset.UtcNow),
            1, DeploymentStatus.Succeeded, DriftStatus.InSync, [], "Production", tierStatus,
            rollback ? [DeploymentTierCapabilities.RollbackEnabled] : []);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, HealingDbContext db)
        {
            _connection = connection;
            Db = db;
            Deployments = new CockpitDeploymentStore();
            Source = new TrustedDeploymentSafetyCapabilitySource(db, Deployments);
        }

        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public Guid ApplicationId { get; } = Guid.NewGuid();
        public Guid IncidentId { get; } = Guid.NewGuid();
        public Guid EpisodeId { get; } = Guid.NewGuid();
        public Guid EnvironmentId { get; } = Guid.NewGuid();
        public Guid SecondEnvironmentId { get; } = Guid.NewGuid();
        public HealingDbContext Db { get; }
        public CockpitDeploymentStore Deployments { get; }
        public TrustedDeploymentSafetyCapabilitySource Source { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new HealingDbContext(new DbContextOptionsBuilder<HealingDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, db);
            db.HealingIncidents.Add(new HealingIncident
            {
                Id = fixture.IncidentId,
                WorkspaceId = fixture.WorkspaceId,
                ApplicationId = fixture.ApplicationId,
                FingerprintVersion = "1",
                Fingerprint = new string('f', 64),
                RepairRepositoryKey = "github:1",
                Status = HealingIncidentStatus.PullRequestOpen,
                Severity = IncidentSeverity.Error,
                Classification = IncidentClassification.UnhandledRequest,
                FirstSeenAt = DateTimeOffset.UtcNow,
                LastSeenAt = DateTimeOffset.UtcNow,
                OccurrenceCount = 1
            });
            await db.SaveChangesAsync();
            db.IncidentEpisodes.Add(new IncidentEpisode
            {
                Id = fixture.EpisodeId,
                WorkspaceId = fixture.WorkspaceId,
                ApplicationId = fixture.ApplicationId,
                IncidentId = fixture.IncidentId,
                OpenedAt = DateTimeOffset.UtcNow,
                ProducingRevisionsJson = "[]",
                Outcome = IncidentEpisodeOutcome.Active
            });
            await db.SaveChangesAsync();
            return fixture;
        }

        public void AddImpact(Guid environmentId, bool closed = false) =>
            Db.EnvironmentImpacts.Add(new EnvironmentImpact
            {
                Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, ApplicationId = ApplicationId,
                EpisodeId = EpisodeId, EnvironmentId = environmentId,
                FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
                OccurrenceCount = 1, ProducingRevisionsJson = "[]",
                VerificationStatus = closed ? VerificationOutcome.Waived : VerificationOutcome.PendingDeployment,
                OccurrenceThreshold = 1, ClassificationPolicyVersion = "1",
                ClassificationPolicyHash = new string('a', 64),
                ClosedAt = closed ? DateTimeOffset.UtcNow : null
            });

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CockpitDeploymentStore : IWorkspaceDeploymentStore
    {
        public DeploymentCockpit Cockpit { get; set; } = new([], [], [], [], [], [], []);
        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(Cockpit);
        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(Guid workspaceId, Guid applicationId, UpdateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(Guid workspaceId, Guid environmentId, UpdateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(Guid workspaceId, Guid engineId, UpdateWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(Guid workspaceId, Guid revisionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine?> GetEngineAsync(Guid workspaceId, Guid engineId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
