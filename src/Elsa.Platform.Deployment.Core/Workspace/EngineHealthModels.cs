using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Workspace;

public sealed record EngineHealthVerificationRequest(
    Guid EngineId,
    Guid ActorAccountId);

public sealed record EngineHeartbeatRequest(
    Guid EngineId,
    Guid EnvironmentId,
    string? Version,
    CertificateStatus CertificateStatus,
    CredentialVerificationStatus CredentialVerificationStatus,
    DateTimeOffset HeartbeatAt,
    IReadOnlyList<EngineCapability>? Capabilities,
    string? Message);

public sealed record EngineHealthProbeResult(
    bool Reachable,
    string? Version,
    CertificateStatus CertificateStatus,
    CredentialVerificationStatus CredentialVerificationStatus,
    string Message);

public sealed record EngineHealthUpdate(
    Guid EngineId,
    Guid EnvironmentId,
    DeploymentHealth Health,
    string? Version,
    CertificateStatus CertificateStatus,
    CredentialVerificationStatus CredentialVerificationStatus,
    DateTimeOffset? CredentialLastVerifiedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastVerificationAt,
    string VerificationMessage,
    IReadOnlyList<EngineCapability>? Capabilities = null);

public sealed record EngineHealthResult(
    Guid EngineId,
    Guid EnvironmentId,
    DeploymentHealth Health,
    string? Version,
    CertificateStatus CertificateStatus,
    CredentialVerificationStatus CredentialVerificationStatus,
    DateTimeOffset? CredentialLastVerifiedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastVerificationAt,
    string Message);

public interface IEngineHealthProbe
{
    Task<EngineHealthProbeResult> ProbeAsync(WorkspaceWorkflowEngine engine, CancellationToken cancellationToken = default);
}
