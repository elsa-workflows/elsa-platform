using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Elsa.Platform.Healing.Abstractions;

/// <summary>
/// Published protocol versions shared by Healing producers, adapters, and consumers.
/// </summary>
public static class HealingContractVersions
{
    public const string SignalProfile = "1.0";
    public const string ComponentManifest = "1.0";
    public const string ProviderProtocol = "1.0";
    public const string AgentProtocol = "1.0";
    public const string WorkloadProtocol = "1.0";
    public const string PolicyProtocol = "1.0";
    public const string DeploymentProtocol = "1.0";
    public const string AuditProtocol = "1.0";

    public static IReadOnlyDictionary<string, string> All { get; } = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["signal-profile"] = SignalProfile,
            ["component-manifest"] = ComponentManifest,
            ["provider-protocol"] = ProviderProtocol,
            ["agent-protocol"] = AgentProtocol,
            ["workload-protocol"] = WorkloadProtocol,
            ["policy-protocol"] = PolicyProtocol,
            ["deployment-protocol"] = DeploymentProtocol,
            ["audit-protocol"] = AuditProtocol
        });
}

/// <summary>
/// Parses and compares the major/minor versions used by Healing wire contracts.
/// </summary>
public static class HealingContractVersion
{
    public static bool IsCompatible(string supportedVersion, string candidateVersion)
    {
        if (!TryParse(supportedVersion, out var supportedMajor, out _) ||
            !TryParse(candidateVersion, out var candidateMajor, out _))
        {
            return false;
        }

        return supportedMajor == candidateMajor;
    }

    public static bool TryParse(string? value, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrEmpty(value))
            return false;

        var separator = value.IndexOf('.');
        if (separator <= 0 || separator != value.LastIndexOf('.') || separator == value.Length - 1)
            return false;

        return int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
               major >= 1 &&
               int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out minor);
    }
}

/// <summary>
/// Stable OpenTelemetry attribute names defined by the Healing Signal Profile.
/// </summary>
public static class HealingSignalAttributes
{
    public const string ProfileVersion = "elsa.healing.profile.version";
    public const string ApplicationId = "elsa.healing.application.id";
    public const string EnvironmentId = "elsa.healing.environment.id";
    public const string RevisionId = "elsa.healing.revision.id";
    public const string SourceRevision = "elsa.healing.source.revision";
    public const string ComponentManifestDigest = "elsa.healing.component_manifest.digest";
    public const string OccurrenceId = "elsa.healing.occurrence.id";
    public const string OperationName = "elsa.healing.operation.name";
    public const string FailureClass = "elsa.healing.failure.class";
    public const string RetryState = "elsa.healing.retry.state";
    public const string Explicit = "elsa.healing.explicit";
    public const string ComponentKey = "elsa.healing.component.key";
    public const string WorkflowDefinitionId = "elsa.healing.workflow.definition.id";
    public const string WorkflowActivityType = "elsa.healing.workflow.activity.type";
}

public static class HealingFailureClasses
{
    public const string UnhandledRequest = "unhandled_request";
    public const string FatalStartup = "fatal_startup";
    public const string FatalBackground = "fatal_background";
    public const string UnexpectedWorkflow = "unexpected_workflow";
    public const string UnexpectedActivity = "unexpected_activity";
    public const string TransientExhausted = "transient_exhausted";
    public const string ExplicitIncident = "explicit_incident";
    public const string Validation = "validation";
    public const string Authorization = "authorization";
    public const string Cancellation = "cancellation";
    public const string Handled = "handled";
    public const string TransientRetrying = "transient_retrying";
    public const string Unknown = "unknown";
}

public static class HealingRetryStates
{
    public const string None = "none";
    public const string Retrying = "retrying";
    public const string Exhausted = "exhausted";
}

/// <summary>
/// A normalized post-redaction Healing signal. Monitored applications cannot put repository routing or mutation
/// authority in this contract; Platform-owned configuration resolves those decisions later.
/// </summary>
public sealed record HealingSignal(
    string ProfileVersion,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid? RevisionId,
    DateTimeOffset OccurredAt,
    string OperationName,
    string FailureClass,
    string RetryState,
    HealingExceptionEvidence Exception,
    HealingEvidenceMetadata Evidence,
    string? OccurrenceId = null,
    string? SourceRevision = null,
    string? ComponentManifestDigest = null,
    bool IsExplicit = false,
    string? ComponentKey = null,
    string? WorkflowDefinitionId = null,
    string? WorkflowActivityType = null,
    HealingTraceContext? Trace = null);

public sealed record HealingExceptionEvidence(
    string Type,
    string? Message,
    string? StackTrace,
    IReadOnlyList<HealingExceptionFrame> Frames);

public sealed record HealingExceptionFrame(
    string? AssemblyName,
    string? TypeName,
    string? MethodName,
    string? FilePath,
    int? LineNumber);

public sealed record HealingTraceContext(string? TraceId, string? SpanId);

public sealed record HealingEvidenceMetadata(
    bool IsRedacted,
    bool IsTruncated,
    IReadOnlyList<string> OmittedFields);

public static class ComponentManifestKinds
{
    public const string Application = "application";
    public const string Package = "package";
    public const string Assembly = "assembly";
}

/// <summary>
/// Immutable build inventory for one application revision. Source fields are advisory and never grant mutation
/// authority; Platform-owned source ownership bindings remain authoritative.
/// </summary>
public sealed record ComponentManifestDocument(
    string SchemaVersion,
    Guid ApplicationId,
    Guid RevisionId,
    string SourceRevision,
    string? BuildId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ComponentManifestEntry> Components,
    IReadOnlyList<ComponentManifestDependency> Dependencies);

public sealed record ComponentManifestEntry(
    string ComponentKey,
    string Kind,
    string Name,
    string? Version,
    string ContentHash,
    string RelativePath,
    bool IsDirectDependency,
    string? PackageId = null,
    string? PackageVersion = null,
    string? AssemblyName = null,
    string? AssemblyVersion = null,
    string? PublicKeyToken = null,
    string? SourceRepositoryUrl = null,
    string? SourceRepositoryCommit = null,
    string? SourceRoot = null);

public sealed record ComponentManifestDependency(string FromComponentKey, string ToComponentKey);

public sealed record ProviderRepositoryReference(
    Guid ProviderConnectionId,
    string RepositoryProviderId,
    string Owner,
    string Name);

public sealed record RepairWorkItemUpsertRequest(
    string ProtocolVersion,
    ProviderRepositoryReference Repository,
    Guid IncidentId,
    Guid EpisodeId,
    string Title,
    string MachineSummary,
    string MachineSummaryHash,
    string IdempotencyKey);

public sealed record ProviderWorkItemReference(
    string ProviderWorkItemId,
    long Number,
    Uri Url,
    string State,
    string? ProviderCorrelationId);

/// <summary>
/// Minimal dispatch instruction. Exception evidence and protected data are retrieved later through the scoped
/// workload capability and are deliberately absent from this contract.
/// </summary>
public sealed record RepairWorkflowDispatchRequest(
    string ProtocolVersion,
    ProviderRepositoryReference Repository,
    string WorkflowIdentity,
    string WorkflowRevision,
    Uri PlatformBaseUrl,
    Guid IncidentId,
    Guid EpisodeId,
    Guid AttemptId,
    string OneTimeNonce,
    string ProducingRevisionStatus,
    string TargetBranch,
    string ExpectedTargetRevision,
    string IdempotencyKey);

public sealed record ProviderOperationReceipt(
    string IdempotencyKey,
    string? ProviderCorrelationId,
    bool IsReplay,
    DateTimeOffset AcceptedAt);

public sealed record ProviderPullRequestReference(
    string ProviderPullRequestId,
    long Number,
    Uri Url,
    string HeadRevision,
    string BaseRevision,
    bool IsDraft,
    string? ProviderCorrelationId);

/// <summary>
/// Provider-neutral output produced by a repair agent. Reproduction is recorded explicitly so a
/// high-confidence repair may still be reviewed when the original failure could not be reproduced.
/// </summary>
public sealed record RepairResultEnvelope(
    string ProtocolVersion,
    Guid AttemptId,
    string WorkflowRunId,
    int WorkflowRunAttempt,
    string BaseRevision,
    string TargetRevision,
    string Classification,
    decimal Confidence,
    string CausalSummary,
    string UnifiedDiff,
    string PatchDigest,
    IReadOnlyList<RepairChangedPathSuggestion> ChangedPaths,
    RepairReproductionEvidence Reproduction,
    RepairRegressionEvidence Regression,
    IReadOnlyList<RepairValidationResult> Validation,
    IReadOnlyList<string> RiskSuggestions,
    string RollbackSummary,
    RepairUsageSummary Usage,
    RepairTimingSummary Timing,
    DateTimeOffset SubmittedAt);

public sealed record RepairChangedPathSuggestion(string Path, string ChangeKind, string? RiskCategory);

public sealed record RepairReproductionEvidence(
    bool WasAttempted,
    bool WasReproduced,
    string Classification,
    string Summary,
    IReadOnlyList<string> Commands);

public sealed record RepairRegressionEvidence(
    bool WasAdded,
    string Summary,
    IReadOnlyList<string> ChangedTests);

public sealed record RepairValidationResult(
    string Kind,
    string Command,
    string Outcome,
    string SafeSummary,
    TimeSpan Duration);

public sealed record RepairUsageSummary(
    long InputUnits,
    long OutputUnits,
    TimeSpan AgentDuration,
    TimeSpan RepositoryRunDuration);

public sealed record RepairTimingSummary(DateTimeOffset StartedAt, DateTimeOffset CompletedAt);

/// <summary>
/// Bounded, redacted evidence delivered to a repair agent. The JSON document is data, never an instruction stream.
/// </summary>
public sealed record RepairEvidenceBundle(
    string ProtocolVersion,
    Guid AttemptId,
    string Tier,
    string CanonicalJson,
    string Digest,
    IReadOnlyList<string> OmittedFields,
    DateTimeOffset ExpiresAt);

public sealed record RepairAgentBudget(
    TimeSpan TimeLimit,
    long InferenceUnitLimit,
    long RepositoryRunLimit);

public sealed record RepairAgentRequest(
    string ProtocolVersion,
    Guid AttemptId,
    string BaseRevision,
    string TargetRevision,
    string? ProducingRevision,
    RepairEvidenceBundle Evidence,
    RepairAgentBudget Budget);

public interface IRepairAgentGateway
{
    ValueTask<RepairResultEnvelope> AnalyzeAsync(
        RepairAgentRequest request,
        CancellationToken cancellationToken = default);
}

public static class PolicyDecisions
{
    public const string Deny = "deny";
    public const string HumanOnly = "human-only";
    public const string AllowPublication = "allow-publication";
    public const string AllowAutomaticMerge = "allow-automatic-merge";
}

public enum PolicyGateState
{
    Pass,
    Block,
    Unknown,
    Stale
}

public sealed record PolicyGateResult(
    string Gate,
    PolicyGateState State,
    string ReasonCode,
    string? SafeDetail = null);

/// <summary>
/// Immutable evidence that a versioned policy was evaluated against a particular input snapshot.
/// Provider adapters consume this decision but never invent or relax it.
/// </summary>
public sealed record PolicyEvaluationSnapshot(
    string ProtocolVersion,
    string PolicyVersion,
    string PolicyHash,
    string InputDigest,
    string Decision,
    IReadOnlyList<PolicyGateResult> Gates,
    DateTimeOffset EvaluatedAt);

public sealed record PolicyEvaluationRequest(
    string ProtocolVersion,
    string PolicyVersion,
    string PolicyHash,
    string InputDigest,
    IReadOnlyDictionary<string, string?> SafeInputs);

public sealed record RepairPublicationRequest(
    string ProtocolVersion,
    ProviderRepositoryReference Repository,
    Guid IncidentId,
    Guid EpisodeId,
    Guid AttemptId,
    string TargetBranch,
    string ExpectedTargetRevision,
    RepairResultEnvelope Result,
    PolicyEvaluationSnapshot PublicationPolicy,
    string IdempotencyKey);

public sealed record ProviderMergeSnapshot(
    string PullRequestId,
    string HeadRevision,
    string BaseRevision,
    IReadOnlyList<ProviderCheckSnapshot> Checks,
    IReadOnlyList<string> RequiredChecks,
    bool IsBranchProtectionSatisfied,
    DateTimeOffset ObservedAt);

public sealed record ProviderCheckSnapshot(string Name, string State, string? Revision, DateTimeOffset ObservedAt);

public sealed record ProviderMergeRequest(
    string ProtocolVersion,
    ProviderRepositoryReference Repository,
    string PullRequestId,
    string ExpectedHeadRevision,
    PolicyEvaluationSnapshot MergePolicy,
    string IdempotencyKey);

public interface IRepairWorkProvider
{
    ValueTask<ProviderWorkItemReference> UpsertWorkItemAsync(
        RepairWorkItemUpsertRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderOperationReceipt> DispatchWorkflowAsync(
        RepairWorkflowDispatchRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITrustedPatchPublisher
{
    ValueTask<ProviderPullRequestReference> PublishAsync(
        RepairPublicationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRepairMergeProvider
{
    ValueTask<ProviderMergeSnapshot> GetMergeSnapshotAsync(
        ProviderRepositoryReference repository,
        string pullRequestId,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderOperationReceipt> RequestMergeAsync(
        ProviderMergeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Operations granted to a repository workflow after workload identity exchange. No provider mutation scope exists.
/// </summary>
public static class WorkloadCapabilityScopes
{
    public const string ReadEvidence = "evidence.read";
    public const string HeartbeatAttempt = "attempt.heartbeat";
    public const string UploadResult = "result.upload";

    public static IReadOnlySet<string> All { get; } = new[]
    {
        ReadEvidence,
        HeartbeatAttempt,
        UploadResult
    }.ToFrozenSet(StringComparer.Ordinal);
}

public sealed record WorkloadIdentityExchangeRequest(
    string ProtocolVersion,
    Guid AttemptId,
    string OneTimeNonce,
    string IdentityAssertion);

public sealed record WorkloadCapabilityGrant(
    string ProtocolVersion,
    Guid AttemptId,
    string CapabilityToken,
    IReadOnlySet<string> AllowedScopes,
    DateTimeOffset ExpiresAt);

public sealed record WorkloadEvidenceRequest(string ProtocolVersion, Guid AttemptId);

public sealed record WorkloadEvidenceResponse(
    string ProtocolVersion,
    Guid AttemptId,
    RepairEvidenceBundle Evidence);

public sealed record WorkloadHeartbeatRequest(
    string ProtocolVersion,
    Guid AttemptId,
    string IdempotencyKey,
    DateTimeOffset RequestedAt);

public sealed record WorkloadHeartbeatReceipt(
    string ProtocolVersion,
    Guid AttemptId,
    DateTimeOffset LeaseExpiresAt,
    bool IsReplay);

public sealed record WorkloadResultUploadRequest(
    string ProtocolVersion,
    Guid AttemptId,
    string IdempotencyKey,
    RepairResultEnvelope Result);

public sealed record WorkloadResultUploadReceipt(
    string ProtocolVersion,
    Guid AttemptId,
    string ResultDigest,
    bool IsReplay,
    DateTimeOffset AcceptedAt);

public interface IHealingWorkloadApi
{
    ValueTask<WorkloadCapabilityGrant> ExchangeAsync(
        WorkloadIdentityExchangeRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkloadEvidenceResponse> GetEvidenceAsync(
        WorkloadEvidenceRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkloadHeartbeatReceipt> HeartbeatAsync(
        WorkloadHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkloadResultUploadReceipt> UploadResultAsync(
        WorkloadResultUploadRequest request,
        CancellationToken cancellationToken = default);
}

public static class DeploymentObservationSources
{
    public const string PlatformDeployment = "platform-deployment";
    public const string ExternalDelivery = "external-delivery";
}

public sealed record DeploymentObservationRequest(
    string ProtocolVersion,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId,
    string Revision,
    DateTimeOffset DeployedAt,
    string Source,
    string SourceObservationId,
    string TrustIdentity,
    string EvidenceDigest,
    string IdempotencyKey);

public sealed record DeploymentObservationReceipt(
    string ProtocolVersion,
    Guid ObservationId,
    bool IsReplay,
    DateTimeOffset AcceptedAt);

/// <summary>
/// Provider-neutral recurrence notification emitted when a repaired revision fails during verification.
/// </summary>
public sealed record RepairVerificationFailedSignal(
    string ProtocolVersion,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid IncidentId,
    Guid EpisodeId,
    string RepairedRevision,
    Guid SupportingOccurrenceId,
    string ReasonCode,
    DateTimeOffset DetectedAt);

public interface IDeploymentObservationSink
{
    ValueTask<DeploymentObservationReceipt> AppendAsync(
        DeploymentObservationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRepairVerificationSignalSink
{
    ValueTask AppendAsync(
        RepairVerificationFailedSignal signal,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe append-only audit wire event. SafeDetail contains capped, redacted values only.
/// </summary>
public sealed record HealingAuditEventContract(
    string ProtocolVersion,
    Guid EventId,
    Guid WorkspaceId,
    long Sequence,
    string AggregateType,
    Guid AggregateId,
    string EventType,
    string ReasonCode,
    string ActorType,
    string ActorId,
    Guid CorrelationId,
    Guid? CausationId,
    string? PolicyVersion,
    string? InputHash,
    string? OutputHash,
    IReadOnlyDictionary<string, string?> SafeDetail,
    DateTimeOffset OccurredAt);

public sealed record HealingAuditQuery(
    string ProtocolVersion,
    Guid WorkspaceId,
    Guid? ApplicationId,
    Guid? IncidentId,
    string? Cursor,
    int Take);

public sealed record HealingAuditPage(
    string ProtocolVersion,
    IReadOnlyList<HealingAuditEventContract> Items,
    string? NextCursor);

public interface IHealingAuditSink
{
    ValueTask AppendAsync(
        HealingAuditEventContract auditEvent,
        CancellationToken cancellationToken = default);
}

public interface IHealingAuditQuery
{
    ValueTask<HealingAuditPage> ListAsync(
        HealingAuditQuery query,
        CancellationToken cancellationToken = default);
}
