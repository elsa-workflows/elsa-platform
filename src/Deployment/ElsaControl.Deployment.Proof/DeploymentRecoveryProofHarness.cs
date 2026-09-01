using System.Text.RegularExpressions;

namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Runs a provider-neutral restore-to-new proof. The provider owns the actual restore mechanics;
/// this harness owns ordering, safety gates, timing objectives, evidence, and bounded cleanup.
/// </summary>
public sealed partial class DeploymentRecoveryProofHarness
{
    public static readonly TimeSpan DefaultMaximumRpo = TimeSpan.FromHours(24);

    public static readonly TimeSpan DefaultMaximumRto = TimeSpan.FromHours(4);

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cleanupTimeout;
    private readonly TimeSpan _maximumRpo;
    private readonly TimeSpan _maximumRto;

    public DeploymentRecoveryProofHarness(
        TimeProvider? timeProvider = null,
        TimeSpan? cleanupTimeout = null,
        TimeSpan? maximumRpo = null,
        TimeSpan? maximumRto = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cleanupTimeout = ValidatePositive(cleanupTimeout ?? TimeSpan.FromMinutes(2), nameof(cleanupTimeout));
        _maximumRpo = ValidatePositive(maximumRpo ?? DefaultMaximumRpo, nameof(maximumRpo));
        _maximumRto = ValidatePositive(maximumRto ?? DefaultMaximumRto, nameof(maximumRto));
    }

    public async Task<DeploymentRecoveryProofReport> RunAsync(
        DeploymentRecoveryPoint recoveryPoint,
        IDeploymentRecoveryProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recoveryPoint);
        ArgumentNullException.ThrowIfNull(provider);

        var startedAt = _timeProvider.GetUtcNow();
        var stages = new List<DeploymentRecoveryStageResult>();
        DeploymentRecoveryTarget? target = null;
        var cleanupAllowed = false;
        var failed = false;
        var cutoverEligible = false;
        DateTimeOffset? eligibilityAt = null;
        var rpoAge = startedAt - recoveryPoint.RestorePointAt;
        if (rpoAge < TimeSpan.Zero)
            rpoAge = TimeSpan.Zero;

        if (cancellationToken.IsCancellationRequested)
        {
            stages.Add(Failure(
                DeploymentRecoveryStage.RecoveryPointValidation,
                CancelledCode(DeploymentRecoveryStage.RecoveryPointValidation),
                CancelledMessage(DeploymentRecoveryStage.RecoveryPointValidation)));
            failed = true;
        }
        else if (!ValidateRecoveryPoint(recoveryPoint, startedAt, _maximumRpo, out var pointCode))
        {
            stages.Add(Failure(
                DeploymentRecoveryStage.RecoveryPointValidation,
                pointCode,
                MessageFor(pointCode)));
            failed = true;
        }
        else
        {
            stages.Add(Passed(
                DeploymentRecoveryStage.RecoveryPointValidation,
                startedAt,
                _timeProvider.GetUtcNow(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceInstanceId"] = recoveryPoint.SourceInstanceId,
                    ["organizationId"] = recoveryPoint.OrganizationId,
                    ["workspaceId"] = recoveryPoint.WorkspaceId,
                    ["recoveryPointId"] = recoveryPoint.RecoveryPointId,
                    ["sourceLifecycle"] = recoveryPoint.SourceLifecycle,
                    ["manifestDigest"] = recoveryPoint.ManifestDigest,
                    ["desiredRevisionId"] = recoveryPoint.DesiredRevisionId,
                    ["desiredRevisionHash"] = recoveryPoint.DesiredRevisionHash,
                    ["resolvedPlanReference"] = recoveryPoint.ResolvedPlanReference,
                    ["resolvedPlanDigest"] = recoveryPoint.ResolvedPlanDigest,
                    ["artifactCount"] = recoveryPoint.Artifacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["providerSnapshotReference"] = recoveryPoint.ProviderSnapshotReference,
                    ["providerSnapshotDigest"] = recoveryPoint.ProviderSnapshotDigest,
                    ["secretReferenceKeyCount"] = recoveryPoint.RequiredSecretReferenceKeys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["rpoAge"] = FormatDuration(rpoAge)
                }));
        }

        if (!failed)
        {
            target = await ExecuteAsync(
                DeploymentRecoveryStage.CreateIsolatedTarget,
                stages,
                () => provider.CreateIsolatedTargetAsync(recoveryPoint, cancellationToken),
                cancellationToken);
            failed = target is null;
            if (target is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.CreateIsolatedTarget), StableProviderMessage(DeploymentRecoveryStage.CreateIsolatedTarget));

            if (target is not null && !DeploymentRecoveryProofContract.IsSafeIdentity(target.InstanceId))
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.target.invalid",
                    "The isolated target returned an invalid logical identity.");
                failed = true;
            }
            else if (target is not null && string.Equals(target.InstanceId, recoveryPoint.SourceInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.target.identityReuse",
                    "The isolated target must have a different logical identity from the source.");
                failed = true;
            }
            else if (target is not null)
            {
                cleanupAllowed = true;
            }
        }

        DeploymentRecoveryRestoredState? restoredState = null;
        if (!failed)
        {
            restoredState = await ExecuteAsync(
                DeploymentRecoveryStage.RestoreRelationalState,
                stages,
                () => provider.RestoreRelationalStateAsync(recoveryPoint, target!, cancellationToken),
                cancellationToken);
            failed = restoredState is null;
            if (restoredState is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.RestoreRelationalState), StableProviderMessage(DeploymentRecoveryStage.RestoreRelationalState));
            if (restoredState is not null && !MatchesRecoveryPoint(recoveryPoint, target!, restoredState))
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.restore.metadataMismatch",
                    "Restored relational metadata did not match the sealed recovery point.");
                failed = true;
            }
        }

        DeploymentRecoverySecretRebind? secretRebind = null;
        if (!failed)
        {
            secretRebind = await ExecuteAsync(
                DeploymentRecoveryStage.RebindExternalSecrets,
                stages,
                () => provider.RebindExternalSecretsAsync(recoveryPoint, target!, cancellationToken),
                cancellationToken);
            failed = secretRebind is null;
            if (secretRebind is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.RebindExternalSecrets), StableProviderMessage(DeploymentRecoveryStage.RebindExternalSecrets));
            if (secretRebind is not null && !SecretSetsMatch(recoveryPoint.RequiredSecretReferenceKeys, secretRebind.ReferenceKeys))
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.secrets.mismatch",
                    "The rebound secret reference key set did not match the sealed recovery point.");
                failed = true;
            }
        }

        if (!failed)
        {
            var immutable = await ExecuteAsync(
                DeploymentRecoveryStage.ValidateImmutableInputs,
                stages,
                () => provider.ValidateImmutableInputsAsync(recoveryPoint, target!, restoredState!, secretRebind!, cancellationToken),
                cancellationToken);
            failed = immutable is null;
            if (immutable is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.ValidateImmutableInputs), StableProviderMessage(DeploymentRecoveryStage.ValidateImmutableInputs));
            if (immutable is not null && !immutable.Valid)
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.immutable.mismatch",
                    "The restored immutable inputs did not match the sealed recovery point.");
                failed = true;
            }
        }

        DeploymentRecoveryHealth? health = null;
        if (!failed)
        {
            health = await ExecuteAsync(
                DeploymentRecoveryStage.TargetHealth,
                stages,
                () => provider.ValidateTargetHealthAsync(recoveryPoint, target!, cancellationToken),
                cancellationToken);
            failed = health is null;
            if (health is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.TargetHealth), StableProviderMessage(DeploymentRecoveryStage.TargetHealth));
            if (health is not null && !health.Healthy)
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.health.unhealthy",
                    "The restored target did not pass its health gate.");
                failed = true;
            }
        }

        if (!failed)
        {
            var workflow = await ExecuteAsync(
                DeploymentRecoveryStage.WorkflowValidation,
                stages,
                () => provider.ValidateWorkflowAsync(recoveryPoint, target!, health!, cancellationToken),
                cancellationToken);
            failed = workflow is null;
            if (workflow is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.WorkflowValidation), StableProviderMessage(DeploymentRecoveryStage.WorkflowValidation));
            if (workflow is not null && !workflow.Succeeded)
            {
                stages[^1] = FailedStage(
                    stages[^1],
                    "recovery.workflow.failed",
                    "The restored target did not pass its workflow validation.");
                failed = true;
            }
        }

        if (!failed)
        {
            var current = _timeProvider.GetUtcNow();
            var elapsed = current - startedAt;
            if (elapsed > _maximumRto)
            {
                stages.Add(Failure(
                    DeploymentRecoveryStage.CutoverEligibility,
                    "recovery.rto.breached",
                    "The measured recovery time exceeded the proof objective."));
                failed = true;
            }
            else
            {
                var eligibility = await ExecuteAsync(
                    DeploymentRecoveryStage.CutoverEligibility,
                    stages,
                    () => provider.EvaluateCutoverEligibilityAsync(recoveryPoint, target!, cancellationToken),
                    cancellationToken);
                failed = eligibility is null;
                if (eligibility is null && stages[^1].Status != DeploymentRecoveryStageStatus.Failed)
                    stages[^1] = FailedStage(stages[^1], GenericCode(DeploymentRecoveryStage.CutoverEligibility), StableProviderMessage(DeploymentRecoveryStage.CutoverEligibility));
                if (eligibility is not null && !eligibility.Eligible)
                {
                    stages[^1] = FailedStage(
                        stages[^1],
                        "recovery.cutover.ineligible",
                        "The restored target was not eligible for a separately governed cutover.");
                    failed = true;
                }
                else if (eligibility is not null)
                {
                    eligibilityAt = _timeProvider.GetUtcNow();
                    if (eligibilityAt.Value - startedAt > _maximumRto)
                    {
                        stages[^1] = FailedStage(
                            stages[^1],
                            "recovery.rto.breached",
                            "The measured recovery time exceeded the proof objective.");
                        failed = true;
                    }
                    else
                    {
                        cutoverEligible = true;
                    }
                }
            }
        }

        AddSkippedStages(stages, cleanupAllowed);
        if (cleanupAllowed)
        {
            using var cleanupCancellation = cancellationToken.IsCancellationRequested
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupCancellation.CancelAfter(_cleanupTimeout);

            var cleanup = await ExecuteAsync(
                DeploymentRecoveryStage.Cleanup,
                stages,
                () => provider.CleanupAsync(target!, cleanupCancellation.Token),
                cleanupCancellation.Token);
            if (cleanup is null || !cleanup.Succeeded)
            {
                failed = true;
                if (cleanup is not null)
                    stages[^1] = FailedStage(
                        stages[^1],
                        "recovery.cleanup.failed",
                        "The provider did not confirm cleanup of the isolated target.");
            }
        }

        var rto = (eligibilityAt ?? _timeProvider.GetUtcNow()) - startedAt;
        if (rto < TimeSpan.Zero)
            rto = TimeSpan.Zero;

        return new DeploymentRecoveryProofReport(
            failed ? DeploymentRecoveryProofOutcome.Failed : DeploymentRecoveryProofOutcome.Passed,
            recoveryPoint,
            target,
            rpoAge,
            rto,
            !failed && cutoverEligible,
            stages.Select(SanitizeStage).ToArray());
    }

    private async Task<T?> ExecuteAsync<T>(
        DeploymentRecoveryStage stage,
        List<DeploymentRecoveryStageResult> stages,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var result = await operation();
            stages.Add(Passed(stage, startedAt, _timeProvider.GetUtcNow(), EvidenceFor(result)));
            return result;
        }
        catch (DeploymentRecoveryStageException exception) when (exception.Stage == stage)
        {
            stages.Add(Failure(
                stage,
                StableProviderCode(stage, exception.Code),
                StableProviderMessage(stage),
                startedAt));
            return default;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stages.Add(Failure(stage, CancelledCode(stage), CancelledMessage(stage), startedAt));
            return default;
        }
        catch (Exception)
        {
            stages.Add(Failure(stage, GenericCode(stage), StableProviderMessage(stage), startedAt));
            return default;
        }
    }

    private static IReadOnlyDictionary<string, string> EvidenceFor<T>(T result) =>
        result switch
        {
            DeploymentRecoveryTarget target => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["targetInstanceId"] = target.InstanceId
            },
            DeploymentRecoveryRestoredState restored => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["targetInstanceId"] = restored.TargetInstanceId,
                ["sourceInstanceId"] = restored.SourceInstanceId,
                ["recoveryPointId"] = restored.RecoveryPointId,
                ["desiredRevisionId"] = restored.DesiredRevisionId,
                ["desiredRevisionHash"] = restored.DesiredRevisionHash,
                ["resolvedPlanReference"] = restored.ResolvedPlanReference,
                ["resolvedPlanDigest"] = restored.ResolvedPlanDigest,
                ["artifactCount"] = restored.Artifacts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["providerSnapshotReference"] = restored.ProviderSnapshotReference,
                ["providerSnapshotDigest"] = restored.ProviderSnapshotDigest
            },
            DeploymentRecoverySecretRebind rebind => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["secretReferenceKeyCount"] = rebind.ReferenceKeys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            DeploymentRecoveryValidation validation => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["valid"] = validation.Valid.ToString().ToLowerInvariant()
            },
            DeploymentRecoveryHealth health => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["healthy"] = health.Healthy.ToString().ToLowerInvariant()
            },
            DeploymentRecoveryWorkflow workflow => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["succeeded"] = workflow.Succeeded.ToString().ToLowerInvariant()
            },
            DeploymentRecoveryCutoverEligibility eligibility => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["eligible"] = eligibility.Eligible.ToString().ToLowerInvariant()
            },
            DeploymentRecoveryCleanup cleanup => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["succeeded"] = cleanup.Succeeded.ToString().ToLowerInvariant()
            },
            _ => new Dictionary<string, string>(StringComparer.Ordinal)
        };

    private static bool ValidateRecoveryPoint(
        DeploymentRecoveryPoint point,
        DateTimeOffset now,
        TimeSpan maximumRpo,
        out string code)
    {
        if (!DeploymentRecoveryProofContract.IsSafeIdentity(point.SourceInstanceId) ||
            !DeploymentRecoveryProofContract.IsSafeIdentity(point.OrganizationId) ||
            !DeploymentRecoveryProofContract.IsSafeIdentity(point.WorkspaceId) ||
            !DeploymentRecoveryProofContract.IsSafeIdentity(point.RecoveryPointId) ||
            !DeploymentRecoveryProofContract.IsSafeIdentity(point.DesiredRevisionId) ||
            !DeploymentRecoveryProofContract.IsSafeIdentity(point.SourceLifecycle))
        {
            code = "recovery.point.invalid";
            return false;
        }

        if (point.CapturedAt > now || point.SourceQuiescedAt > point.RestorePointAt ||
            point.RestorePointAt > point.CapturedAt)
        {
            code = "recovery.point.future";
            return false;
        }

        if (now - point.RestorePointAt > maximumRpo)
        {
            code = "recovery.point.stale";
            return false;
        }

        if (!DeploymentRecoveryProofContract.IsStrictSha256Digest(point.ManifestDigest) ||
            !DeploymentRecoveryProofContract.IsStrictSha256Digest(point.DesiredRevisionHash) ||
            !DeploymentRecoveryProofContract.IsStrictSha256Digest(point.ResolvedPlanDigest) ||
            !DeploymentRecoveryProofContract.IsStrictSha256Digest(point.ProviderSnapshotDigest) ||
            !DeploymentRecoveryProofContract.IsSafeReference(point.ResolvedPlanReference) ||
            !DeploymentRecoveryProofContract.ReferenceMatchesDigest(point.ResolvedPlanReference, point.ResolvedPlanDigest) ||
            !DeploymentRecoveryProofContract.IsSafeReference(point.ProviderSnapshotReference) ||
            !DeploymentRecoveryProofContract.ReferenceMatchesDigest(point.ProviderSnapshotReference, point.ProviderSnapshotDigest))
        {
            code = "recovery.point.digestOrReferenceInvalid";
            return false;
        }

        if (point.Artifacts.Count == 0 || point.Artifacts.Any(artifact =>
                artifact is null ||
                !DeploymentRecoveryProofContract.IsSafeReference(artifact.Reference) ||
                !DeploymentRecoveryProofContract.IsStrictSha256Digest(artifact.Digest) ||
                !DeploymentRecoveryProofContract.ReferenceMatchesDigest(artifact.Reference, artifact.Digest)))
        {
            code = "recovery.point.artifactInvalid";
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (point.RequiredSecretReferenceKeys.Count == 0 || point.RequiredSecretReferenceKeys.Any(key =>
                !DeploymentRecoveryProofContract.IsSafeSecretReferenceKey(key) || !keys.Add(key)))
        {
            code = "recovery.point.secretKeysInvalid";
            return false;
        }

        code = string.Empty;
        return true;
    }

    private static bool MatchesRecoveryPoint(
        DeploymentRecoveryPoint point,
        DeploymentRecoveryTarget target,
        DeploymentRecoveryRestoredState restored)
    {
        return string.Equals(restored.TargetInstanceId, target.InstanceId, StringComparison.Ordinal)
            && string.Equals(restored.SourceInstanceId, point.SourceInstanceId, StringComparison.Ordinal)
            && string.Equals(restored.RecoveryPointId, point.RecoveryPointId, StringComparison.Ordinal)
            && string.Equals(restored.DesiredRevisionId, point.DesiredRevisionId, StringComparison.Ordinal)
            && string.Equals(restored.DesiredRevisionHash, point.DesiredRevisionHash, StringComparison.Ordinal)
            && string.Equals(restored.ResolvedPlanReference, point.ResolvedPlanReference, StringComparison.Ordinal)
            && string.Equals(restored.ResolvedPlanDigest, point.ResolvedPlanDigest, StringComparison.Ordinal)
            && restored.Artifacts.SequenceEqual(point.Artifacts)
            && string.Equals(restored.ProviderSnapshotReference, point.ProviderSnapshotReference, StringComparison.Ordinal)
            && string.Equals(restored.ProviderSnapshotDigest, point.ProviderSnapshotDigest, StringComparison.Ordinal);
    }

    private static bool SecretSetsMatch(IReadOnlyList<string> required, IReadOnlyList<string> rebound) =>
        required.Count == rebound.Count &&
        rebound.Count == rebound.Distinct(StringComparer.Ordinal).Count() &&
        required.ToHashSet(StringComparer.Ordinal).SetEquals(rebound);

    private static void AddSkippedStages(List<DeploymentRecoveryStageResult> stages, bool cleanupAllowed)
    {
        var existing = stages.Select(stage => stage.Stage).ToHashSet();
        foreach (var stage in Enum.GetValues<DeploymentRecoveryStage>())
        {
            if (existing.Contains(stage))
                continue;
            if (cleanupAllowed && stage == DeploymentRecoveryStage.Cleanup)
                continue;

            stages.Add(new DeploymentRecoveryStageResult(
                stage,
                DeploymentRecoveryStageStatus.Skipped,
                "recovery.stage.skipped",
                "Stage was skipped after an earlier recovery gate failed.",
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
    }

    private static DeploymentRecoveryStageResult Passed(
        DeploymentRecoveryStage stage,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyDictionary<string, string> evidence) =>
        new(
            stage,
            DeploymentRecoveryStageStatus.Passed,
            $"recovery.{StageCode(stage)}.passed",
            "Stage completed.",
            startedAt,
            completedAt,
            DeploymentRecoveryProofEvidence.SanitizeStageEvidence(evidence));

    private DeploymentRecoveryStageResult Failure(
        DeploymentRecoveryStage stage,
        string code,
        string message,
        DateTimeOffset? startedAt = null) =>
        new(
            stage,
            DeploymentRecoveryStageStatus.Failed,
            code,
            message,
            startedAt ?? _timeProvider.GetUtcNow(),
            _timeProvider.GetUtcNow(),
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static DeploymentRecoveryStageResult FailedStage(
        DeploymentRecoveryStageResult stage,
        string code,
        string message) =>
        stage with
        {
            Status = DeploymentRecoveryStageStatus.Failed,
            Code = code,
            Message = message,
            Evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        };

    private static DeploymentRecoveryStageResult SanitizeStage(DeploymentRecoveryStageResult stage) =>
        stage with
        {
            Code = DeploymentProofEvidence.SanitizeMessage(stage.Code),
            Message = DeploymentProofEvidence.SanitizeMessage(stage.Message),
            Evidence = DeploymentRecoveryProofEvidence.SanitizeStageEvidence(stage.Evidence)
        };

    private static string StageCode(DeploymentRecoveryStage stage) => stage switch
    {
        DeploymentRecoveryStage.RecoveryPointValidation => "point",
        DeploymentRecoveryStage.CreateIsolatedTarget => "target",
        DeploymentRecoveryStage.RestoreRelationalState => "restore",
        DeploymentRecoveryStage.RebindExternalSecrets => "secrets",
        DeploymentRecoveryStage.ValidateImmutableInputs => "immutable",
        DeploymentRecoveryStage.TargetHealth => "health",
        DeploymentRecoveryStage.WorkflowValidation => "workflow",
        DeploymentRecoveryStage.CutoverEligibility => "cutover",
        DeploymentRecoveryStage.Cleanup => "cleanup",
        _ => "stage"
    };

    private static string GenericCode(DeploymentRecoveryStage stage) => $"recovery.{StageCode(stage)}.failed";

    private static string CancelledCode(DeploymentRecoveryStage stage) => $"recovery.{StageCode(stage)}.cancelled";

    private static string CancelledMessage(DeploymentRecoveryStage stage) => "The recovery operation was cancelled.";

    private static string StableProviderCode(DeploymentRecoveryStage stage, string suppliedCode) =>
        IsStableCode(suppliedCode, stage) ? suppliedCode : GenericCode(stage);

    private static string StableProviderMessage(DeploymentRecoveryStage stage) =>
        $"The recovery {StageCode(stage)} gate failed.";

    private static string MessageFor(string code) => code switch
    {
        "recovery.point.stale" => "The recovery point is older than the proof RPO objective.",
        "recovery.point.future" => "The recovery point timestamp is not valid.",
        "recovery.point.digestOrReferenceInvalid" => "The recovery point contains an invalid immutable digest or reference.",
        "recovery.point.artifactInvalid" => "The recovery point contains an invalid artifact identity.",
        "recovery.point.secretKeysInvalid" => "The recovery point contains invalid secret reference keys.",
        _ => "The recovery point is invalid."
    };

    private static bool IsStableCode(string? code, DeploymentRecoveryStage stage) =>
        code is not null && code.Length <= 96 &&
        code.StartsWith($"recovery.{StageCode(stage)}.", StringComparison.Ordinal) &&
        StableCodeRegex().IsMatch(code);

    private static string FormatDuration(TimeSpan value) =>
        (value < TimeSpan.Zero ? TimeSpan.Zero : value).ToString("c", System.Globalization.CultureInfo.InvariantCulture);

    private static TimeSpan ValidatePositive(TimeSpan value, string parameterName) =>
        value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan
            ? throw new ArgumentOutOfRangeException(parameterName, "The duration must be positive and finite.")
            : value;

    [GeneratedRegex("^recovery\\.[a-z]+\\.[a-z]+$")]
    private static partial Regex StableCodeRegex();
}

/// <summary>Shared safety predicates for provider-neutral recovery identities.</summary>
public static class DeploymentRecoveryProofContract
{
    public static bool IsStrictSha256Digest(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value[7..].All(IsLowerHex);

    public static bool IsSafeIdentity(string? value) =>
        value is { Length: > 0 and <= 256 } &&
        !value.Any(char.IsControl) &&
        !value.Any(char.IsWhiteSpace) &&
        value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    public static bool IsSafeReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('%') ||
            value.Contains('\\') || value.Contains('?') || value.Contains('#') ||
            value.Contains("/../", StringComparison.Ordinal) || value.EndsWith("/..", StringComparison.Ordinal) ||
            value.Contains("/./", StringComparison.Ordinal) || value.EndsWith("/.", StringComparison.Ordinal))
            return false;

        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
                return false;

            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            return !path.Split('/').Any(segment => segment is "." or "..");
        }

        var at = value.IndexOf('@');
        var slash = value.IndexOf('/');
        if (at >= 0 && (slash < 0 || at < slash))
            return false;

        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':' or '/' or '@');
    }

    public static bool IsSafeSecretReferenceKey(string? value) =>
        value is { Length: > 0 and <= 256 } &&
        !value.Any(char.IsControl) &&
        !value.Any(char.IsWhiteSpace) &&
        !value.Contains("=", StringComparison.Ordinal) &&
        !value.Contains("?", StringComparison.Ordinal) &&
        !value.Contains("#", StringComparison.Ordinal) &&
        !value.Contains("://", StringComparison.Ordinal) &&
        !value.Contains('\\') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':');

    public static bool ReferenceMatchesDigest(string? reference, string? digest)
    {
        if (!IsStrictSha256Digest(digest) || string.IsNullOrWhiteSpace(reference))
            return false;

        var marker = reference.LastIndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return true;

        var embedded = reference[(marker + 1)..];
        return IsStrictSha256Digest(embedded) && string.Equals(embedded, digest, StringComparison.Ordinal);
    }

    private static bool IsLowerHex(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f';
}
