using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using ValenceControl.Healing.Core.OpenTelemetry;
using Microsoft.AspNetCore.Http;

namespace ValenceControl.Healing.OpenTelemetry;

/// <summary>
/// Authenticates a Control-issued OTLP source token and establishes server-owned Healing routing claims.
/// Every rejection is deliberately indistinguishable to callers.
/// </summary>
public sealed class ControlHealingOtlpRequestAuthenticator(
    IHealingTelemetrySourceStore store,
    HealingTelemetrySourceTokenService tokens) : IOtlpRequestAuthenticator
{
    public async ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var values = httpContext.Request.Headers[HealingTelemetrySourceTokenService.HeaderName];
        if (values.Count != 1 || !tokens.TryParse(values[0], out var sourceId, out var secret))
            return OtlpRequestAuthenticationResult.Rejected;

        try
        {
            var source = await store.GetActiveTelemetrySourceForAuthenticationAsync(sourceId, cancellationToken);
            if (source is null || !tokens.Verify(secret, source.CredentialSalt, source.CredentialHash))
                return OtlpRequestAuthenticationResult.Rejected;

            var claims = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [HealingTelemetryScopeClaims.WorkspaceId] = source.WorkspaceId.ToString("D"),
                [HealingTelemetryScopeClaims.ApplicationId] = source.ApplicationId.ToString("D"),
                [HealingTelemetryScopeClaims.EnvironmentId] = source.EnvironmentId.ToString("D")
            };
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["valence.control.telemetry-source.credential-version"] = source.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            var context = OpenTelemetryIngestionContext.Authenticated(
                $"control-otel-source:{source.Id:D}", claims, metadata);
            return OtlpRequestAuthenticationResult.Accept(context);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret);
        }
    }
}
