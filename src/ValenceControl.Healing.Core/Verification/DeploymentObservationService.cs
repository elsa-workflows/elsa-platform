using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core.Security;

namespace ValenceControl.Healing.Core.Verification;

public sealed class DeploymentObservationService(
    IHealingVerificationStore store,
    HealingVerificationService verificationService,
    HealingAuditService auditService,
    TimeProvider timeProvider) : IDeploymentObservationSink
{
    public static readonly TimeSpan MaximumObservationAge = TimeSpan.FromDays(365);
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.Zero;
    public const int MaximumIdentityLength = 2_048;
    public const int MaximumKeyLength = 256;

    public async ValueTask<DeploymentObservationReceipt> AppendAsync(
        DeploymentObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, timeProvider.GetUtcNow());
        var source = request.Source switch
        {
            DeploymentObservationSources.ControlDeployment => DeploymentObservationSource.ControlDeployment,
            DeploymentObservationSources.ExternalDelivery => DeploymentObservationSource.ExternalDelivery,
            _ => throw new ArgumentException("The deployment observation source is unsupported.", nameof(request))
        };
        var observation = new DeploymentObservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            ApplicationId = request.ApplicationId,
            EnvironmentId = request.EnvironmentId,
            Revision = request.Revision.Trim().ToLowerInvariant(),
            DeployedAt = request.DeployedAt,
            Source = source,
            SourceObservationId = request.SourceObservationId.Trim(),
            SourceIdempotencyKey = request.IdempotencyKey.Trim(),
            TrustIdentity = request.TrustIdentity.Trim(),
            EvidenceDigest = request.EvidenceDigest.Trim(),
            AcceptedAt = timeProvider.GetUtcNow()
        };

        var append = await store.AppendDeploymentObservationAsync(observation, cancellationToken);
        // Re-drive both idempotent projections on replay so a crash after the durable append cannot strand
        // the observation before verification or audit is updated.
        await verificationService.ObserveDeploymentAsync(append.Value, cancellationToken);
        await auditService.AppendAsync(new HealingAuditWrite(
            request.WorkspaceId,
            "deployment-observation",
            append.Value.Id,
            "deployment-observed",
            source == DeploymentObservationSource.ControlDeployment ? "control-deployment" : "external-delivery",
            HealingActorTypes.DeploymentSystem,
            request.TrustIdentity,
            append.Value.Id,
            null,
            null,
            null,
            request.EvidenceDigest,
            new Dictionary<string, string?>
            {
                ["environment"] = request.EnvironmentId.ToString("D"),
                ["revision"] = SafeAuditRevision(request.Revision)
            }), cancellationToken);

        return new DeploymentObservationReceipt(
            HealingContractVersions.DeploymentProtocol,
            append.Value.Id,
            append.IsReplay,
            append.Value.AcceptedAt);
    }

    private static void Validate(DeploymentObservationRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.ProtocolVersion, HealingContractVersions.DeploymentProtocol, StringComparison.Ordinal))
            throw new ArgumentException("The deployment observation protocol version is unsupported.", nameof(request));
        if (request.WorkspaceId == Guid.Empty || request.ApplicationId == Guid.Empty || request.EnvironmentId == Guid.Empty)
            throw new ArgumentException("Deployment observation scope is required.", nameof(request));
        Require(request.Revision, nameof(request.Revision), MaximumKeyLength);
        Require(request.Source, nameof(request.Source), MaximumKeyLength);
        Require(request.SourceObservationId, nameof(request.SourceObservationId), MaximumKeyLength);
        Require(request.IdempotencyKey, nameof(request.IdempotencyKey), MaximumKeyLength);
        Require(request.TrustIdentity, nameof(request.TrustIdentity), MaximumIdentityLength);
        Require(request.EvidenceDigest, nameof(request.EvidenceDigest), MaximumKeyLength);
        if (!request.EvidenceDigest.StartsWith("sha256:", StringComparison.Ordinal) || request.EvidenceDigest.Length != 71 ||
            !request.EvidenceDigest[7..].All(char.IsAsciiHexDigit))
            throw new ArgumentException("EvidenceDigest must be a SHA-256 digest.", nameof(request));
        if (request.DeployedAt > now.Add(MaximumFutureSkew) || request.DeployedAt < now.Subtract(MaximumObservationAge))
            throw new ArgumentOutOfRangeException(nameof(request), "DeployedAt is outside the accepted trusted observation window.");
    }

    private static void Require(string value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"{name} is required and must not exceed {maximumLength} characters.", name);
    }

    private static string? SafeAuditRevision(string value)
    {
        var candidate = value.Trim();
        return candidate.Length is >= 7 and <= 64 && candidate.All(char.IsAsciiHexDigit) ? candidate : null;
    }
}
