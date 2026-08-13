using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ValenceControl.Api.Healing;

public sealed class HealingVerificationFailureDeliveryOptions
{
    public const string SectionName = "Healing:VerificationFailureDelivery";

    public bool Enabled { get; set; }
    public Uri? Endpoint { get; set; }
    public string SharedSecret { get; set; } = string.Empty;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(1);
    public int MaximumBatchSize { get; set; } = 25;

    public bool IsValid() =>
        LeaseDuration >= TimeSpan.FromSeconds(5) && LeaseDuration <= TimeSpan.FromHours(1) &&
        RetryDelay >= TimeSpan.FromSeconds(1) && RetryDelay <= TimeSpan.FromHours(1) &&
        MaximumBatchSize is >= 1 and <= 100 &&
        (!Enabled ||
         (Endpoint is not null &&
          Endpoint.IsAbsoluteUri &&
          Endpoint.Scheme == Uri.UriSchemeHttps &&
          SharedSecret is not null &&
          Encoding.UTF8.GetByteCount(SharedSecret) >= 32));
}

/// <summary>
/// Replaceable notification-only consumer for failed repair verification signals. Implementations can
/// notify a deployment system, but this contract intentionally exposes no deployment or rollback command.
/// </summary>
public interface IRepairVerificationFailureConsumer
{
    ValueTask<bool> ConsumeAsync(
        RepairVerificationFailedSignalLease delivery,
        CancellationToken cancellationToken = default);
}

public sealed class HttpRepairVerificationFailureConsumer(
    HttpClient httpClient,
    TimeProvider timeProvider,
    IOptions<HealingVerificationFailureDeliveryOptions> options) : IRepairVerificationFailureConsumer
{
    public async ValueTask<bool> ConsumeAsync(
        RepairVerificationFailedSignalLease delivery,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        if (!configured.Enabled || configured.Endpoint is null)
            return false;

        var payload = JsonSerializer.Serialize(delivery.Signal);
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signatureMaterial = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var secret = Encoding.UTF8.GetBytes(configured.SharedSecret);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(secret, signatureMaterial));

        using var request = new HttpRequestMessage(HttpMethod.Post, configured.Endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", delivery.DeliveryId.ToString("N"));
        request.Headers.Add("X-Valence-Control-Event", "repair-verification-failed");
        request.Headers.Add("X-Valence-Control-Timestamp", timestamp);
        request.Headers.Add("X-Valence-Control-Signature", $"sha256={signature}");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict;
    }
}

public sealed class HealingVerificationFailureDeliveryService(
    IRepairVerificationFailedSignalOutbox outbox,
    IEnumerable<IRepairVerificationFailureConsumer> consumers,
    TimeProvider timeProvider,
    IOptions<HealingVerificationFailureDeliveryOptions> options)
{
    public async ValueTask<int> RunBatchAsync(string consumerId, CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return 0;

        var consumer = ResolveConsumer();
        var processed = 0;
        while (processed < configured.MaximumBatchSize &&
               await RunOnceCoreAsync(consumerId, consumer, cancellationToken))
        {
            processed++;
        }
        return processed;
    }

    public async ValueTask<bool> RunOnceAsync(string consumerId, CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            return false;

        var consumer = ResolveConsumer();
        return await RunOnceCoreAsync(consumerId, consumer, cancellationToken);
    }

    private async ValueTask<bool> RunOnceCoreAsync(
        string consumerId,
        IRepairVerificationFailureConsumer consumer,
        CancellationToken cancellationToken)
    {
        var configured = options.Value;

        var now = timeProvider.GetUtcNow();
        var delivery = await outbox.TryLeaseNextAsync(consumerId, now, configured.LeaseDuration, cancellationToken);
        if (delivery is null)
            return false;

        try
        {
            if (await consumer.ConsumeAsync(delivery, cancellationToken))
            {
                await outbox.MarkDeliveredAsync(delivery.DeliveryId, delivery.LeaseToken, timeProvider.GetUtcNow(), cancellationToken);
                return true;
            }

            await ReleaseAsync(delivery, "deployment-system-rejected-signal", cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await ReleaseAsync(delivery, "deployment-system-delivery-failed", cancellationToken);
            throw;
        }
    }

    private IRepairVerificationFailureConsumer ResolveConsumer()
    {
        using var enumerator = consumers.GetEnumerator();
        if (!enumerator.MoveNext())
            throw new InvalidOperationException(
                "Enabled repair verification failure delivery requires exactly one registered consumer.");
        var consumer = enumerator.Current;
        if (enumerator.MoveNext())
            throw new InvalidOperationException("Exactly one repair verification failure consumer must be registered.");
        return consumer;
    }

    private ValueTask<bool> ReleaseAsync(
        RepairVerificationFailedSignalLease delivery,
        string outcomeCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var multiplier = Math.Min(1 << Math.Min(delivery.AttemptCount - 1, 4), 16);
        return outbox.ReleaseAsync(
            delivery.DeliveryId,
            delivery.LeaseToken,
            now,
            now.Add(TimeSpan.FromTicks(options.Value.RetryDelay.Ticks * multiplier)),
            outcomeCode,
            cancellationToken);
    }
}

public sealed class HealingVerificationFailureDeliveryHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<HealingOptions> healingOptions,
    ILogger<HealingVerificationFailureDeliveryHostedService> logger) : BackgroundService
{
    private readonly string _consumerId = $"verification-failure-delivery:{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processed = await scope.ServiceProvider
                    .GetRequiredService<HealingVerificationFailureDeliveryService>()
                    .RunBatchAsync(_consumerId, stoppingToken);
                if (processed == 0)
                    await Task.Delay(healingOptions.Value.IdleDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Repair verification failure delivery failed; the durable signal will be retried.");
                await Task.Delay(healingOptions.Value.IdleDelay, timeProvider, stoppingToken);
            }
        }
    }
}
