using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed class EngineHealthService(
    IWorkspaceDeploymentStore store,
    IEngineHealthProbe probe,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<EngineHealthResult> VerifyEngineAsync(
        Guid workspaceId,
        EngineHealthVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var engine = await store.GetEngineAsync(workspaceId, request.EngineId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow engine does not exist in the workspace.");

        var now = _timeProvider.GetUtcNow();
        var result = await probe.ProbeAsync(engine, cancellationToken);
        var health = Classify(result.Reachable, result.CertificateStatus, result.CredentialVerificationStatus);
        var credentialLastVerifiedAt = result.CredentialVerificationStatus == CredentialVerificationStatus.Verified
            ? now
            : null;

        return await store.UpdateEngineHealthAsync(
            workspaceId,
            new EngineHealthUpdate(
                engine.Id,
                engine.EnvironmentId,
                health,
                string.IsNullOrWhiteSpace(result.Version) ? engine.Version : result.Version,
                result.CertificateStatus,
                result.CredentialVerificationStatus,
                credentialLastVerifiedAt,
                result.Reachable ? now : engine.LastHeartbeatAt,
                now,
                SafeMessage(result.Message)),
            cancellationToken);
    }

    public async Task<EngineHealthResult> ApplyHeartbeatAsync(
        Guid workspaceId,
        EngineHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var engine = await store.GetEngineAsync(workspaceId, request.EngineId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow engine does not exist in the workspace.");
        if (engine.EnvironmentId != request.EnvironmentId)
            throw new InvalidOperationException("Heartbeat environment does not match the registered engine.");

        var health = Classify(true, request.CertificateStatus, request.CredentialVerificationStatus);
        var credentialLastVerifiedAt = request.CredentialVerificationStatus == CredentialVerificationStatus.Verified
            ? request.HeartbeatAt
            : null;

        return await store.ApplyEngineHeartbeatAsync(
            workspaceId,
            new EngineHealthUpdate(
                engine.Id,
                engine.EnvironmentId,
                health,
                string.IsNullOrWhiteSpace(request.Version) ? engine.Version : request.Version,
                request.CertificateStatus,
                request.CredentialVerificationStatus,
                credentialLastVerifiedAt,
                request.HeartbeatAt,
                engine.LastVerificationAt,
                SafeMessage(request.Message ?? "Heartbeat accepted."),
                request.Capabilities),
            cancellationToken);
    }

    private static DeploymentHealth Classify(
        bool reachable,
        CertificateStatus certificateStatus,
        CredentialVerificationStatus credentialStatus)
    {
        if (!reachable)
            return DeploymentHealth.Unreachable;

        return certificateStatus == CertificateStatus.Trusted && credentialStatus == CredentialVerificationStatus.Verified
            ? DeploymentHealth.Healthy
            : DeploymentHealth.Degraded;
    }

    private static string SafeMessage(string message)
    {
        var safe = message.Trim();
        if (safe.Length == 0)
            return "No diagnostic message was provided.";
        return safe.Length <= 512 ? safe : safe[..512];
    }
}

public sealed class HttpEngineHealthProbe(HttpClient httpClient) : IEngineHealthProbe
{
    public async Task<EngineHealthProbeResult> ProbeAsync(
        WorkspaceWorkflowEngine engine,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(engine.BaseUrl, UriKind.Absolute, out var endpoint))
            return new EngineHealthProbeResult(false, engine.Version, engine.CertificateStatus, CredentialVerificationStatus.Unverified, "Endpoint URL is not valid.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var version = response.Headers.TryGetValues("X-Elsa-Version", out var versions)
                ? versions.FirstOrDefault()
                : engine.Version;
            var credentialStatus = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? CredentialVerificationStatus.Unverified
                : CredentialVerificationStatus.Verified;
            var certificateStatus = endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? CertificateStatus.Trusted
                : engine.CertificateStatus;
            var message = response.IsSuccessStatusCode
                ? "Endpoint responded successfully."
                : $"Endpoint responded with HTTP {(int)response.StatusCode}.";

            return new EngineHealthProbeResult(true, version, certificateStatus, credentialStatus, message);
        }
        catch (HttpRequestException ex) when (IsCertificateFailure(ex))
        {
            return new EngineHealthProbeResult(true, engine.Version, CertificateStatus.Untrusted, CredentialVerificationStatus.Unverified, "TLS certificate validation failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new EngineHealthProbeResult(false, engine.Version, engine.CertificateStatus, CredentialVerificationStatus.Unverified, "Endpoint did not respond before verification timed out.");
        }
    }

    private static bool IsCertificateFailure(HttpRequestException exception) =>
        exception.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
        || exception.InnerException?.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) == true;
}
