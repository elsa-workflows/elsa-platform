using Azure;
using Azure.Core;
using Azure.Core.Pipeline;

namespace ElsaControl.Api.Catalog;

// Azure.Core 1.60.0's managed-identity policy semantics are intentionally retained except
// the optional capability probe. Keep these defaults aligned when upgrading Azure.Core.
// https://github.com/Azure/azure-sdk-for-net/tree/Azure.Core_1.60.0/sdk/core/Azure.Core/src/Identity/Policies
internal class ManagedIdentityProbeRetryPolicy(DelayStrategy? delayStrategy = null) : RetryPolicy(maxRetries: 5, delayStrategy: delayStrategy ?? new ManagedIdentityRetryDelay())
{
    private static readonly ResponseClassifier ManagedIdentityClassifier = new IdentityResponseClassifier();

    protected override bool ShouldRetry(HttpMessage message, Exception? exception)
    {
        message.ResponseClassifier = ManagedIdentityClassifier;
        return !IsAvailabilityProbe(message) && !IsUnsupportedCapabilityProbe(message, exception) &&
            base.ShouldRetry(message, exception);
    }

    protected override ValueTask<bool> ShouldRetryAsync(HttpMessage message, Exception? exception)
    {
        message.ResponseClassifier = ManagedIdentityClassifier;
        return IsAvailabilityProbe(message) || IsUnsupportedCapabilityProbe(message, exception)
            ? ValueTask.FromResult(false)
            : base.ShouldRetryAsync(message, exception);
    }

    // Preserve the SDK's existing availability-probe exemption.
    private static bool IsAvailabilityProbe(HttpMessage message) =>
        message.Request.Uri.Host == "169.254.169.254" &&
        message.Request.Uri.Path == "/metadata/identity/oauth2/token" &&
        !message.Request.Headers.Contains("Metadata");

    private static bool IsUnsupportedCapabilityProbe(HttpMessage message, Exception? exception)
    {
        if (exception is not null || !message.HasResponse || message.Response.Status != 404 ||
            message.Request.Method != RequestMethod.Get || message.Request.Headers.Contains("Metadata"))
            return false;

        var uri = message.Request.Uri.ToUri();
        return uri.Scheme == "http" && uri.Host == "169.254.169.254" && uri.Port == 80 &&
            uri.UserInfo.Length == 0 && uri.AbsolutePath == "/metadata/identity/getplatformmetadata" &&
            IsCapabilityQuery(uri.Query) && uri.Fragment.Length == 0;
    }

    // MSAL appends client_id for our supported user-assigned identity mode; system-assigned
    // probes carry only the version. Unknown versions, selectors and extra parameters retry normally.
    private static bool IsCapabilityQuery(string query)
    {
        const string version = "?cred-api-version=2.0";
        const string clientPrefix = version + "&client_id=";
        return query == version ||
            (query.StartsWith(clientPrefix, StringComparison.Ordinal) &&
             Guid.TryParseExact(query.AsSpan(clientPrefix.Length), "D", out var clientId) && clientId != Guid.Empty);
    }

    private sealed class IdentityResponseClassifier : ResponseClassifier
    {
        public override bool IsRetriableResponse(HttpMessage message) => message.Response.Status switch
        {
            404 or 410 => true,
            502 => false,
            _ => base.IsRetriableResponse(message)
        };
    }
}

internal sealed class ManagedIdentityRetryDelay() : DelayStrategy()
{
    protected override TimeSpan GetNextDelayCore(Response? response, int retryNumber) =>
        TimeSpan.FromSeconds(Math.Pow(2, retryNumber - 1) * (response?.Status == 410 ? 3 : 0.8));
}
