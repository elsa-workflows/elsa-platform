using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Incidents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed partial class IncidentProjectionConcurrencyTests
{
    [Fact]
    [Trait("Category", "Stress")]
    public async Task Ten_thousand_occurrences_across_one_hundred_instances_produce_one_canonical_incident_and_work_item()
    {
        const int instanceCount = 100;
        const int occurrencesPerInstance = 100;
        const int environmentCount = 4;
        const int revisionCount = 10;
        const int projectionConcurrency = 8;
        var databasePath = Path.Combine(Path.GetTempPath(), $"valence-control-healing-scale-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30;Pooling=True")
            .Options;
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var environments = Enumerable.Range(0, environmentCount).Select(_ => Guid.NewGuid()).ToArray();
        var revisions = Enumerable.Range(0, revisionCount).Select(_ => Guid.NewGuid()).ToArray();
        var startedAt = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var fingerprint = $"sha256:{new string('f', 64)}";

        try
        {
            AuthorityIds authority;
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                authority = await SeedAuthorityAsync(setup, workspaceId, applicationId);
            }

            var instanceStreams = Enumerable.Range(0, instanceCount)
                .Select(instanceIndex => Enumerable.Range(0, occurrencesPerInstance)
                    .Select(occurrenceIndex =>
                    {
                        var globalIndex = instanceIndex * occurrencesPerInstance + occurrenceIndex;
                        return WithAuthority(
                            Request(
                                workspaceId,
                                applicationId,
                                environments[(instanceIndex + occurrenceIndex) % environmentCount],
                                globalIndex) with
                            {
                                RevisionId = revisions[(instanceIndex * 2 + occurrenceIndex) % revisionCount],
                                OccurrenceKey = $"instance:{instanceIndex:D3}:occurrence:{occurrenceIndex:D3}",
                                OccurredAt = startedAt.AddMilliseconds(globalIndex),
                                AcceptedAt = startedAt.AddMilliseconds(globalIndex + 1),
                                TraceId = instanceIndex.ToString("x32"),
                                SpanId = occurrenceIndex.ToString("x16"),
                                Fingerprint = fingerprint,
                                EvidenceDigest = $"sha256:{globalIndex:x64}",
                                OccurrenceThreshold = 1,
                                DebounceWindow = TimeSpan.Zero
                            },
                            authority);
                    })
                    .ToArray())
                .ToArray();

            await Parallel.ForEachAsync(
                instanceStreams,
                new ParallelOptions { MaxDegreeOfParallelism = projectionConcurrency },
                async (requests, cancellationToken) =>
                {
                    await using var intakeDb = new HealingDbContext(options);
                    intakeDb.HealingSignalInboxItems.AddRange(requests.Select(Inbox));
                    await intakeDb.SaveChangesAsync(cancellationToken);
                });

            await Parallel.ForEachAsync(
                instanceStreams,
                new ParallelOptions { MaxDegreeOfParallelism = projectionConcurrency },
                async (requests, cancellationToken) =>
                {
                    await using var projectionDb = new HealingDbContext(options);
                    var store = new HealingStore(projectionDb);
                    foreach (var request in requests)
                    {
                        await store.ProjectOccurrenceAsync(request, cancellationToken);
                        projectionDb.ChangeTracker.Clear();
                    }
                });

            await using var verify = new HealingDbContext(options);
            var incident = await verify.HealingIncidents.AsNoTracking().SingleAsync();
            var episode = await verify.IncidentEpisodes.AsNoTracking().SingleAsync();
            var workItem = await verify.RepairWorkItemProjections.AsNoTracking().SingleAsync();
            var occurrences = await verify.IncidentOccurrences.AsNoTracking()
                .Select(x => new { x.IncidentId, x.EpisodeId, x.OccurrenceKey, x.TraceId })
                .ToArrayAsync();
            var impacts = await verify.EnvironmentImpacts.AsNoTracking().ToArrayAsync();
            var producingRevisions = JsonSerializer.Deserialize<Guid[]>(episode.ProducingRevisionsJson)!;

            occurrences.Should().HaveCount(instanceCount * occurrencesPerInstance);
            occurrences.Should().OnlyContain(x => x.IncidentId == incident.Id && x.EpisodeId == episode.Id);
            occurrences.Select(x => x.OccurrenceKey).Should().OnlyHaveUniqueItems();
            occurrences.GroupBy(x => x.OccurrenceKey.Split(':')[1]).Should().HaveCount(instanceCount);
            occurrences.GroupBy(x => x.OccurrenceKey.Split(':')[1]).Should().OnlyContain(x => x.Count() == occurrencesPerInstance);
            occurrences.GroupBy(x => x.TraceId).Should().HaveCount(instanceCount);
            occurrences.GroupBy(x => x.TraceId).Should().OnlyContain(x => x.Count() == occurrencesPerInstance);
            incident.Fingerprint.Should().Be(fingerprint);
            incident.RepairRepositoryKey.Should().Be("github:repository-1");
            incident.OccurrenceCount.Should().Be(instanceCount * occurrencesPerInstance);
            incident.Status.Should().Be(HealingIncidentStatus.ReadyForRepair);
            incident.ActiveEpisodeId.Should().Be(episode.Id);
            incident.WorkItemProjectionId.Should().Be(workItem.Id);
            workItem.IncidentId.Should().Be(incident.Id);
            workItem.EpisodeId.Should().Be(episode.Id);
            impacts.Should().HaveCount(environmentCount);
            impacts.Should().OnlyContain(x => x.OccurrenceCount == instanceCount * occurrencesPerInstance / environmentCount);
            impacts.Should().AllSatisfy(x =>
                JsonSerializer.Deserialize<Guid[]>(x.ProducingRevisionsJson).Should().BeEquivalentTo(revisions));
            producingRevisions.Should().BeEquivalentTo(revisions);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
