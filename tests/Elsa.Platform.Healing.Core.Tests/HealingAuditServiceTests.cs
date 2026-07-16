using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Security;
using FluentAssertions;

namespace Elsa.Platform.Healing.Core.Tests;

public sealed class HealingAuditServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task AppendRejectsDetailsWhoseKeysCouldCarryCredentials()
    {
        var service = new HealingAuditService(new InMemoryAuditStore(), TimeProvider.System);
        var write = ValidWrite() with
        {
            SafeDetails = new Dictionary<string, string?> { ["accessToken"] = "should-never-be-stored" }
        };

        var act = () => service.AppendAsync(write).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*accessToken*not permitted*");
    }

    [Fact]
    public async Task AppendPersistsOnlyCanonicalSafeStructuredDetails()
    {
        var store = new InMemoryAuditStore();
        var service = new HealingAuditService(store, new FixedTimeProvider(Now));
        var write = ValidWrite() with
        {
            SafeDetails = new Dictionary<string, string?>
            {
                ["status"] = "ready",
                ["attemptCount"] = "1"
            }
        };

        var appended = await service.AppendAsync(write);

        appended.Sequence.Should().Be(1);
        appended.OccurredAt.Should().Be(Now);
        appended.SafeDetailJson.Should().Be("{\"attemptCount\":\"1\",\"status\":\"ready\"}");
    }

    [Fact]
    public async Task AppendRejectsObviousCredentialMaterialEvenUnderAnAllowedKey()
    {
        var service = new HealingAuditService(new InMemoryAuditStore());
        var write = ValidWrite() with
        {
            SafeDetails = new Dictionary<string, string?> { ["providerOutcome"] = "Bearer credential-value" }
        };

        var act = () => service.AppendAsync(write).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*providerOutcome*credential material*");
    }

    [Theory]
    [InlineData("opaque-token-value with spaces")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.signature")]
    [InlineData("server=db;user=app;pwd=hunter2")]
    public async Task AppendRejectsUnstructuredOrSecretBearingValuesUnderAnAllowedKey(string unsafeValue)
    {
        var service = new HealingAuditService(new InMemoryAuditStore());
        var write = ValidWrite() with
        {
            SafeDetails = new Dictionary<string, string?> { ["providerOutcome"] = unsafeValue }
        };

        var act = () => service.AppendAsync(write).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*providerOutcome*");
    }

    [Fact]
    public async Task AppendRejectsUnregisteredDetailFields()
    {
        var service = new HealingAuditService(new InMemoryAuditStore());
        var write = ValidWrite() with
        {
            SafeDetails = new Dictionary<string, string?> { ["detail"] = "ready" }
        };

        var act = () => service.AppendAsync(write).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*detail*not registered*");
    }

    private static HealingAuditWrite ValidWrite() => new(
        Guid.NewGuid(),
        "incident",
        Guid.NewGuid(),
        "incident.observed",
        "eligible-failure",
        "platform",
        "healing-worker",
        Guid.NewGuid(),
        null,
        null,
        null,
        null,
        new Dictionary<string, string?>());

    private sealed class InMemoryAuditStore : IHealingAuditStore
    {
        public ValueTask<HealingAuditEvent> AppendAsync(HealingAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            auditEvent.Sequence = 1;
            return ValueTask.FromResult(auditEvent);
        }

        public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(HealingAuditQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<HealingAuditEvent>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
