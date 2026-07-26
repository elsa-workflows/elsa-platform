using System.Net;
using System.Security.Cryptography;
using System.Text;
using ValenceControl.Api.Healing;
using ValenceControl.Healing.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingVerificationFailureDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-17T10:00:00Z");

    [Theory]
    [InlineData(null, 32)]
    [InlineData("/verification-failures", 32)]
    [InlineData("http://deployment.example.test/verification-failures", 32)]
    [InlineData("https://deployment.example.test/verification-failures", 31)]
    public void Enabled_delivery_requires_an_absolute_https_endpoint_and_a_32_byte_secret(
        string? endpoint,
        int secretLength)
    {
        var options = EnabledOptions();
        options.Endpoint = endpoint is null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute);
        options.SharedSecret = new string('s', secretLength);

        options.IsValid().Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dispatcher_acknowledges_only_explicitly_accepted_delivery(bool accepted)
    {
        var lease = Lease();
        var outbox = new RecordingOutbox(lease);
        var service = new HealingVerificationFailureDeliveryService(
            outbox,
            [new StubConsumer(accepted)],
            new FixedTimeProvider(),
            Options.Create(EnabledOptions()));

        (await service.RunOnceAsync("deployment-consumer")).Should().BeTrue();

        outbox.Delivered.Should().Be(accepted);
        outbox.Released.Should().Be(!accepted);
        outbox.OutcomeCode.Should().Be(accepted ? null : "deployment-system-rejected-signal");
    }

    [Fact]
    public async Task Enabled_dispatcher_rejects_composition_without_a_registered_consumer()
    {
        var outbox = new RecordingOutbox(Lease());
        var service = new HealingVerificationFailureDeliveryService(
            outbox,
            [],
            new FixedTimeProvider(),
            Options.Create(EnabledOptions()));

        var dispatch = async () => await service.RunOnceAsync("deployment-consumer");

        await dispatch.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*requires exactly one registered consumer*");

        outbox.LeaseRequests.Should().Be(0);
        outbox.Delivered.Should().BeFalse();
        outbox.Released.Should().BeFalse();
    }

    [Fact]
    public async Task Dispatcher_processes_only_the_configured_bounded_batch()
    {
        var outbox = new RecordingOutbox(Lease(), alwaysReturnLease: true);
        var options = EnabledOptions();
        options.MaximumBatchSize = 3;
        var service = new HealingVerificationFailureDeliveryService(
            outbox,
            [new StubConsumer(true)],
            new FixedTimeProvider(),
            Options.Create(options));

        (await service.RunBatchAsync("deployment-consumer")).Should().Be(3);

        outbox.LeaseRequests.Should().Be(3);
        outbox.DeliveredCount.Should().Be(3);
    }

    [Fact]
    public async Task Http_consumer_signs_signal_and_supplies_a_stable_idempotency_key()
    {
        var lease = Lease();
        var handler = new RecordingHandler();
        var options = EnabledOptions();
        var consumer = new HttpRepairVerificationFailureConsumer(
            new HttpClient(handler),
            new FixedTimeProvider(),
            Options.Create(options));

        (await consumer.ConsumeAsync(lease)).Should().BeTrue();

        handler.Headers["Idempotency-Key"].Should().ContainSingle(lease.DeliveryId.ToString("N"));
        handler.Headers["X-Valence-Control-Event"].Should().ContainSingle("repair-verification-failed");
        var timestamp = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = handler.Payload.Should().NotBeNull().And.Subject;
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.SharedSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));
        handler.Headers["X-Valence-Control-Signature"]
            .Should().ContainSingle($"sha256={expected}");
    }

    private static HealingVerificationFailureDeliveryOptions EnabledOptions() => new()
    {
        Enabled = true,
        Endpoint = new Uri("https://deployment.example.test/healing/verification-failures"),
        SharedSecret = new string('s', 32),
        LeaseDuration = TimeSpan.FromMinutes(1),
        RetryDelay = TimeSpan.FromMinutes(2)
    };

    private static RepairVerificationFailedSignalLease Lease()
    {
        var signal = new RepairVerificationFailedSignal(
            HealingContractVersions.DeploymentProtocol,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "abcdef1234567890", Guid.NewGuid(), "matching-recurrence", Now);
        return new RepairVerificationFailedSignalLease(Guid.NewGuid(), "lease-token", signal, 1, Now.AddMinutes(1));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubConsumer(bool accepted) : IRepairVerificationFailureConsumer
    {
        public ValueTask<bool> ConsumeAsync(RepairVerificationFailedSignalLease delivery, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(accepted);
    }

    private sealed class RecordingOutbox(
        RepairVerificationFailedSignalLease lease,
        bool alwaysReturnLease = false) : IRepairVerificationFailedSignalOutbox
    {
        public bool Delivered { get; private set; }
        public int DeliveredCount { get; private set; }
        public bool Released { get; private set; }
        public string? OutcomeCode { get; private set; }
        public int LeaseRequests { get; private set; }

        public ValueTask<RepairVerificationFailedSignalAppendReceipt> AppendAsync(RepairVerificationFailedSignal signal, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RepairVerificationFailedSignalLease?> TryLeaseNextAsync(string consumerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            LeaseRequests++;
            return ValueTask.FromResult<RepairVerificationFailedSignalLease?>(
                alwaysReturnLease || LeaseRequests == 1 ? lease : null);
        }

        public ValueTask<bool> MarkDeliveredAsync(Guid deliveryId, string leaseToken, DateTimeOffset deliveredAt, CancellationToken cancellationToken = default)
        {
            Delivered = true;
            DeliveredCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> ReleaseAsync(Guid deliveryId, string leaseToken, DateTimeOffset now, DateTimeOffset nextAttemptAt, string outcomeCode, CancellationToken cancellationToken = default)
        {
            Released = true;
            OutcomeCode = outcomeCode;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Dictionary<string, string[]> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
                Headers[header.Key] = header.Value.ToArray();
            Payload = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
