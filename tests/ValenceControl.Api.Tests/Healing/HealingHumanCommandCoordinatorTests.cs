using ValenceControl.Api.Healing;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.Core.Security;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingHumanCommandCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T12:00:00Z");

    [Theory]
    [InlineData(WorkItemProjectionStatus.Stale, ProviderConnectionStatus.Active)]
    [InlineData(WorkItemProjectionStatus.Current, ProviderConnectionStatus.Suspended)]
    public async Task Pending_command_with_changed_provider_authority_is_terminally_rejected_and_audited(
        WorkItemProjectionStatus projectionStatus,
        ProviderConnectionStatus providerStatus)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"valence-control-healing-human-command-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<HealingDbContext>()
                .UseSqlite($"Data Source={databasePath};Default Timeout=30;Pooling=False")
                .Options;
            await using var dbContext = new HealingDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var workspaceId = Guid.NewGuid();
            var applicationId = Guid.NewGuid();
            var incidentId = Guid.NewGuid();
            var episodeId = Guid.NewGuid();
            var providerId = Guid.NewGuid();
            var commandId = Guid.NewGuid();
            dbContext.HealingConfigurations.Add(Configuration(workspaceId, applicationId));
            dbContext.HealingIncidents.Add(Incident(workspaceId, applicationId, incidentId));
            dbContext.ProviderConnections.Add(Provider(workspaceId, providerId, providerStatus));
            await dbContext.SaveChangesAsync();
            dbContext.IncidentEpisodes.Add(new IncidentEpisode
            {
                Id = episodeId,
                WorkspaceId = workspaceId,
                ApplicationId = applicationId,
                IncidentId = incidentId,
                OpenedAt = Now.AddMinutes(-2),
                ProducingRevisionsJson = "[]",
                Outcome = IncidentEpisodeOutcome.Active
            });
            await dbContext.SaveChangesAsync();
            dbContext.RepairWorkItemProjections.Add(Projection(
                workspaceId, applicationId, incidentId, episodeId, providerId, projectionStatus));
            dbContext.HumanCommands.Add(new HumanCommand
            {
                Id = commandId,
                WorkspaceId = workspaceId,
                ApplicationId = applicationId,
                IncidentId = incidentId,
                IdempotencyKey = $"retry:{incidentId:D}",
                Command = HealingHumanCommands.Retry,
                ProviderActorId = "12345",
                ProviderActorLogin = "maintainer",
                Status = HumanCommandStatus.Pending,
                RequestedAt = Now.AddMinutes(-1)
            });
            await dbContext.SaveChangesAsync();

            var timeProvider = new FixedTimeProvider();
            var auditService = new HealingAuditService(new HealingStore(dbContext), timeProvider);
            var commandService = new HumanProviderCommandService(
                new HealingHumanProviderCommandStore(dbContext, auditService, timeProvider));
            var coordinator = new HealingHumanCommandCoordinator(
                dbContext,
                new UnexpectedProviderPermissionProvider(),
                new WorkspacePermissionService(new UnexpectedWorkspacePermissionStore()),
                commandService);

            Assert.True((await coordinator.RunOnceAsync()));

            var command = await dbContext.HumanCommands.AsNoTracking().SingleAsync(x => x.Id == commandId);
            Assert.Equal(HumanCommandStatus.Rejected, command.Status);
            Assert.Equal("provider-permission-denied", command.ResultCode);
            Assert.Null(command.SafeResultDetail);
            Assert.Equal(Now, command.CompletedAt);
            var audit = await dbContext.Set<HealingAuditEvent>().AsNoTracking()
                .SingleAsync(x => x.AggregateType == "human-command" && x.AggregateId == commandId);
            Assert.Equal("human-command-rejected", audit.EventType);
            Assert.Equal("provider-permission-denied", audit.ReasonCode);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static HealingConfiguration Configuration(Guid workspaceId, Guid applicationId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        DiscoveryEnabled = true,
        RepairEnabled = true,
        SignalProfileVersion = HealingContractVersions.SignalProfile,
        DefaultAttemptLimit = 2,
        VerificationWindow = TimeSpan.FromMinutes(10),
        TimeBudget = TimeSpan.FromMinutes(10),
        ConcurrencyBudget = 1,
        InferenceBudget = 100,
        RepositoryRunBudget = 1,
        CreatedAt = Now.AddHours(-1),
        UpdatedAt = Now.AddHours(-1)
    };

    private static HealingIncident Incident(Guid workspaceId, Guid applicationId, Guid incidentId) => new()
    {
        Id = incidentId,
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        FingerprintVersion = "1",
        Fingerprint = new string('a', 64),
        RepairRepositoryKey = "github:repository-1",
        Status = HealingIncidentStatus.NeedsHuman,
        Severity = IncidentSeverity.Error,
        Classification = IncidentClassification.UnhandledRequest,
        FirstSeenAt = Now.AddHours(-1),
        LastSeenAt = Now,
        OccurrenceCount = 1
    };

    private static ProviderConnection Provider(
        Guid workspaceId,
        Guid providerId,
        ProviderConnectionStatus status) => new()
    {
        Id = providerId,
        WorkspaceId = workspaceId,
        Provider = "GitHub",
        InstallationId = "installation-1",
        RepositoryProviderId = "repository-1",
        RepositoryOwner = "acme",
        RepositoryName = "checkout",
        CredentialReference = "credential://github",
        Status = status,
        CreatedAt = Now.AddHours(-1),
        UpdatedAt = Now
    };

    private static RepairWorkItemProjection Projection(
        Guid workspaceId,
        Guid applicationId,
        Guid incidentId,
        Guid episodeId,
        Guid providerId,
        WorkItemProjectionStatus status) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        IncidentId = incidentId,
        EpisodeId = episodeId,
        ProviderConnectionId = providerId,
        MachineSummaryHash = new string('b', 64),
        ProjectionStatus = status
    };

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class UnexpectedProviderPermissionProvider : IGitHubRepositoryPermissionProvider
    {
        public ValueTask<GitHubRepositoryPermissionSnapshot> GetAsync(
            ProviderRepositoryReference repository,
            string providerActorId,
            string providerActorLogin,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Stale provider authority must be rejected before a provider call.");
    }

    private sealed class UnexpectedWorkspacePermissionStore : IWorkspacePermissionStore
    {
        public Task<DateTimeOffset?> GetWorkspaceMembershipCreatedAtAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Stale provider authority must be rejected before workspace permission lookup.");

        public Task<IReadOnlyList<WorkspacePermissionGrant>> GetPermissionGrantsAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkspacePermissionGrant>> ListPermissionGrantsAsync(Guid workspaceId, Guid? accountId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WorkspacePermissionAuditRecord>> ListPermissionAuditRecordsAsync(Guid workspaceId, Guid? accountId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspacePermissionGrant> GrantPermissionAsync(Guid workspaceId, GrantWorkspacePermissionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RevokeWorkspacePermissionResult> RevokePermissionAsync(Guid workspaceId, RevokeWorkspacePermissionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
