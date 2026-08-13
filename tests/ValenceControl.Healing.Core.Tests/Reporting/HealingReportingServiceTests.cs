using ValenceControl.Healing.Core.Reporting;

namespace ValenceControl.Healing.Core.Tests.Reporting;

public sealed class HealingReportingServiceTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task One_sided_windows_are_bounded_and_overlong_or_future_windows_are_rejected()
    {
        var store = new StubStore(EmptySource);
        var service = new HealingReportingService(store, new FixedTimeProvider(Now));

        await service.GetUsageAsync(new(WorkspaceId, To: Now.AddDays(-10)));
        Assert.Equal(Now.AddDays(-376), store.LastOverviewQuery!.From);
        Assert.Equal(Now.AddDays(-10), store.LastOverviewQuery.To);

        Func<Task> overlong = async () => await service.GetUsageAsync(new(WorkspaceId, From: Now.AddDays(-367)));
        Func<Task> future = async () => await service.GetUsageAsync(new(WorkspaceId, From: Now.AddDays(2)));
        var overlongException = await Assert.ThrowsAsync<ArgumentException>(overlong);
        Assert.Matches(".*cannot exceed 366 days.*", overlongException.Message);
        var futureException = await Assert.ThrowsAsync<ArgumentException>(future);
        Assert.Matches(".*cannot be in the future.*", futureException.Message);
    }

    [Fact]
    public async Task Omitted_reporting_window_defaults_to_a_bounded_interval()
    {
        var store = new StubStore(EmptySource);
        var service = new HealingReportingService(store, new FixedTimeProvider(Now));

        await service.GetOverviewAsync(new(WorkspaceId));

        Assert.Equal(Now.AddDays(-366), store.LastOverviewQuery!.From);
        Assert.Equal(Now, store.LastOverviewQuery.To);
    }

    [Fact]
    public async Task Usage_projection_returns_the_bounded_store_aggregate()
    {
        var expected = EmptyUsage with
        {
            InputUnits = long.MaxValue,
            OutputUnits = long.MaxValue,
            RepositoryRuns = long.MaxValue,
            AgentDurationSeconds = TimeSpan.MaxValue.TotalSeconds,
            RepositoryRunDurationSeconds = TimeSpan.MaxValue.TotalSeconds
        };
        var store = new StubStore(EmptySource with { Usage = expected });
        var service = new HealingReportingService(store, new FixedTimeProvider(Now));

        var usage = await service.GetUsageAsync(new(WorkspaceId));

        Assert.Equal(long.MaxValue, usage.InputUnits);
        Assert.Equal(long.MaxValue, usage.OutputUnits);
        Assert.Equal(long.MaxValue, usage.RepositoryRuns);
        Assert.Equal(TimeSpan.MaxValue.TotalSeconds, usage.AgentDurationSeconds);
        Assert.Equal(TimeSpan.MaxValue.TotalSeconds, usage.RepositoryRunDurationSeconds);
    }

    [Fact]
    public async Task Audit_projection_defensively_redacts_credential_shaped_actor_and_detail_values()
    {
        var auditEvent = new HealingAuditEvent
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Sequence = 1, AggregateType = "incident",
            AggregateId = Guid.NewGuid(), EventType = "repair-started", ReasonCode = "policy-allowed",
            ActorType = "provider-user", ActorId = "ghp_Aa123456789012345678901234567890",
            CorrelationId = Guid.NewGuid(), SafeDetailJson = "{\"status\":\"github_pat_Aa123456789012345678901234\"}",
            OccurredAt = Now
        };
        var service = new HealingReportingService(new StubStore(EmptySource, new([auditEvent], false)), new FixedTimeProvider(Now));

        var page = await service.GetAuditAsync(new(WorkspaceId));

        Assert.Single(page.Items);
        Assert.Equal("redacted", page.Items[0].ActorId);
        Assert.Empty(page.Items[0].Details);
    }

    private static readonly HealingUsageReport EmptyUsage = new(
        null, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static readonly HealingOverviewSource EmptySource = new(
        [], [], 0, [], [], new(0, 0), new(0, 0, 0, 0), [], EmptyUsage, []);

    private sealed class StubStore(HealingOverviewSource source, HealingAuditSourcePage? auditPage = null) : IHealingReportingStore
    {
        public HealingOverviewQuery? LastOverviewQuery { get; private set; }

        public ValueTask<HealingOverviewSource> LoadOverviewAsync(HealingOverviewQuery query, CancellationToken cancellationToken = default)
        {
            LastOverviewQuery = query;
            return ValueTask.FromResult(source);
        }

        public ValueTask<HealingAuditSourcePage> LoadAuditAsync(Guid workspaceId, Guid? applicationId, Guid? incidentId, HealingAuditCursor? before, int take, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(auditPage ?? new HealingAuditSourcePage([], false));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
