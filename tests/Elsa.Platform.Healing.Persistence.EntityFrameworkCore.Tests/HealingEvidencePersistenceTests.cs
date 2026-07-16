using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Repairs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingEvidencePersistenceTests
{
    [Fact]
    public async Task Evidence_is_workspace_scoped_and_immutable()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var incident = CreateIncident(workspaceId, applicationId);
        fixture.Db.HealingIncidents.Add(incident);
        await fixture.Db.SaveChangesAsync();
        IHealingEvidenceStore store = new HealingStore(fixture.Db);
        var bundle = CreateBundle(workspaceId, applicationId, incident.Id);

        var accepted = await store.TryAppendBundleAsync(bundle);
        var replay = await store.TryAppendBundleAsync(CreateBundle(workspaceId, applicationId, incident.Id, bundle.Id));
        var crossWorkspace = await store.FindBundleAsync(Guid.NewGuid(), bundle.Id);
        bundle.CanonicalJson = "{\"mutated\":true}";
        var mutation = () => fixture.Db.SaveChangesAsync();

        accepted.Should().BeTrue();
        replay.Should().BeFalse();
        crossWorkspace.Should().BeNull();
        await mutation.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*append-only*");
    }

    [Fact]
    public async Task Evidence_source_returns_only_the_requested_projected_fields()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var incident = CreateIncident(workspaceId, applicationId);
        var episode = new IncidentEpisode
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            IncidentId = incident.Id,
            OpenedAt = incident.FirstSeenAt,
            ProducingRevisionsJson = "[]",
            Outcome = IncidentEpisodeOutcome.Active
        };
        var inbox = new HealingSignalInboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            EnvironmentId = Guid.NewGuid(),
            IdempotencyKey = "occurrence-1",
            Source = HealingSignalSource.OpenTelemetry,
            ProfileVersion = "1.0",
            OccurredAt = incident.FirstSeenAt,
            AcceptedAt = incident.FirstSeenAt,
            RedactedEnvelopeJson = "{}",
            EnvelopeHash = new string('e', 64),
            Status = HealingInboxStatus.Completed
        };
        fixture.Db.HealingIncidents.Add(incident);
        fixture.Db.IncidentEpisodes.Add(episode);
        fixture.Db.HealingSignalInboxItems.Add(inbox);
        fixture.Db.IncidentOccurrences.Add(new IncidentOccurrence
        {
            Id = Guid.NewGuid(),
            InboxItemId = inbox.Id,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            EnvironmentId = Guid.NewGuid(),
            IncidentId = incident.Id,
            EpisodeId = episode.Id,
            OccurrenceKey = "occurrence-1",
            OccurredAt = incident.FirstSeenAt,
            AcceptedAt = incident.FirstSeenAt,
            Classification = IncidentClassification.UnhandledRequest,
            Severity = IncidentSeverity.Error,
            ExceptionType = "System.InvalidOperationException",
            OperationName = "workflow.execute",
            NormalizedStackJson = "[\"Acme.Activity.Execute\"]",
            TraceId = "trace-secret",
            FingerprintVersion = "1",
            Fingerprint = incident.Fingerprint,
            EvidenceTier = EvidenceTier.DefaultRedacted,
            EvidenceDigest = new string('a', 64)
        });
        await fixture.Db.SaveChangesAsync();
        var source = new HealingEvidenceSource(fixture.Db);

        var snapshot = await source.ReadAsync(new EvidenceSourceRequest(
            workspaceId,
            applicationId,
            incident.Id,
            new HashSet<EvidenceField> { EvidenceField.ExceptionType, EvidenceField.OperationName }));

        snapshot.Values.Should().ContainKey(EvidenceField.ExceptionType).WhoseValue.Should().Be("System.InvalidOperationException");
        snapshot.Values.Should().ContainKey(EvidenceField.OperationName).WhoseValue.Should().Be("workflow.execute");
        snapshot.Values.Should().NotContainKey(EvidenceField.TraceCorrelation);
    }

    private static HealingIncident CreateIncident(Guid workspaceId, Guid applicationId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        FingerprintVersion = "1",
        Fingerprint = new string('f', 64),
        RepairRepositoryKey = "observation-only",
        Status = HealingIncidentStatus.ReadyForRepair,
        Severity = IncidentSeverity.Error,
        Classification = IncidentClassification.UnhandledRequest,
        FirstSeenAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
        LastSeenAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
        OccurrenceCount = 1
    };

    private static EvidenceBundle CreateBundle(
        Guid workspaceId,
        Guid applicationId,
        Guid incidentId,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        IncidentId = incidentId,
        Tier = EvidenceTier.DefaultRedacted,
        CanonicalJson = "{\"exceptionType\":\"System.InvalidOperationException\"}",
        Digest = new string('a', 64),
        ProvenanceJson = "{}",
        OmissionsJson = "[]",
        SizeBytes = 64,
        CreatedAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z"),
        ExpiresAt = DateTimeOffset.Parse("2026-07-16T13:00:00Z")
    };
}
