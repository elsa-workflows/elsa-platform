using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Reporting;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingReportingStoreTests
{
    [Fact]
    public async Task Overview_aggregates_the_full_filtered_set_but_bounds_recent_incidents()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        fixture.Db.HealingIncidents.AddRange(Enumerable.Range(0, 35).Select(index => new HealingIncident
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            FingerprintVersion = "1",
            Fingerprint = $"sha256:{index:x64}",
            RepairRepositoryKey = "observation-only",
            Status = HealingIncidentStatus.ObservationOnly,
            Severity = index % 2 == 0 ? IncidentSeverity.Error : IncidentSeverity.Warning,
            Classification = IncidentClassification.UnhandledRequest,
            FirstSeenAt = now.AddMinutes(-index - 1),
            LastSeenAt = now.AddMinutes(-index),
            OccurrenceCount = index + 1
        }));
        await fixture.Db.SaveChangesAsync();

        var source = await new HealingReportingStore(fixture.Db).LoadOverviewAsync(
            new(workspaceId, applicationId, From: now.AddDays(-1), To: now));

        Assert.Equal(35, source.OpenIncidents);
        Assert.Equal(new HealingNamedCount(nameof(HealingIncidentStatus.ObservationOnly), 35), Assert.Single(source.IncidentStates));
        Assert.Equal(new HealingRepairability(0, 35), source.Repairability);
        Assert.Equal(20, source.RecentIncidents.Count());
        Assert.Equal(source.RecentIncidents.Select(x => x.LastSeenAt).OrderDescending(), source.RecentIncidents.Select(x => x.LastSeenAt));
        Assert.Equal(0, source.Usage.Attempts);
    }

    [Fact]
    public async Task Audit_scope_resolution_remains_composable_and_pages_in_the_database()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var incidentIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        fixture.Db.HealingIncidents.AddRange(incidentIds.Select((incidentId, index) => new HealingIncident
        {
            Id = incidentId,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            FingerprintVersion = "1",
            Fingerprint = $"sha256:{index:x64}",
            RepairRepositoryKey = "observation-only",
            Status = HealingIncidentStatus.ObservationOnly,
            Severity = IncidentSeverity.Error,
            Classification = IncidentClassification.UnhandledRequest,
            FirstSeenAt = now,
            LastSeenAt = now,
            OccurrenceCount = 1
        }));
        fixture.Db.Set<HealingAuditEvent>().AddRange(Enumerable.Range(1, 200).Select(index => new HealingAuditEvent
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Sequence = (index - 1) / 5 + 1,
            AggregateType = "incident",
            AggregateId = incidentIds[(index - 1) % incidentIds.Length],
            EventType = "incident-observed",
            ReasonCode = "signal-accepted",
            ActorType = "system",
            ActorId = "healing-worker",
            CorrelationId = Guid.NewGuid(),
            SafeDetailJson = "{}",
            OccurredAt = now.AddSeconds(index)
        }));
        await fixture.Db.SaveChangesAsync();

        var store = new HealingReportingStore(fixture.Db);
        HealingAuditCursor? cursor = null;
        var observed = new List<HealingAuditEvent>();
        HealingAuditSourcePage page;
        do
        {
            page = await store.LoadAuditAsync(workspaceId, applicationId, null, cursor, 7);
            observed.AddRange(page.Items);
            cursor = page.HasMore && page.Items.Count > 0
                ? new(page.Items[^1].Sequence, page.Items[^1].Id)
                : null;
        } while (cursor is not null);

        Assert.Equal(200, observed.Count());
        Assert.Equal(observed.Select(x => x.Id).Distinct().Count(), observed.Select(x => x.Id).Count());
        Assert.Equal(observed.Select(x => x.Sequence).OrderDescending(), observed.Select(x => x.Sequence));
        Assert.Equal(40, observed[0].Sequence);
    }
}
