using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Healing.Core.Configuration;

namespace Elsa.Platform.Healing.Core.Repairs;

public enum RepairTargetState { Unknown, Unresolved, AlreadyFixed }
public enum RepairAttemptCreationOutcome { Created, AlreadyFixed, AttemptLimitReached, ConcurrencyLimitReached, TargetStateUnknown, TargetRevisionChanged, EvidenceUnavailable, Conflict }
public enum RepairAttemptStoreOutcome { Created, AttemptLimitReached, ConcurrencyLimitReached, Conflict }
public enum ReproductionOutcome { Unknown, Reproduced, NotReproduced, NotAttempted }

public sealed record RepairTargetInspectionRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid IncidentId,
    Guid EpisodeId,
    Guid BindingId,
    string ExpectedTargetRevision);

public sealed record RepairTargetInspection(RepairTargetState State, string CurrentTargetRevision);

public sealed record CreateRepairAttemptRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid IncidentId,
    Guid EpisodeId,
    Guid BindingId,
    Guid EvidenceBundleId,
    string ExpectedTargetRevision,
    string? ProducingRevision,
    bool ProducingRevisionVerified,
    string BudgetJson,
    int MaximumAttempts = RepairOrchestrationService.MaximumAttempts,
    int MaximumConcurrentAttempts = HealingBudgetOptions.MaximumConcurrency);

public sealed record RepairAttemptCreationResult(
    RepairAttemptCreationOutcome Outcome,
    RepairAttempt? Attempt,
    string? OneTimeNonce,
    string ReasonCode)
{
    public bool Succeeded => Outcome == RepairAttemptCreationOutcome.Created;
}

public sealed record RepairAttemptStoreCreateResult(RepairAttemptStoreOutcome Outcome, RepairAttempt? Attempt);

public sealed record RepairAttemptLeaseResult(
    bool Succeeded,
    string ReasonCode,
    string? LeaseToken,
    DateTimeOffset? ExpiresAt);

public sealed record RepairReproductionSubmission(
    Guid WorkspaceId,
    Guid AttemptId,
    string LeaseToken,
    ReproductionOutcome Outcome,
    decimal Confidence,
    string? ReasonCode,
    string EvidenceDigest);

public sealed record RepairReproductionResult(
    bool Succeeded,
    string ReasonCode,
    RepairClassification Classification,
    bool ReproductionAttempted,
    bool Reproduced);

public interface IRepairTargetInspector
{
    ValueTask<RepairTargetInspection> InspectAsync(
        RepairTargetInspectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Atomic persistence boundary for repair execution. Implementations must enforce the episode attempt cap,
/// application concurrency cap, and every lease-token/expiry predicate in the same transaction as the mutation.
/// </summary>
public interface IRepairOrchestrationStore
{
    ValueTask<RepairAttemptStoreCreateResult> TryCreateAttemptAsync(
        RepairAttempt attempt,
        int maximumAttempts,
        int maximumConcurrentAttempts,
        CancellationToken cancellationToken = default);

    ValueTask<RepairAttempt?> FindAttemptAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryAcquireLeaseAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseOwner,
        string leaseTokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryHeartbeatLeaseAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseTokenHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryRecordReproductionAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseTokenHash,
        RepairClassification classification,
        string reproductionJson,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

public sealed class RepairOrchestrationService(
    IRepairOrchestrationStore store,
    IHealingEvidenceStore evidenceStore,
    IRepairTargetInspector targetInspector,
    TimeProvider? timeProvider = null)
{
    public const int MaximumAttempts = 2;
    public static readonly TimeSpan MaximumLeaseDuration = TimeSpan.FromMinutes(15);
    public const decimal HighConfidenceThreshold = 0.80m;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<RepairAttemptCreationResult> CreateAttemptAsync(
        CreateRepairAttemptRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCreateRequest(request);
        var evidence = await evidenceStore.FindBundleAsync(request.WorkspaceId, request.EvidenceBundleId, cancellationToken);
        if (!IsUsableEvidence(evidence, request, _timeProvider.GetUtcNow()))
        {
            return Rejected(RepairAttemptCreationOutcome.EvidenceUnavailable, "evidence-unavailable");
        }
        var usableEvidence = evidence!;

        var inspection = await targetInspector.InspectAsync(new RepairTargetInspectionRequest(
            request.WorkspaceId,
            request.ApplicationId,
            request.IncidentId,
            request.EpisodeId,
            request.BindingId,
            request.ExpectedTargetRevision), cancellationToken);
        if (inspection.State == RepairTargetState.Unknown
            || !Enum.IsDefined(inspection.State)
            || string.IsNullOrWhiteSpace(inspection.CurrentTargetRevision))
            return Rejected(RepairAttemptCreationOutcome.TargetStateUnknown, "target-state-unknown");
        if (!string.Equals(inspection.CurrentTargetRevision, request.ExpectedTargetRevision, StringComparison.OrdinalIgnoreCase))
            return Rejected(RepairAttemptCreationOutcome.TargetRevisionChanged, "target-revision-changed");
        if (inspection.State == RepairTargetState.AlreadyFixed)
            return Rejected(RepairAttemptCreationOutcome.AlreadyFixed, "already-fixed");
        if (inspection.State != RepairTargetState.Unresolved)
            return Rejected(RepairAttemptCreationOutcome.TargetStateUnknown, "target-state-unknown");

        var nonceBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var nonce = Base64UrlEncode(nonceBytes);
            var attempt = new RepairAttempt
            {
                Id = Guid.NewGuid(),
                WorkspaceId = request.WorkspaceId,
                ApplicationId = request.ApplicationId,
                IncidentId = request.IncidentId,
                EpisodeId = request.EpisodeId,
                BindingId = request.BindingId,
                ProducingRevision = request.ProducingRevision,
                TargetRevision = inspection.CurrentTargetRevision,
                Status = RepairAttemptStatus.Queued,
                EvidenceBundleId = usableEvidence.Id,
                RepairClassification = request.ProducingRevisionVerified && !string.IsNullOrWhiteSpace(request.ProducingRevision)
                    ? RepairClassification.InsufficientConfidence
                    : RepairClassification.RevisionUnverified,
                NonceHash = HashSecret(nonce),
                BudgetJson = request.BudgetJson,
                UsageJson = "{}"
            };

            var created = await store.TryCreateAttemptAsync(
                attempt,
                request.MaximumAttempts,
                request.MaximumConcurrentAttempts,
                cancellationToken);
            return created.Outcome switch
            {
                RepairAttemptStoreOutcome.Created when created.Attempt is not null =>
                    new RepairAttemptCreationResult(RepairAttemptCreationOutcome.Created, created.Attempt, nonce, "created"),
                RepairAttemptStoreOutcome.AttemptLimitReached =>
                    Rejected(RepairAttemptCreationOutcome.AttemptLimitReached, "attempt-limit-reached"),
                RepairAttemptStoreOutcome.ConcurrencyLimitReached =>
                    Rejected(RepairAttemptCreationOutcome.ConcurrencyLimitReached, "concurrency-limit-reached"),
                _ => Rejected(RepairAttemptCreationOutcome.Conflict, "attempt-create-conflict")
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonceBytes);
        }
    }

    public async ValueTask<RepairAttemptLeaseResult> AcquireLeaseAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseOwner,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ValidateLeaseRequest(workspaceId, attemptId, leaseOwner, duration);
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var token = Base64UrlEncode(tokenBytes);
            var now = _timeProvider.GetUtcNow();
            var expiresAt = now.Add(duration);
            var acquired = await store.TryAcquireLeaseAsync(
                workspaceId,
                attemptId,
                leaseOwner,
                HashSecret(token),
                now,
                expiresAt,
                cancellationToken);
            return acquired
                ? new RepairAttemptLeaseResult(true, "lease-acquired", token, expiresAt)
                : new RepairAttemptLeaseResult(false, "lease-unavailable", null, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public async ValueTask<RepairAttemptLeaseResult> HeartbeatAsync(
        Guid workspaceId,
        Guid attemptId,
        string leaseToken,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ValidateLeaseRequest(workspaceId, attemptId, "heartbeat", duration);
        if (string.IsNullOrWhiteSpace(leaseToken))
            throw new ArgumentException("LeaseToken is required.", nameof(leaseToken));

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(duration);
        var renewed = await store.TryHeartbeatLeaseAsync(
            workspaceId,
            attemptId,
            HashSecret(leaseToken),
            now,
            expiresAt,
            cancellationToken);
        return renewed
            ? new RepairAttemptLeaseResult(true, "lease-renewed", null, expiresAt)
            : new RepairAttemptLeaseResult(false, "lease-lost", null, null);
    }

    public async ValueTask<RepairReproductionResult> RecordReproductionAsync(
        RepairReproductionSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ValidateReproductionSubmission(submission);
        var attempt = await store.FindAttemptAsync(submission.WorkspaceId, submission.AttemptId, cancellationToken);
        if (attempt is null)
            return ReproductionRejected("attempt-not-found");

        var attempted = submission.Outcome is ReproductionOutcome.Reproduced or ReproductionOutcome.NotReproduced;
        var reproduced = submission.Outcome == ReproductionOutcome.Reproduced;
        var classification = attempt.RepairClassification == RepairClassification.RevisionUnverified
            ? RepairClassification.RevisionUnverified
            : submission.Outcome switch
            {
                ReproductionOutcome.Reproduced => RepairClassification.Reproduced,
                ReproductionOutcome.NotReproduced or ReproductionOutcome.NotAttempted
                    when submission.Confidence >= HighConfidenceThreshold => RepairClassification.InferredHighConfidence,
                _ => RepairClassification.InsufficientConfidence
            };

        var reproductionJson = JsonSerializer.Serialize(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["confidence"] = submission.Confidence,
            ["evidenceDigest"] = submission.EvidenceDigest,
            ["outcome"] = submission.Outcome.ToString(),
            ["reasonCode"] = submission.ReasonCode,
            ["reproduced"] = reproduced,
            ["reproductionAttempted"] = attempted
        });
        var recorded = await store.TryRecordReproductionAsync(
            submission.WorkspaceId,
            submission.AttemptId,
            HashSecret(submission.LeaseToken),
            classification,
            reproductionJson,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return recorded
            ? new RepairReproductionResult(true, "reproduction-recorded", classification, attempted, reproduced)
            : ReproductionRejected("lease-lost");
    }

    private static RepairAttemptCreationResult Rejected(RepairAttemptCreationOutcome outcome, string reasonCode) =>
        new(outcome, null, null, reasonCode);

    private static RepairReproductionResult ReproductionRejected(string reasonCode) =>
        new(false, reasonCode, RepairClassification.InsufficientConfidence, false, false);

    private static void ValidateCreateRequest(CreateRepairAttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredId(request.WorkspaceId, nameof(request.WorkspaceId));
        ValidateRequiredId(request.ApplicationId, nameof(request.ApplicationId));
        ValidateRequiredId(request.IncidentId, nameof(request.IncidentId));
        ValidateRequiredId(request.EpisodeId, nameof(request.EpisodeId));
        ValidateRequiredId(request.BindingId, nameof(request.BindingId));
        ValidateRequiredId(request.EvidenceBundleId, nameof(request.EvidenceBundleId));
        ValidateRevision(request.ExpectedTargetRevision, nameof(request.ExpectedTargetRevision));
        if (request.ProducingRevision is not null)
            ValidateRevision(request.ProducingRevision, nameof(request.ProducingRevision));
        if (request.ProducingRevisionVerified && string.IsNullOrWhiteSpace(request.ProducingRevision))
            throw new ArgumentException("A verified producing revision is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.BudgetJson) || Encoding.UTF8.GetByteCount(request.BudgetJson) > 8_192)
            throw new ArgumentException("BudgetJson is required and must not exceed 8192 bytes.", nameof(request));
        if (request.MaximumAttempts is < 1 or > MaximumAttempts)
            throw new ArgumentOutOfRangeException(nameof(request), $"MaximumAttempts must be between one and {MaximumAttempts}.");
        if (request.MaximumConcurrentAttempts is < 1 or > HealingBudgetOptions.MaximumConcurrency)
            throw new ArgumentOutOfRangeException(nameof(request),
                $"MaximumConcurrentAttempts must be between one and {HealingBudgetOptions.MaximumConcurrency}.");
        ValidateBudget(request.BudgetJson, request);
    }

    private static void ValidateLeaseRequest(Guid workspaceId, Guid attemptId, string leaseOwner, TimeSpan duration)
    {
        ValidateRequiredId(workspaceId, nameof(workspaceId));
        ValidateRequiredId(attemptId, nameof(attemptId));
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 128)
            throw new ArgumentException("LeaseOwner is required and must not exceed 128 characters.", nameof(leaseOwner));
        if (duration <= TimeSpan.Zero || duration > MaximumLeaseDuration)
            throw new ArgumentOutOfRangeException(nameof(duration), $"Lease duration must be positive and at most {MaximumLeaseDuration}.");
    }

    private static void ValidateReproductionSubmission(RepairReproductionSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidateRequiredId(submission.WorkspaceId, nameof(submission.WorkspaceId));
        ValidateRequiredId(submission.AttemptId, nameof(submission.AttemptId));
        if (string.IsNullOrWhiteSpace(submission.LeaseToken))
            throw new ArgumentException("LeaseToken is required.", nameof(submission));
        if (submission.Outcome == ReproductionOutcome.Unknown)
            throw new ArgumentException("An explicit reproduction outcome is required.", nameof(submission));
        if (submission.Confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(submission), "Confidence must be between zero and one.");
        if (submission.Outcome is ReproductionOutcome.NotReproduced or ReproductionOutcome.NotAttempted
            && (string.IsNullOrWhiteSpace(submission.ReasonCode) || submission.ReasonCode.Length > 128))
            throw new ArgumentException("An explicit bounded reason is required when reproduction did not succeed.", nameof(submission));
        if (!IsSafeCode(submission.ReasonCode))
            throw new ArgumentException("ReasonCode contains unsupported characters.", nameof(submission));
        if (submission.EvidenceDigest.Length != 64 || !submission.EvidenceDigest.All(char.IsAsciiHexDigit))
            throw new ArgumentException("EvidenceDigest must be a SHA-256 hexadecimal digest.", nameof(submission));
    }

    private static bool IsSafeCode(string? value) => value is null || value.All(x =>
        char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.' or '/' or ':');

    private static void ValidateRequiredId(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name);
    }

    private static void ValidateRevision(string value, string name)
    {
        if (value.Length is < 7 or > 64 || !value.All(char.IsAsciiHexDigit))
            throw new ArgumentException($"{name} must be a hexadecimal source revision between 7 and 64 characters.", name);
    }

    private static bool IsUsableEvidence(
        EvidenceBundle? evidence,
        CreateRepairAttemptRequest request,
        DateTimeOffset now)
    {
        if (evidence is null
            || evidence.ApplicationId != request.ApplicationId
            || evidence.IncidentId != request.IncidentId
            || evidence.ExpiresAt <= now)
            return false;

        var canonicalBytes = Encoding.UTF8.GetBytes(evidence.CanonicalJson);
        if (canonicalBytes.Length != evidence.SizeBytes
            || canonicalBytes.Length > HealingEvidenceService.MaximumBundleBytes
            || evidence.Digest.Length != 64
            || !evidence.Digest.All(char.IsAsciiHexDigit))
            return false;

        var expectedDigest = SHA256.HashData(canonicalBytes);
        var actualDigest = Convert.FromHexString(evidence.Digest);
        return CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest);
    }

    private static void ValidateBudget(string budgetJson, CreateRepairAttemptRequest request)
    {
        try
        {
            using var document = JsonDocument.Parse(budgetJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("BudgetJson must be an object of bounded integer measures.", nameof(request));

            var allowedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "maxDurationSeconds", "maxPatchBytes", "maxSteps", "maxTokens"
            };
            var count = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                count++;
                if (!allowedFields.Contains(property.Name)
                    || property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt64(out var value)
                    || value is < 0 or > 1_000_000_000)
                    throw new ArgumentException("BudgetJson contains an unsupported or invalid measure.", nameof(request));
            }

            if (count == 0)
                throw new ArgumentException("BudgetJson must contain at least one bounded measure.", nameof(request));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("BudgetJson must contain valid JSON.", nameof(request), exception);
        }
    }

    private static string HashSecret(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
