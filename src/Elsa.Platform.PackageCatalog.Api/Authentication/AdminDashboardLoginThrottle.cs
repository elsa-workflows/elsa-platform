using System.Collections.Concurrent;
using System.Net;

namespace Elsa.Platform.PackageCatalog.Api.Authentication;

public sealed class AdminDashboardLoginThrottle(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, LoginThrottleEntry> _entries = new(StringComparer.Ordinal);

    public LoginThrottleDecision Check(HttpContext context)
    {
        var clientKey = GetClientKey(context);
        if (clientKey is null)
            return LoginThrottleDecision.Allowed(null);

        var now = timeProvider.GetUtcNow();
        if (!_entries.TryGetValue(clientKey, out var entry))
            return LoginThrottleDecision.Allowed(clientKey);

        if (entry.RetryAfter is { } retryAfter && retryAfter > now)
            return LoginThrottleDecision.Throttled(clientKey, retryAfter);

        if (now - entry.WindowStartedAt >= AdminDashboardAuthenticationDefaults.LoginThrottleWindow)
            _entries.TryRemove(clientKey, out _);

        return LoginThrottleDecision.Allowed(clientKey);
    }

    public void RecordFailure(string? clientKey)
    {
        if (clientKey is null)
            return;

        var now = timeProvider.GetUtcNow();
        _entries.AddOrUpdate(
            clientKey,
            _ => new LoginThrottleEntry(now, 1, null),
            (_, entry) =>
            {
                var windowStartedAt = entry.WindowStartedAt;
                var failedAttemptCount = entry.FailedAttemptCount;
                if (now - windowStartedAt >= AdminDashboardAuthenticationDefaults.LoginThrottleWindow)
                {
                    windowStartedAt = now;
                    failedAttemptCount = 0;
                }

                failedAttemptCount++;
                var retryAfter = failedAttemptCount >= AdminDashboardAuthenticationDefaults.LoginThrottleFailureThreshold
                    ? now.Add(AdminDashboardAuthenticationDefaults.LoginThrottleDelay)
                    : (DateTimeOffset?)null;

                return new LoginThrottleEntry(windowStartedAt, failedAttemptCount, retryAfter);
            });
    }

    public void Clear(string? clientKey)
    {
        if (clientKey is not null)
            _entries.TryRemove(clientKey, out _);
    }

    private static string? GetClientKey(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address is null ? null : Normalize(address);
    }

    private static string Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();

    private sealed record LoginThrottleEntry(
        DateTimeOffset WindowStartedAt,
        int FailedAttemptCount,
        DateTimeOffset? RetryAfter);
}

public sealed record LoginThrottleDecision(
    string? ClientKey,
    bool IsThrottled,
    DateTimeOffset? RetryAfter)
{
    public static LoginThrottleDecision Allowed(string? clientKey) => new(clientKey, false, null);
    public static LoginThrottleDecision Throttled(string clientKey, DateTimeOffset retryAfter) => new(clientKey, true, retryAfter);
}
