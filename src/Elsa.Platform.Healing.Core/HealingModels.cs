namespace Elsa.Platform.Healing.Core;

public enum HealingSignalSource { OpenTelemetry, ExplicitIncident }
public enum HealingInboxStatus { Pending, Leased, Completed, Rejected, DeadLettered }
public enum ComponentManifestTrustState { Unverified, Verified, Rejected, Revoked }
public enum ComponentKind { Unknown = 0, Application = 1, Package = 2, Assembly = 3 }
public enum SourceSelectorKind { Application, Package, Assembly, ComponentKey }
public enum SourceOwnershipBindingStatus { Draft, Active, Suspended, Revoked }
public enum IncidentClassification
{
    UnhandledRequest = 0,
    FatalStartup = 1,
    FatalBackground = 2,
    UnexpectedWorkflow = 3,
    UnexpectedActivity = 4,
    TransientExhausted = 5,
    ExplicitIncident = 6,
    Unknown = 7,
    Validation = 8,
    Authorization = 9,
    Cancellation = 10,
    Handled = 11,
    TransientRetrying = 12
}
public enum IncidentSeverity { Informational, Warning, Error, Fatal }
public enum IncidentRetryState { None, Retrying, Exhausted }
public enum EvidenceTier { DefaultRedacted, Elevated }
public enum HealingTelemetrySourceStatus { Active, Revoked }

[Flags]
public enum AttributionBasis { None = 0, StackFrame = 1, Assembly = 2, Package = 4, SourceLink = 8, ExplicitComponent = 16 }

public enum AttributionResolution { Selected, Candidate, Ambiguous, Unauthorized, Unmapped }
public enum HealingIncidentStatus { Observed, ThresholdPending, ReadyForRepair, Repairing, PullRequestOpen, ObservationOnly, Suppressed, NeedsHuman, Failed, Merged, Verifying, Healed, FailedVerification, Superseded, Waived }
public enum NeedsHumanReason { AttemptLimitReached, AmbiguousOwnership, UnauthorizedSource, InsufficientConfidence, RevisionUnverified, PolicyBlocked, VerificationFailed, OperatorStopped }
public enum IncidentEpisodeOutcome { Active, Healed, Failed, Superseded, Waived }
public enum VerificationOutcome { PendingDeployment, Deployed, DeployedUnverified, Healed, FailedVerification, Superseded, Waived }
public enum WorkItemProjectionStatus { Pending, Current, Stale, Failed, Deleted }
public enum RepairAttemptStatus { Queued, Dispatched, Running, ProposalReady, ResultReceived, Publishing, PullRequestOpen, Succeeded, Failed, Stopped, Expired }
public enum RepairClassification { Reproduced, InferredHighConfidence, InsufficientConfidence, RevisionUnverified }
public enum PullRequestMergeState { Open, MergeRequested, Merged, Closed }
public enum PolicyKind { Path, Evidence, Merge }
public enum PolicyGateResult { Pass, Block, Unknown, Stale }
public enum PolicyDecision { Deny, HumanOnly, AllowPublication, AllowAutomaticMerge }
public enum ProviderConnectionStatus { Active, Suspended, Revoked, PendingValidation }
public enum ProviderOperationKind { UpsertWorkItem, DispatchWorkflow, PublishPullRequest, RefreshChecks, RequestMerge }
public enum ProviderOperationStatus { Pending, Leased, Completed, Failed, DeadLettered }
public enum WorkloadIdentityExchangeStatus { Pending, Exchanged, Expired, Revoked }
public enum ProviderWebhookDeliveryStatus { Pending, Processing, Completed, Rejected, Failed }
public enum HumanCommandStatus { Pending, Authorized, Rejected, Executed, Failed }
public enum DeploymentObservationSource { PlatformDeployment, ExternalDelivery }

public static class HealingTransitionReasonCodes
{
    public const string Transitioned = "transitioned";
    public const string AlreadyInState = "already-in-state";
    public const string InvalidIncidentTransition = "invalid-incident-transition";
}

public sealed record HealingTransitionResult(
    bool Succeeded,
    HealingIncidentStatus From,
    HealingIncidentStatus To,
    string ReasonCode)
{
    public static HealingTransitionResult Allowed(HealingIncidentStatus from, HealingIncidentStatus to) =>
        new(true, from, to, HealingTransitionReasonCodes.Transitioned);

    public static HealingTransitionResult Rejected(HealingIncidentStatus from, HealingIncidentStatus to, string reasonCode) =>
        new(false, from, to, reasonCode);
}

public sealed class HealingConfiguration
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public bool DiscoveryEnabled { get; set; }
    public bool RepairEnabled { get; set; }
    public bool AutomaticMergeEnabled { get; set; }
    public string SignalProfileVersion { get; set; } = string.Empty;
    public int DefaultAttemptLimit { get; set; } = 2;
    public TimeSpan VerificationWindow { get; set; }
    public TimeSpan TimeBudget { get; set; }
    public int ConcurrencyBudget { get; set; }
    public long InferenceBudget { get; set; }
    public int RepositoryRunBudget { get; set; }
    public bool ApplicationKillSwitch { get; set; }
    public string ClassificationPolicyJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
    public List<HealingEnvironmentConfiguration> Environments { get; set; } = [];
}

public sealed class HealingWorkspaceConfiguration
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public bool WorkspaceKillSwitch { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class HealingEnvironmentConfiguration
{
    public Guid Id { get; set; }
    public Guid HealingConfigurationId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public bool? DiscoveryEnabled { get; set; }
    public bool? RepairEnabled { get; set; }
    public int? OccurrenceThreshold { get; set; }
    public TimeSpan? DebounceWindow { get; set; }
    public bool EnvironmentKillSwitch { get; set; }
    public string ClassificationPolicyJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

/// <summary>
/// A server-owned OTLP source registration. Credential material is persisted only as a salted hash;
/// workspace, application, and environment scope is never supplied by the monitored process.
/// </summary>
public sealed class HealingTelemetrySource
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte[] CredentialSalt { get; set; } = [];
    public byte[] CredentialHash { get; set; } = [];
    public int CredentialVersion { get; set; } = 1;
    public HealingTelemetrySourceStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class HealingSignalInboxItem
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public HealingSignalSource Source { get; set; }
    public string ProfileVersion { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public string RedactedEnvelopeJson { get; set; } = "{}";
    public string EnvelopeHash { get; set; } = string.Empty;
    public HealingInboxStatus Status { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? OutcomeCode { get; set; }
    public string? SafeOutcomeDetail { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class ComponentManifest
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid RevisionId { get; set; }
    public string SchemaVersion { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public string? BuildId { get; set; }
    public string ManifestDigest { get; set; } = string.Empty;
    public string CanonicalJson { get; set; } = "{}";
    public ComponentManifestTrustState TrustState { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationMethod { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ComponentManifestEntry> Entries { get; set; } = [];
    public List<ComponentDependency> Dependencies { get; set; } = [];
}

public sealed class ComponentManifestRegistration
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid RevisionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid ManifestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ComponentManifestEntry
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public string ComponentKey { get; set; } = string.Empty;
    public ComponentKind Kind { get; set; }
    public string KindName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? PackageId { get; set; }
    public string? PackageVersion { get; set; }
    public string? AssemblyName { get; set; }
    public string? AssemblyVersion { get; set; }
    public string? PublicKeyToken { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? RepositoryCommit { get; set; }
    public string? SourceRoot { get; set; }
    public bool IsDirectDependency { get; set; }
    public List<ComponentManifestAssemblyArtifact> Assemblies { get; set; } = [];
}

public sealed class ComponentManifestAssemblyArtifact
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid ComponentEntryId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? PublicKeyToken { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
}

public sealed class ComponentDependency
{
    public Guid Id { get; set; }
    public Guid ManifestId { get; set; }
    public Guid FromEntryId { get; set; }
    public Guid ToEntryId { get; set; }
}

public sealed class SourceOwnershipBinding
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SourceSelectorKind SelectorKind { get; set; }
    public string SelectorPattern { get; set; } = string.Empty;
    public int Priority { get; set; }
    public Guid ProviderConnectionId { get; set; }
    public string RepositoryProviderId { get; set; } = string.Empty;
    public string RepositoryOwner { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string WorkflowIdentity { get; set; } = string.Empty;
    public string WorkflowReference { get; set; } = string.Empty;
    public string WorkflowRevision { get; set; } = string.Empty;
    public Guid PathPolicyId { get; set; }
    public Guid EvidencePolicyId { get; set; }
    public Guid MergePolicyId { get; set; }
    public SourceOwnershipBindingStatus Status { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class IncidentOccurrence
{
    public Guid Id { get; set; }
    public Guid InboxItemId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid? RevisionId { get; set; }
    public string OccurrenceKey { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public IncidentClassification Classification { get; set; }
    public IncidentSeverity Severity { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string NormalizedStackJson { get; set; } = "[]";
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public IncidentRetryState RetryState { get; set; }
    public string FingerprintVersion { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public EvidenceTier EvidenceTier { get; set; }
    public string EvidenceDigest { get; set; } = string.Empty;
}

public sealed class ComponentAttribution
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OccurrenceId { get; set; }
    public Guid ComponentEntryId { get; set; }
    public Guid? BindingId { get; set; }
    public decimal Confidence { get; set; }
    public AttributionBasis Basis { get; set; }
    public AttributionResolution Resolution { get; set; }
    public string ReasonCodesJson { get; set; } = "[]";
}

public sealed class HealingIncident
{
    private static readonly IReadOnlyDictionary<HealingIncidentStatus, IReadOnlySet<HealingIncidentStatus>> AllowedTransitions =
        new Dictionary<HealingIncidentStatus, IReadOnlySet<HealingIncidentStatus>>
        {
            [HealingIncidentStatus.Observed] = States(HealingIncidentStatus.ThresholdPending, HealingIncidentStatus.ObservationOnly, HealingIncidentStatus.Suppressed, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.ThresholdPending] = States(HealingIncidentStatus.ReadyForRepair, HealingIncidentStatus.ObservationOnly, HealingIncidentStatus.Suppressed, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.ReadyForRepair] = States(HealingIncidentStatus.Repairing, HealingIncidentStatus.ObservationOnly, HealingIncidentStatus.Suppressed, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.Repairing] = States(HealingIncidentStatus.PullRequestOpen, HealingIncidentStatus.NeedsHuman, HealingIncidentStatus.Failed, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.PullRequestOpen] = States(HealingIncidentStatus.Merged, HealingIncidentStatus.Verifying, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.Merged] = States(HealingIncidentStatus.Verifying, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.Verifying] = States(HealingIncidentStatus.Healed, HealingIncidentStatus.FailedVerification, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.FailedVerification] = States(HealingIncidentStatus.ReadyForRepair, HealingIncidentStatus.NeedsHuman, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.ObservationOnly] = States(HealingIncidentStatus.ReadyForRepair, HealingIncidentStatus.Suppressed, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.Suppressed] = States(HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived),
            [HealingIncidentStatus.NeedsHuman] = States(HealingIncidentStatus.ReadyForRepair, HealingIncidentStatus.Superseded, HealingIncidentStatus.Waived)
        };

    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public string FingerprintVersion { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string RepairRepositoryKey { get; set; } = "observation-only";
    public HealingIncidentStatus Status { get; set; }
    public IncidentSeverity Severity { get; set; }
    public IncidentClassification Classification { get; set; }
    public Guid? SelectedBindingId { get; set; }
    public Guid? SelectedComponentEntryId { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public long OccurrenceCount { get; set; }
    public Guid? ActiveEpisodeId { get; set; }
    public Guid? WorkItemProjectionId { get; set; }
    public NeedsHumanReason? NeedsHumanReason { get; set; }
    public DateTimeOffset? ReadyAfter { get; set; }
    public byte[] Version { get; set; } = [];

    public HealingTransitionResult TryTransitionTo(HealingIncidentStatus target)
    {
        var from = Status;
        if (from == target)
            return HealingTransitionResult.Rejected(from, target, HealingTransitionReasonCodes.AlreadyInState);

        if (!AllowedTransitions.TryGetValue(from, out var targets) || !targets.Contains(target))
            return HealingTransitionResult.Rejected(from, target, HealingTransitionReasonCodes.InvalidIncidentTransition);

        Status = target;
        return HealingTransitionResult.Allowed(from, target);
    }

    private static IReadOnlySet<HealingIncidentStatus> States(params HealingIncidentStatus[] values) =>
        new HashSet<HealingIncidentStatus>(values);
}

public sealed class IncidentEpisode
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? PreviousEpisodeId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string ProducingRevisionsJson { get; set; } = "[]";
    public string? TargetRevision { get; set; }
    public IncidentEpisodeOutcome Outcome { get; set; }
    public string? RegressionReason { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class EnvironmentImpact
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid EnvironmentId { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public long OccurrenceCount { get; set; }
    public string ProducingRevisionsJson { get; set; } = "[]";
    public string? CurrentDeployedRevision { get; set; }
    public VerificationOutcome VerificationStatus { get; set; }
    public int OccurrenceThreshold { get; set; }
    public TimeSpan DebounceWindow { get; set; }
    public DateTimeOffset? ThresholdReachedAt { get; set; }
    public DateTimeOffset? ReadyAfter { get; set; }
    public string ClassificationPolicyVersion { get; set; } = "1";
    public string ClassificationPolicyHash { get; set; } = string.Empty;
    public string? ClosedByActorId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class RepairWorkItemProjection
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid ProviderConnectionId { get; set; }
    public string? ProviderWorkItemId { get; set; }
    public long? Number { get; set; }
    public string? Url { get; set; }
    public string MachineSummaryHash { get; set; } = string.Empty;
    public string? ProviderState { get; set; }
    public WorkItemProjectionStatus ProjectionStatus { get; set; }
    public DateTimeOffset? LastProjectedAt { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class RepairAttempt
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid BindingId { get; set; }
    public int AttemptNumber { get; set; }
    public string? ProducingRevision { get; set; }
    public string TargetRevision { get; set; } = string.Empty;
    public RepairAttemptStatus Status { get; set; }
    public Guid EvidenceBundleId { get; set; }
    public RepairClassification RepairClassification { get; set; }
    public string NonceHash { get; set; } = string.Empty;
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string BudgetJson { get; set; } = "{}";
    public string UsageJson { get; set; } = "{}";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? OutcomeCode { get; set; }
    public string? SafeOutcomeDetail { get; set; }
    public byte[] Version { get; set; } = [];
}

public enum ManagedRepairProposalStatus { Ready, Finalized, Expired, Rejected }

/// <summary>Immutable Platform-generated patch proposal. Repository runners may validate it but cannot replace it.</summary>
public sealed class ManagedRepairProposal
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid AttemptId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SourceContextDigest { get; set; } = string.Empty;
    public string ProposalDigest { get; set; } = string.Empty;
    public string ProposalJson { get; set; } = "{}";
    public string FinalizationNonceHash { get; set; } = string.Empty;
    public string ProtectedFinalizationNonce { get; set; } = string.Empty;
    public ManagedRepairProposalStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class EvidenceBundle
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid IncidentId { get; set; }
    public EvidenceTier Tier { get; set; }
    public string CanonicalJson { get; set; } = "{}";
    public string Digest { get; set; } = string.Empty;
    public string ProvenanceJson { get; set; } = "{}";
    public string OmissionsJson { get; set; } = "[]";
    public int SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class EvidenceAccessDecision
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? ReleasedBundleId { get; set; }
    public string RequesterId { get; set; } = string.Empty;
    public EvidenceTier RequestedTier { get; set; }
    public string RequestedFieldsJson { get; set; } = "[]";
    public string Purpose { get; set; } = string.Empty;
    public bool Authorized { get; set; }
    public string ReasonCodesJson { get; set; } = "[]";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

public sealed class RepairResult
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid? ProposalId { get; set; }
    public string? ProposalDigest { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string WorkflowRunId { get; set; } = string.Empty;
    public int WorkflowRunAttempt { get; set; }
    public string BaseRevision { get; set; } = string.Empty;
    public string TargetRevision { get; set; } = string.Empty;
    public RepairClassification Classification { get; set; }
    public decimal Confidence { get; set; }
    public string UnifiedDiff { get; set; } = string.Empty;
    public string PatchDigest { get; set; } = string.Empty;
    public string EnvelopeDigest { get; set; } = string.Empty;
    public string ChangedPathsJson { get; set; } = "[]";
    public string ReproductionJson { get; set; } = "{}";
    public string RegressionJson { get; set; } = "{}";
    public string ValidationJson { get; set; } = "{}";
    public string RiskJson { get; set; } = "{}";
    public DateTimeOffset SubmittedAt { get; set; }
}

public sealed class RepairPullRequest
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid ProviderConnectionId { get; set; }
    public string ProviderPullRequestId { get; set; } = string.Empty;
    public long Number { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string BaseRevision { get; set; } = string.Empty;
    public string HeadRevision { get; set; } = string.Empty;
    public string PatchDigest { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public RepairClassification Classification { get; set; }
    public string CheckSnapshotJson { get; set; } = "{}";
    public string BranchProtectionSnapshotJson { get; set; } = "{}";
    public Guid? MergePolicyEvaluationId { get; set; }
    public PullRequestMergeState MergeState { get; set; }
    public string? MergedRevision { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public string? ClosureReason { get; set; }
    public byte[] Version { get; set; } = [];
}

public abstract class HealingPolicyDefinition
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class PathPolicy : HealingPolicyDefinition
{
    public string AllowedRootsJson { get; set; } = "[]";
    public string ForbiddenRootsJson { get; set; } = "[]";
    public int MaxFiles { get; set; }
    public int MaxChangedLines { get; set; }
    public int MaxPatchBytes { get; set; }
    public bool AllowBinary { get; set; }
    public bool AllowRenames { get; set; }
    public bool AllowSymlinks { get; set; }
    public bool AllowSubmodules { get; set; }
}

public sealed class EvidencePolicy : HealingPolicyDefinition
{
    public bool RequireReproduction { get; set; }
    public bool AllowHighConfidenceInference { get; set; }
    public decimal MinimumInferenceConfidence { get; set; }
    public EvidenceTier MaximumTier { get; set; }
    public string PermittedFieldsJson { get; set; } = "[]";
}

public sealed class MergePolicy : HealingPolicyDefinition
{
    public bool AutomaticMergeEnabled { get; set; }
    public string RequiredChecksJson { get; set; } = "[]";
    public string? IndependentVerifier { get; set; }
    public string ForbiddenChangeCategoriesJson { get; set; } = "[]";
    public bool RequireRollbackOrStopCapability { get; set; }
}

public sealed class PolicyEvaluation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid PolicyId { get; set; }
    public PolicyKind PolicyKind { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicyHash { get; set; } = string.Empty;
    public string InputSnapshotHash { get; set; } = string.Empty;
    public string GateResultsJson { get; set; } = "[]";
    public PolicyDecision Decision { get; set; }
    public string ReasonCodesJson { get; set; } = "[]";
    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class ProviderConnection
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string RepositoryProviderId { get; set; } = string.Empty;
    public string RepositoryOwner { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string CredentialReference { get; set; } = string.Empty;
    public string? WebhookSecretReference { get; set; }
    public ProviderConnectionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class ProviderOperation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid ProviderConnectionId { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid? AttemptId { get; set; }
    public ProviderOperationKind Kind { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string PayloadHash { get; set; } = string.Empty;
    public ProviderOperationStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? ProviderCorrelationId { get; set; }
    public string? ResultJson { get; set; }
    public string? OutcomeCode { get; set; }
    public string? SafeError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

/// <summary>
/// Durable provider-side idempotency journal. This is deliberately separate from the leased provider outbox:
/// a remote mutation may reserve a receipt while an outbox operation itself is already leased.
/// </summary>
public sealed class ProviderMutationJournalEntry
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProviderConnectionId { get; set; }
    public ProviderOperationKind Kind { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string SafePayloadJson { get; set; } = "{}";
    public string PayloadHash { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class WorkloadIdentityExchange
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid? ProposalId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string ScopesJson { get; set; } = "[]";
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string RepositoryProviderId { get; set; } = string.Empty;
    public string RepositoryOwner { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string WorkflowReference { get; set; } = string.Empty;
    public string WorkflowRevision { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public string WorkflowRunId { get; set; } = string.Empty;
    public int WorkflowRunAttempt { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string JwtId { get; set; } = string.Empty;
    public string NonceHash { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ExchangedAt { get; set; }
    public string? CapabilityTokenHash { get; set; }
    public WorkloadIdentityExchangeStatus Status { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class WorkloadHeartbeat
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid AttemptId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset LeaseExpiresAt { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}

public sealed class ProviderWebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ProviderDeliveryId { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string RepositoryProviderId { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string? Action { get; set; }
    public string BodyDigest { get; set; } = string.Empty;
    public string? RetainedBody { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public ProviderWebhookDeliveryStatus Status { get; set; }
    public string? OutcomeCode { get; set; }
    public string? SafeOutcomeDetail { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class HumanCommand
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid IncidentId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string ProviderActorId { get; set; } = string.Empty;
    public string? PlatformActorId { get; set; }
    public string ProviderPermissionSnapshotJson { get; set; } = "{}";
    public bool WorkspacePermissionGranted { get; set; }
    public Guid? ConfirmationId { get; set; }
    public HumanCommandStatus Status { get; set; }
    public string? ResultCode { get; set; }
    public string? SafeResultDetail { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class DeploymentObservation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Revision { get; set; } = string.Empty;
    public DateTimeOffset DeployedAt { get; set; }
    public DeploymentObservationSource Source { get; set; }
    public string SourceIdempotencyKey { get; set; } = string.Empty;
    public string TrustIdentity { get; set; } = string.Empty;
    public string EvidenceDigest { get; set; } = string.Empty;
    public DateTimeOffset AcceptedAt { get; set; }
}

public sealed class VerificationResult
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EpisodeId { get; set; }
    public Guid EnvironmentId { get; set; }
    public string RepairedRevision { get; set; } = string.Empty;
    public DateTimeOffset? WindowStartedAt { get; set; }
    public DateTimeOffset? WindowEndsAt { get; set; }
    public long RelevantOperationSuccessCount { get; set; }
    public DateTimeOffset? LastRelevantOperationSuccessAt { get; set; }
    public long RecurrenceCount { get; set; }
    public DateTimeOffset? LastRecurrenceAt { get; set; }
    public VerificationOutcome Outcome { get; set; }
    public Guid? DeploymentObservationId { get; set; }
    public Guid? SupportingOccurrenceId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class HealingAuditEvent
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public long Sequence { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public string? PolicyVersion { get; set; }
    public string? InputHash { get; set; }
    public string? OutputHash { get; set; }
    public string SafeDetailJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
}
