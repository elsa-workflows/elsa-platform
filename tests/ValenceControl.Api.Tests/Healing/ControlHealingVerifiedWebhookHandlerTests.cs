using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Api.Healing;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Tests.Healing;

public sealed class ControlHealingVerifiedWebhookHandlerTests
{
    private const string SharedSecret = "a-shared-webhook-secret-value";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task Matching_repository_authority_fans_out_durable_processing_to_every_workspace()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(firstWorkspace, "secret://first");
        database.AddConnection(secondWorkspace, "secret://second");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(firstWorkspace, "secret://first", SharedSecret)
            .Add(secondWorkspace, "secret://second", SharedSecret);
        var handler = database.CreateHandler(resolver);
        var request = Request("delivery-1", SharedSecret);

        var accepted = await handler.ProcessAsync(request);
        var replay = await handler.ProcessAsync(request);

        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-1", false, "check-unbound"), accepted);
        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-1", true, "check-unbound"), replay);
        var deliveries = await database.Context.ProviderWebhookDeliveries.AsNoTracking()
            .OrderBy(x => x.WorkspaceId)
            .ToArrayAsync();
        Assert.Equal(2, deliveries.Count());
        Assert.Equivalent(new[] { firstWorkspace, secondWorkspace }, deliveries.Select(x => x.WorkspaceId));
        Assert.All(deliveries, x => Assert.True(
            x.ProviderDeliveryId == "delivery-1" &&
            x.RepositoryProviderId == "987" &&
            x.Status == ProviderWebhookDeliveryStatus.Completed &&
            x.OutcomeCode == "check-unbound"));
    }

    [Fact]
    public async Task Partial_workspace_replay_is_reported_as_newly_accepted_for_safe_retry_processing()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(firstWorkspace, "secret://first");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(firstWorkspace, "secret://first", SharedSecret)
            .Add(secondWorkspace, "secret://second", SharedSecret);
        var handler = database.CreateHandler(resolver);
        var request = Request("delivery-partial", SharedSecret);
        Assert.False((await handler.ProcessAsync(request)).IsReplay);
        database.AddConnection(secondWorkspace, "secret://second");
        await database.Context.SaveChangesAsync();

        var receipt = await handler.ProcessAsync(request);

        Assert.False(receipt.IsReplay);
        Assert.Equal(2, (await database.Context.ProviderWebhookDeliveries.CountAsync()));
    }

    [Fact]
    public async Task Workspace_with_wrong_webhook_secret_does_not_block_independently_verified_workspace()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(firstWorkspace, "secret://first");
        database.AddConnection(secondWorkspace, "secret://second");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(firstWorkspace, "secret://first", SharedSecret)
            .Add(secondWorkspace, "secret://second", "a-different-webhook-secret-value");
        var handler = database.CreateHandler(resolver);

        var receipt = await handler.ProcessAsync(Request("delivery-2", SharedSecret));

        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-2", false, "check-unbound"), receipt);
        var delivery = await database.Context.ProviderWebhookDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(firstWorkspace, delivery.WorkspaceId);
    }

    [Fact]
    public async Task Workspace_with_wrong_repository_identity_does_not_block_independently_verified_workspace()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(firstWorkspace, "secret://first");
        database.AddConnection(secondWorkspace, "secret://second", repositoryName: "other-app");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(firstWorkspace, "secret://first", SharedSecret)
            .Add(secondWorkspace, "secret://second", SharedSecret);
        var handler = database.CreateHandler(resolver);

        var receipt = await handler.ProcessAsync(Request("delivery-3", SharedSecret));

        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-3", false, "check-unbound"), receipt);
        var delivery = await database.Context.ProviderWebhookDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(firstWorkspace, delivery.WorkspaceId);
    }

    [Fact]
    public async Task Workspace_with_missing_webhook_secret_does_not_block_independently_verified_workspace()
    {
        await using var database = await TestDatabase.CreateAsync();
        var missingSecretWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var verifiedWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(missingSecretWorkspace, "secret://missing");
        database.AddConnection(verifiedWorkspace, "secret://verified");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(verifiedWorkspace, "secret://verified", SharedSecret);
        var handler = database.CreateHandler(resolver);

        var receipt = await handler.ProcessAsync(Request("delivery-missing", SharedSecret));

        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-missing", false, "check-unbound"), receipt);
        var delivery = await database.Context.ProviderWebhookDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(verifiedWorkspace, delivery.WorkspaceId);
    }

    [Fact]
    public async Task Workspace_with_failing_secret_resolution_does_not_block_independently_verified_workspace()
    {
        await using var database = await TestDatabase.CreateAsync();
        var failingWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var verifiedWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(failingWorkspace, "secret://failing");
        database.AddConnection(verifiedWorkspace, "secret://verified");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .ThrowFor(failingWorkspace, "secret://failing")
            .Add(verifiedWorkspace, "secret://verified", SharedSecret);
        var handler = database.CreateHandler(resolver);

        var receipt = await handler.ProcessAsync(Request("delivery-resolution-failure", SharedSecret));

        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-resolution-failure", false, "check-unbound"), receipt);
        var delivery = await database.Context.ProviderWebhookDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(verifiedWorkspace, delivery.WorkspaceId);
    }

    [Fact]
    public async Task Duplicate_repository_authority_within_one_workspace_fails_closed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var workspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        database.AddConnection(workspaceId, "secret://first");
        database.AddConnection(workspaceId, "secret://second");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(workspaceId, "secret://first", SharedSecret)
            .Add(workspaceId, "secret://second", SharedSecret);
        var handler = database.CreateHandler(resolver);

        Func<Task> act = async () => await handler.ProcessAsync(Request("delivery-4", SharedSecret));

        await AssertRejectedAsync(act);
        Assert.Equal(0, (await database.Context.ProviderWebhookDeliveries.CountAsync()));
    }

    [Fact]
    public async Task Ambiguous_workspace_authority_does_not_block_independently_verified_workspace()
    {
        await using var database = await TestDatabase.CreateAsync();
        var ambiguousWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var verifiedWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(ambiguousWorkspace, "secret://first");
        database.AddConnection(ambiguousWorkspace, "secret://duplicate");
        database.AddConnection(verifiedWorkspace, "secret://verified");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(ambiguousWorkspace, "secret://first", SharedSecret)
            .Add(ambiguousWorkspace, "secret://duplicate", SharedSecret)
            .Add(verifiedWorkspace, "secret://verified", SharedSecret);
        var handler = database.CreateHandler(resolver);

        var receipt = await handler.ProcessAsync(Request("delivery-ambiguous", SharedSecret));

        Assert.Equal(new HealingVerifiedWebhookReceipt("delivery-ambiguous", false, "check-unbound"), receipt);
        var delivery = await database.Context.ProviderWebhookDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(verifiedWorkspace, delivery.WorkspaceId);
    }

    [Fact]
    public async Task Processing_failure_is_isolated_persisted_and_retried_without_reprocessing_completed_workspaces()
    {
        await using var database = await TestDatabase.CreateAsync();
        var failingWorkspace = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var succeedingWorkspace = Guid.Parse("22222222-2222-2222-2222-222222222222");
        database.AddConnection(failingWorkspace, "secret://first");
        database.AddConnection(succeedingWorkspace, "secret://second");
        await database.Context.SaveChangesAsync();
        var resolver = new TestCredentialResolver()
            .Add(failingWorkspace, "secret://first", SharedSecret)
            .Add(succeedingWorkspace, "secret://second", SharedSecret);
        var processorRunner = database.CreateRunner(failingWorkspace);
        var handler = database.CreateHandler(resolver, processorRunner);
        var request = Request("delivery-processing-failure", SharedSecret);

        var firstAttempt = () => handler.ProcessAsync(request).AsTask();
        await Assert.ThrowsAsync<InvalidOperationException>(firstAttempt);

        var afterFailure = await database.Context.ProviderWebhookDeliveries.AsNoTracking()
            .OrderBy(x => x.WorkspaceId)
            .ToArrayAsync();
        Assert.Equal(2, afterFailure.Count());
        Assert.Equal(failingWorkspace, afterFailure[0].WorkspaceId);
        Assert.Equal(ProviderWebhookDeliveryStatus.Failed, afterFailure[0].Status);
        Assert.Equal("processing-failed", afterFailure[0].OutcomeCode);
        Assert.Equal(succeedingWorkspace, afterFailure[1].WorkspaceId);
        Assert.Equal(ProviderWebhookDeliveryStatus.Completed, afterFailure[1].Status);

        var retried = await handler.ProcessAsync(request);

        Assert.Equal(
            new HealingVerifiedWebhookReceipt("delivery-processing-failure", true, "check-unbound"),
            retried);
        var completed = await database.Context.ProviderWebhookDeliveries.AsNoTracking().ToArrayAsync();
        Assert.All(completed, x => Assert.True(
            x.Status == ProviderWebhookDeliveryStatus.Completed && x.OutcomeCode == "check-unbound"));
        Assert.Equal(2, processorRunner.CallsByWorkspace[failingWorkspace]);
        Assert.Equal(2, processorRunner.CallsByWorkspace[succeedingWorkspace]);
    }

    private static async Task AssertRejectedAsync(Func<Task> act)
    {
        var exception = await Assert.ThrowsAsync<HealingWorkflowRequestException>(act);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("healing.webhook.verification-failed", exception.ReasonCode);
    }

    private static HealingVerifiedWebhookRequest Request(string deliveryId, string secret)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            action = "completed",
            installation = new { id = 42 },
            repository = new { id = 987, full_name = "acme/app" },
            check_run = new
            {
                name = "ci",
                head_sha = new string('a', 40),
                status = "completed",
                conclusion = "success",
                completed_at = Now
            }
        });
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
        return new HealingVerifiedWebhookRequest(deliveryId, "check_run", $"sha256={signature}", body);
    }

    private sealed class TestDatabase(
        SqliteConnection connection,
        DbContextOptions<HealingDbContext> options,
        HealingDbContext context) : IAsyncDisposable
    {
        public HealingDbContext Context { get; } = context;

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<HealingDbContext>().UseSqlite(connection).Options;
            var context = new HealingDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, options, context);
        }

        public void AddConnection(
            Guid workspaceId,
            string webhookSecretReference,
            string repositoryName = "app") =>
            Context.ProviderConnections.Add(new ProviderConnection
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Provider = "GitHub",
                InstallationId = "42",
                RepositoryProviderId = "987",
                RepositoryOwner = "acme",
                RepositoryName = repositoryName,
                CredentialReference = "secret://github-app",
                WebhookSecretReference = webhookSecretReference,
                Status = ProviderConnectionStatus.Active,
                CreatedAt = Now,
                UpdatedAt = Now,
                Version = Guid.NewGuid().ToByteArray()
            });

        public TestScopedWebhookProcessorRunner CreateRunner(Guid? failingWorkspaceId = null) =>
            new(options, new FixedTimeProvider(Now), failingWorkspaceId);

        public ControlHealingVerifiedWebhookHandler CreateHandler(
            IHealingProviderCredentialResolver resolver,
            IControlHealingGitHubWebhookProcessorRunner? processorRunner = null)
        {
            var timeProvider = new FixedTimeProvider(Now);
            var replayStore = new ControlGitHubReplayStore(Context, timeProvider);
            return new ControlHealingVerifiedWebhookHandler(
                Context,
                resolver,
                new GitHubWebhookVerifier(replayStore, timeProvider),
                processorRunner ?? CreateRunner());
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestCredentialResolver : IHealingProviderCredentialResolver
    {
        private readonly Dictionary<(Guid WorkspaceId, string Reference), string> _secrets = [];
        private readonly HashSet<(Guid WorkspaceId, string Reference)> _failures = [];

        public TestCredentialResolver Add(Guid workspaceId, string reference, string secret)
        {
            _secrets[(workspaceId, reference)] = JsonSerializer.Serialize(new { webhookSecret = secret });
            return this;
        }

        public TestCredentialResolver ThrowFor(Guid workspaceId, string reference)
        {
            _failures.Add((workspaceId, reference));
            return this;
        }

        public ValueTask<string?> ResolveAsync(
            Guid workspaceId,
            string credentialReference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_failures.Contains((workspaceId, credentialReference)))
                throw new InvalidOperationException("Tenant-scoped credential resolution failed.");
            return ValueTask.FromResult(_secrets.GetValueOrDefault((workspaceId, credentialReference)));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestScopedWebhookProcessorRunner(
        DbContextOptions<HealingDbContext> options,
        TimeProvider timeProvider,
        Guid? failingWorkspaceId) : IControlHealingGitHubWebhookProcessorRunner
    {
        private bool _failed;
        public Dictionary<Guid, int> CallsByWorkspace { get; } = [];

        public async ValueTask<string> ProcessAsync(
            ProviderConnection connection,
            string deliveryId,
            string eventName,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
        {
            CallsByWorkspace[connection.WorkspaceId] = CallsByWorkspace.GetValueOrDefault(connection.WorkspaceId) + 1;
            if (connection.WorkspaceId == failingWorkspaceId && !_failed)
            {
                await using var failingContext = new HealingDbContext(options);
                var dirtyDelivery = await failingContext.ProviderWebhookDeliveries.SingleAsync(
                    x => x.WorkspaceId == connection.WorkspaceId && x.ProviderDeliveryId == deliveryId,
                    cancellationToken);
                dirtyDelivery.OutcomeCode = "unsaved-dirty-state-must-not-leak";
                _failed = true;
                throw new InvalidOperationException("Synthetic tenant-scoped processor failure.");
            }
            await using var context = new HealingDbContext(options);
            return await new ControlHealingGitHubWebhookProcessor(
                    context,
                    new GitHubWebhookProcessor(),
                    timeProvider)
                .ProcessAsync(connection, deliveryId, eventName, body, cancellationToken);
        }

        public async ValueTask RecordFailureAsync(
            Guid workspaceId,
            string deliveryId,
            CancellationToken cancellationToken = default)
        {
            await using var context = new HealingDbContext(options);
            await context.ProviderWebhookDeliveries
                .Where(x => x.WorkspaceId == workspaceId &&
                            x.ProviderDeliveryId == deliveryId &&
                            x.Status != ProviderWebhookDeliveryStatus.Completed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ProviderWebhookDeliveryStatus.Failed)
                    .SetProperty(x => x.OutcomeCode, "processing-failed")
                    .SetProperty(x => x.ProcessedAt, timeProvider.GetUtcNow())
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
        }
    }
}
