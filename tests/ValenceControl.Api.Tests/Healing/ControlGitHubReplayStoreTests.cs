using ValenceControl.Api.Healing;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Tests.Healing;

public sealed class ControlGitHubReplayStoreTests
{
    [Fact]
    public async Task Durable_delivery_identity_distinguishes_exact_redelivery_from_digest_conflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HealingDbContext>().UseSqlite(connection).Options;
        await using var dbContext = new HealingDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var workspaceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        dbContext.ProviderConnections.Add(new ProviderConnection
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Provider = "GitHub",
            InstallationId = "42",
            RepositoryProviderId = "987",
            RepositoryOwner = "acme",
            RepositoryName = "app",
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync();
        var store = new ControlGitHubReplayStore(dbContext);
        var delivery = Delivery(workspaceId, new string('a', 64), now);

        var accepted = await store.TryAcceptAsync(delivery);
        var exactReplay = await store.TryAcceptAsync(delivery with { ReceivedAt = now.AddSeconds(1) });
        var conflict = await store.TryAcceptAsync(delivery with
        {
            BodyDigest = new string('b', 64),
            ReceivedAt = now.AddSeconds(2)
        });

        Assert.Equal(GitHubWebhookReplayDisposition.Accepted, accepted.Disposition);
        Assert.Equal(GitHubWebhookReplayResult.ExactReplay(delivery.BodyDigest), exactReplay);
        Assert.Equal(GitHubWebhookReplayResult.Conflict(delivery.BodyDigest), conflict);
        Assert.Equal(1, (await dbContext.ProviderWebhookDeliveries.CountAsync()));
    }

    private static GitHubWebhookReplayRecord Delivery(
        Guid workspaceId,
        string bodyDigest,
        DateTimeOffset receivedAt) =>
        new(
            workspaceId,
            "delivery-1",
            "42",
            "987",
            "acme/app",
            "workflow_run",
            "completed",
            bodyDigest,
            receivedAt);
}
