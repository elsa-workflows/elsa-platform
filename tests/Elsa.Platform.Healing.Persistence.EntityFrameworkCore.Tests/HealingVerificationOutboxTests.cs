using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingVerificationOutboxTests
{
    [Fact]
    public async Task Concurrent_identical_appends_converge_on_one_durable_delivery()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-verification-outbox-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30;Pooling=False")
            .Options;
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        try
        {
            SeededIds seeded;
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                seeded = await SeedAsync(setup, now);
            }
            var signal = new RepairVerificationFailedSignal(
                HealingContractVersions.DeploymentProtocol, seeded.WorkspaceId, seeded.ApplicationId,
                seeded.EnvironmentId, seeded.IncidentId, seeded.EpisodeId, "abcdef1234567890",
                seeded.OccurrenceId, "matching-recurrence", now);
            var ready = 0;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<RepairVerificationFailedSignalAppendReceipt> AppendAsync()
            {
                if (Interlocked.Increment(ref ready) == 2)
                    start.SetResult();
                await start.Task;
                await using var db = new HealingDbContext(options);
                return await new HealingVerificationStore(db, new HealingStore(db)).AppendAsync(signal);
            }

            var receipts = await Task.WhenAll(AppendAsync(), AppendAsync());

            receipts.Select(x => x.DeliveryId).Distinct().Should().ContainSingle();
            receipts.Count(x => x.IsReplay).Should().Be(1);
            await using var verify = new HealingDbContext(options);
            (await verify.RepairVerificationFailureOutbox.CountAsync()).Should().Be(1);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Failure_signal_is_idempotently_leased_retried_and_acknowledged_by_a_deployment_consumer()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var seeded = await SeedAsync(fixture.Db, now);
        var outbox = new HealingVerificationStore(fixture.Db, new HealingStore(fixture.Db));
        var signal = new RepairVerificationFailedSignal(
            HealingContractVersions.DeploymentProtocol,
            seeded.WorkspaceId,
            seeded.ApplicationId,
            seeded.EnvironmentId,
            seeded.IncidentId,
            seeded.EpisodeId,
            "abcdef1234567890",
            seeded.OccurrenceId,
            "matching-recurrence",
            now);

        var appended = await outbox.AppendAsync(signal);
        var replay = await outbox.AppendAsync(signal);
        var firstLease = await outbox.TryLeaseNextAsync("deployment-system", now, TimeSpan.FromMinutes(1));

        replay.Should().Be(new RepairVerificationFailedSignalAppendReceipt(appended.DeliveryId, true, now));
        firstLease.Should().NotBeNull();
        firstLease!.Signal.Should().Be(signal);
        firstLease.AttemptCount.Should().Be(1);
        (await outbox.ReleaseAsync(firstLease.DeliveryId, firstLease.LeaseToken, now, now.AddMinutes(1), "delivery-unavailable"))
            .Should().BeTrue();
        (await outbox.TryLeaseNextAsync("deployment-system", now.AddSeconds(30), TimeSpan.FromMinutes(1)))
            .Should().BeNull();

        var retryLease = await outbox.TryLeaseNextAsync("deployment-system", now.AddMinutes(1), TimeSpan.FromMinutes(1));
        retryLease.Should().NotBeNull();
        retryLease!.AttemptCount.Should().Be(2);
        (await outbox.MarkDeliveredAsync(retryLease.DeliveryId, retryLease.LeaseToken, now.AddMinutes(1)))
            .Should().BeTrue();
        (await outbox.TryLeaseNextAsync("deployment-system", now.AddMinutes(2), TimeSpan.FromMinutes(1)))
            .Should().BeNull();

        var persisted = await fixture.Db.RepairVerificationFailureOutbox.AsNoTracking().SingleAsync();
        persisted.Status.Should().Be(RepairVerificationFailureDeliveryStatus.Delivered);
        persisted.DeliveredAt.Should().Be(now.AddMinutes(1));
        persisted.OutcomeCode.Should().Be("delivery-unavailable");
    }

    [Fact]
    public async Task Replayed_failure_signal_ignores_varying_detection_time_but_preserves_original_delivery()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var detectedAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var seeded = await SeedAsync(fixture.Db, detectedAt);
        var outbox = new HealingVerificationStore(fixture.Db, new HealingStore(fixture.Db));
        var signal = new RepairVerificationFailedSignal(
            HealingContractVersions.DeploymentProtocol,
            seeded.WorkspaceId,
            seeded.ApplicationId,
            seeded.EnvironmentId,
            seeded.IncidentId,
            seeded.EpisodeId,
            "abcdef1234567890",
            seeded.OccurrenceId,
            "matching-recurrence",
            detectedAt);

        var appended = await outbox.AppendAsync(signal);
        var replay = await outbox.AppendAsync(signal with { DetectedAt = detectedAt.AddMinutes(5) });

        replay.Should().Be(new RepairVerificationFailedSignalAppendReceipt(
            appended.DeliveryId,
            true,
            detectedAt));
        var persisted = await fixture.Db.RepairVerificationFailureOutbox.AsNoTracking().SingleAsync();
        persisted.CreatedAt.Should().Be(detectedAt);
        JsonSerializer.Deserialize<RepairVerificationFailedSignal>(persisted.PayloadJson)!
            .DetectedAt.Should().Be(detectedAt);
    }

    private static async Task<SeededIds> SeedAsync(HealingDbContext db, DateTimeOffset now)
    {
        var ids = new SeededIds(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.HealingIncidents.Add(new HealingIncident
        {
            Id = ids.IncidentId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            FingerprintVersion = "1", Fingerprint = new string('b', 64), RepairRepositoryKey = "observation-only",
            Status = HealingIncidentStatus.FailedVerification, FirstSeenAt = now, LastSeenAt = now, OccurrenceCount = 1
        });
        await db.SaveChangesAsync();
        db.IncidentEpisodes.Add(new IncidentEpisode
        {
            Id = ids.EpisodeId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            IncidentId = ids.IncidentId, OpenedAt = now, ProducingRevisionsJson = "[]", Outcome = IncidentEpisodeOutcome.Failed
        });
        await db.SaveChangesAsync();
        var incident = await db.HealingIncidents.SingleAsync();
        incident.ActiveEpisodeId = ids.EpisodeId;
        await db.SaveChangesAsync();
        db.HealingSignalInboxItems.Add(new HealingSignalInboxItem
        {
            Id = ids.InboxId, WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId,
            EnvironmentId = ids.EnvironmentId, IdempotencyKey = "verification-failure", ProfileVersion = "1.0",
            OccurredAt = now, AcceptedAt = now, RedactedEnvelopeJson = "{}", EnvelopeHash = new string('a', 64),
            Status = HealingInboxStatus.Completed
        });
        db.IncidentOccurrences.Add(new IncidentOccurrence
        {
            Id = ids.OccurrenceId, InboxItemId = ids.InboxId, IncidentId = ids.IncidentId, EpisodeId = ids.EpisodeId,
            WorkspaceId = ids.WorkspaceId, ApplicationId = ids.ApplicationId, EnvironmentId = ids.EnvironmentId,
            OccurrenceKey = "verification-failure", OccurredAt = now, AcceptedAt = now,
            ExceptionType = "System.InvalidOperationException", OperationName = "orders.load", NormalizedStackJson = "[]",
            FingerprintVersion = "1", Fingerprint = new string('b', 64), EvidenceDigest = new string('c', 64)
        });
        await db.SaveChangesAsync();
        return ids;
    }

    private sealed record SeededIds(
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId,
        Guid IncidentId,
        Guid EpisodeId,
        Guid OccurrenceId,
        Guid InboxId);
}
