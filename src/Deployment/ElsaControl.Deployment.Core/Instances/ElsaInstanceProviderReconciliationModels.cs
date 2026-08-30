using ElsaControl.Deployment.Abstractions.Instances;
using System.Security.Cryptography;
using System.Text;

namespace ElsaControl.Deployment.Core.Instances;

public enum ElsaInstanceProviderObservationKind
{
    Confirmed,
    Unknown,
    Ambiguous
}

public enum ElsaInstanceProviderHealthGate
{
    Passed,
    Failed,
    Unknown,
    NotApplicable
}

/// <summary>
/// Opaque, safe evidence that a new provider apply would not duplicate uncertain
/// work. Its presence is advisory to a later retry decision; reconciliation never
/// turns it into an automatic retry.
/// </summary>
public sealed record ElsaInstanceProviderRetryEvidence
{
    public ElsaInstanceProviderRetryEvidence(string reference, string digest)
    {
        Reference = RequireToken(reference, nameof(reference));
        Digest = RequireDigest(digest);
    }

    public string Reference { get; }

    public string Digest { get; }

    private static string RequireToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Retry evidence reference is invalid.", parameterName);
        return value.Trim();
    }

    private static string RequireDigest(string value)
    {
        if (value is null || value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            value.AsSpan(7).ContainsAnyExcept("0123456789abcdef"))
            throw new ArgumentException("Retry evidence digest is invalid.", nameof(value));
        return value;
    }
}

/// <summary>
/// Provider-neutral, value-free provider observation. Provider adapters retain raw
/// payloads and expose only a correlated lifecycle fact plus optional retry proof.
/// </summary>
public sealed record ElsaInstanceProviderObservation
{
    public ElsaInstanceProviderObservation(
        ElsaInstanceProviderObservationKind kind,
        ElsaObservedLifecycle observedLifecycle,
        ElsaInstanceProviderHealthGate healthGate,
        string correlationId,
        ElsaInstanceProviderRetryEvidence? retryEvidence = null)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(observedLifecycle) || !Enum.IsDefined(healthGate))
            throw new ArgumentOutOfRangeException(nameof(kind), "Provider observation value is invalid.");
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128 ||
            correlationId.Any(char.IsControl) || correlationId.Any(char.IsWhiteSpace))
            throw new ArgumentException("Provider observation correlation is invalid.", nameof(correlationId));
        if (kind != ElsaInstanceProviderObservationKind.Confirmed &&
            (observedLifecycle != ElsaObservedLifecycle.Unknown || healthGate != ElsaInstanceProviderHealthGate.Unknown))
            throw new ArgumentException("Uncertain provider observations must remain unknown.", nameof(observedLifecycle));

        Kind = kind;
        ObservedLifecycle = observedLifecycle;
        HealthGate = healthGate;
        CorrelationId = correlationId;
        RetryEvidence = retryEvidence;
    }

    public ElsaInstanceProviderObservationKind Kind { get; }

    public ElsaObservedLifecycle ObservedLifecycle { get; }

    public ElsaInstanceProviderHealthGate HealthGate { get; }

    public string CorrelationId { get; }

    public ElsaInstanceProviderRetryEvidence? RetryEvidence { get; }

    internal string ComputeFingerprint()
    {
        var canonical = $"{Kind}\n{ObservedLifecycle}\n{HealthGate}\n{CorrelationId}\n{RetryEvidence?.Reference}\n{RetryEvidence?.Digest}\n";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record ElsaInstanceProviderReconciliationRequest(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    int AttemptNumber,
    ElsaDesiredLifecycle DesiredLifecycle,
    ElsaResolvedPlanReference? ResolvedPlanReference,
    ElsaCurrentDeploymentReference? CurrentDeploymentReference);

public sealed record ElsaInstanceProviderReconciliationTarget(
    ElsaInstance Instance,
    ElsaInstanceOperation Operation,
    int ReconciliationVersion)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Instance);
        ArgumentNullException.ThrowIfNull(Operation);
        if (Operation.InstanceId != Instance.Id || Operation.State != ElsaInstanceOperationState.RecoveryRequired ||
            ReconciliationVersion < 0)
            throw new InvalidOperationException("Provider reconciliation target is invalid.");
    }
}

public sealed record ElsaInstanceProviderReconciliationCommit(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    int ExpectedInstanceVersion,
    int ExpectedAttemptNumber,
    int ExpectedReconciliationVersion,
    string EvidenceFingerprint,
    ElsaInstance Instance,
    ElsaInstanceOperation Operation,
    string DiagnosticCode,
    bool RetrySafe,
    DateTimeOffset ReconciledAt)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty ||
            ExpectedInstanceVersion < 1 || ExpectedAttemptNumber < 1 || ExpectedReconciliationVersion < 0 ||
            EvidenceFingerprint.Length != 64 || EvidenceFingerprint.Any(x => !char.IsAsciiHexDigit(x)) ||
            string.IsNullOrWhiteSpace(DiagnosticCode) || DiagnosticCode.Length > 128 ||
            DiagnosticCode.Any(x => !(char.IsAsciiLetterLower(x) || char.IsAsciiDigit(x) || x is '.' or '-')))
            throw new InvalidOperationException("Provider reconciliation commit envelope is invalid.");
        ArgumentNullException.ThrowIfNull(Instance);
        ArgumentNullException.ThrowIfNull(Operation);
        if (Instance.Id != InstanceId || Instance.WorkspaceId != WorkspaceId ||
            Operation.Id != OperationId || Operation.InstanceId != InstanceId ||
            Operation.AttemptNumber != ExpectedAttemptNumber ||
            Operation.State is not (ElsaInstanceOperationState.RecoveryRequired or ElsaInstanceOperationState.Succeeded or ElsaInstanceOperationState.Failed))
            throw new InvalidOperationException("Provider reconciliation commit state is invalid.");
    }
}

public enum ElsaInstanceProviderReconciliationOutcome
{
    Converged,
    RecoveryRequired,
    HealthGateFailed,
    Failed
}

public sealed record ElsaInstanceProviderReconciliationResult(
    ElsaInstanceProviderReconciliationOutcome Outcome,
    ElsaInstance Instance,
    ElsaInstanceOperation Operation,
    string DiagnosticCode,
    bool RetrySafe,
    bool Replayed,
    DateTimeOffset ReconciledAt);
