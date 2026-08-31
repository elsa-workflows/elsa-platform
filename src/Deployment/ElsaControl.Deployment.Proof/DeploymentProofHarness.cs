namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Runs the highest useful deployment seam in a disposable context. It deliberately owns no
/// provider credentials or resource lifecycle policy beyond offering Cleanup after planning.
/// </summary>
public sealed class DeploymentProofHarness(
    TimeProvider? timeProvider = null,
    TimeSpan? cleanupTimeout = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _cleanupTimeout = ValidateCleanupTimeout(cleanupTimeout ?? TimeSpan.FromMinutes(2));

    public async Task<DeploymentProofReport> RunAsync(
        DeploymentProofInput input,
        DeploymentProofEnvironment environment,
        IDeploymentProofProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(provider);

        var stages = new List<DeploymentProofStageResult>();
        DeploymentProofSelection? selection = null;
        DeploymentProofPlan? plan = null;
        DeploymentProofDeployment? deployment = null;
        DeploymentProofHealth? health = null;
        var failed = false;

        if (!TryValidateInput(input, environment, out var validationCode, out var validationMessage))
        {
            stages.Add(Failure(DeploymentProofStage.Selection, validationCode, validationMessage));
            failed = true;
        }
        else
        {
            selection = await ExecuteAsync(
                DeploymentProofStage.Selection,
                stages,
                () => provider.SelectAsync(input, environment, cancellationToken),
                cancellationToken);
            failed = selection is null;
            if (selection is not null && !SelectionMatches(input, selection))
            {
                stages[^1] = stages[^1] with
                {
                    Status = DeploymentProofStageStatus.Failed,
                    Code = "proof.selection.mismatch",
                    Message = "The provider selected a version, topology, feature set, image reference, or digest different from the requested proof input."
                };
                failed = true;
            }
        }

        if (!failed)
        {
            plan = await ExecuteAsync(
                DeploymentProofStage.Plan,
                stages,
                () => provider.PlanAsync(selection!, environment, cancellationToken),
                cancellationToken);
            failed = plan is null;
        }

        if (!failed)
        {
            deployment = await ExecuteAsync(
                DeploymentProofStage.Provision,
                stages,
                () => provider.ProvisionAsync(plan!, environment, cancellationToken),
                cancellationToken);
            failed = deployment is null;
        }

        if (!failed)
        {
            health = await ExecuteAsync(
                DeploymentProofStage.Health,
                stages,
                () => provider.WaitForHealthAsync(deployment!, environment, cancellationToken),
                cancellationToken);
            failed = health is null || !health.Healthy;
            if (health is not null && !health.Healthy)
                stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.health.unhealthy", Message = "The provider returned an unhealthy endpoint." };
            else if (health is not null && !string.Equals(health.Endpoint, deployment!.Endpoint, StringComparison.Ordinal))
            {
                stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.health.endpointMismatch", Message = "The health result endpoint did not match the provisioned endpoint." };
                failed = true;
            }
        }

        if (!failed)
        {
            var workflow = await ExecuteAsync(
                DeploymentProofStage.Workflow,
                stages,
                () => provider.RunWorkflowAsync(health!, environment, cancellationToken),
                cancellationToken);
            failed = workflow is null || !workflow.Succeeded;
            if (workflow is not null && !workflow.Succeeded)
                stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.workflow.failed", Message = "The provider reported that the basic workflow did not succeed." };
        }

        if (!failed)
        {
            // Provisioning already creates or reconciles the planned target. Re-apply that
            // same plan once and require the provider to make the idempotent no-op explicit.
            var repeatApply = await ExecuteAsync(
                DeploymentProofStage.RepeatApply,
                stages,
                () => provider.ApplyAsync(plan!, environment, cancellationToken),
                cancellationToken);
            if (repeatApply is null)
                failed = true;
            else if (!string.Equals(repeatApply.PlanId, plan!.PlanId, StringComparison.Ordinal))
            {
                stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.repeatApply.planMismatch", Message = "The repeated apply result referenced a different plan." };
                failed = true;
            }
            else if (!repeatApply.NoOp || repeatApply.Applied)
            {
                stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.repeatApply.notIdempotent", Message = "The repeated apply did not report an idempotent no-op." };
                failed = true;
            }
        }

        // A plan identifies the disposable target even when provisioning returned an error
        // after creating some resources. Always give the provider a cleanup opportunity once
        // planning completed, passing the deployment result when one is available.
        AddSkippedStages(stages, plan is null ? DeploymentProofStage.Cleanup : null);
        if (plan is not null)
        {
            using var cleanupCancellation = cancellationToken.IsCancellationRequested
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupCancellation.CancelAfter(_cleanupTimeout);
            var cleanup = await ExecuteAsync(
                DeploymentProofStage.Cleanup,
                stages,
                () => provider.CleanupAsync(plan, deployment, environment, cleanupCancellation.Token),
                cleanupCancellation.Token);
            if (cleanup is null || !cleanup.Succeeded)
            {
                failed = true;
                if (cleanup is not null)
                    stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.cleanup.failed", Message = "The provider did not confirm cleanup." };
            }
            else if (deployment is not null && !string.Equals(cleanup.ResourceId, deployment.ResourceId, StringComparison.Ordinal))
            {
                failed = true;
                stages[^1] = stages[^1] with { Status = DeploymentProofStageStatus.Failed, Code = "proof.cleanup.resourceMismatch", Message = "The cleanup result referenced a different resource." };
            }
        }

        return new DeploymentProofReport(
            failed ? DeploymentProofOutcome.Failed : DeploymentProofOutcome.Passed,
            input,
            environment,
            stages.Select(stage => stage with
            {
                Message = DeploymentProofEvidence.SanitizeMessage(stage.Message),
                Evidence = DeploymentProofEvidence.Sanitize(stage.Evidence)
            }).ToArray());
    }

    private async Task<T?> ExecuteAsync<T>(
        DeploymentProofStage stage,
        List<DeploymentProofStageResult> stages,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var result = await operation();
            var completedAt = _timeProvider.GetUtcNow();
            stages.Add(Passed(stage, result, startedAt, completedAt));
            return result;
        }
        catch (DeploymentProofStageException exception) when (exception.Stage == stage)
        {
            stages.Add(new DeploymentProofStageResult(
                stage,
                DeploymentProofStageStatus.Failed,
                exception.Code,
                DeploymentProofEvidence.SanitizeMessage(exception.Message),
                startedAt,
                _timeProvider.GetUtcNow(),
                new Dictionary<string, string>(StringComparer.Ordinal)));
            return default;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stages.Add(new DeploymentProofStageResult(
                stage,
                DeploymentProofStageStatus.Failed,
                $"proof.{StageCode(stage)}.cancelled",
                "The provider operation was cancelled.",
                startedAt,
                _timeProvider.GetUtcNow(),
                new Dictionary<string, string>(StringComparer.Ordinal)));
            return default;
        }
        catch (Exception)
        {
            stages.Add(new DeploymentProofStageResult(
                stage,
                DeploymentProofStageStatus.Failed,
                $"proof.{StageCode(stage)}.unexpected",
                "The provider operation failed unexpectedly.",
                startedAt,
                _timeProvider.GetUtcNow(),
                new Dictionary<string, string>(StringComparer.Ordinal)));
            return default;
        }
    }

    private static TimeSpan ValidateCleanupTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Cleanup timeout must be positive.");
        return timeout;
    }

    private static string StageCode(DeploymentProofStage stage) => stage switch
    {
        DeploymentProofStage.RepeatApply => "repeatApply",
        _ => stage.ToString().ToLowerInvariant()
    };

    private static DeploymentProofStageResult Passed<T>(DeploymentProofStage stage, T result, DateTimeOffset startedAt, DateTimeOffset completedAt)
    {
        var evidence = result switch
        {
            DeploymentProofSelection value => new Dictionary<string, string>
            {
                ["selectionId"] = value.SelectionId,
                ["elsaVersion"] = value.ElsaVersion,
                ["topology"] = value.Topology,
                ["features"] = string.Join(",", value.Features),
                ["imageReference"] = value.ImageReference,
                ["imageDigest"] = value.ImageDigest
            },
            DeploymentProofPlan value => new Dictionary<string, string>(value.SafeMetadata, StringComparer.Ordinal)
            {
                ["planId"] = value.PlanId,
                ["selectionId"] = value.SelectionId,
                ["fingerprint"] = value.Fingerprint
            },
            DeploymentProofDeployment value => new Dictionary<string, string>(value.SafeMetadata, StringComparer.Ordinal)
            {
                ["resourceId"] = value.ResourceId,
                ["endpoint"] = value.Endpoint,
                ["planId"] = value.PlanId
            },
            DeploymentProofHealth value => new Dictionary<string, string>(value.SafeMetadata, StringComparer.Ordinal)
            {
                ["healthy"] = value.Healthy.ToString().ToLowerInvariant(),
                ["endpoint"] = value.Endpoint,
                ["status"] = value.Status
            },
            DeploymentProofWorkflow value => new Dictionary<string, string>(value.SafeMetadata, StringComparer.Ordinal)
            {
                ["workflowId"] = value.WorkflowId,
                ["succeeded"] = value.Succeeded.ToString().ToLowerInvariant(),
                ["result"] = value.Result
            },
            DeploymentProofApply value => new Dictionary<string, string>(value.SafeMetadata, StringComparer.Ordinal)
            {
                ["planId"] = value.PlanId,
                ["applied"] = value.Applied.ToString().ToLowerInvariant(),
                ["noOp"] = value.NoOp.ToString().ToLowerInvariant()
            },
            DeploymentProofCleanup value => CleanupMetadata(value),
            _ => new Dictionary<string, string>(StringComparer.Ordinal)
        };

        return new DeploymentProofStageResult(
            stage,
            DeploymentProofStageStatus.Passed,
            $"proof.{stage.ToString().ToLowerInvariant()}.passed",
            "Stage completed.",
            startedAt,
            completedAt,
            DeploymentProofEvidence.Sanitize(evidence));
    }

    private static Dictionary<string, string> CleanupMetadata(DeploymentProofCleanup value)
    {
        var metadata = new Dictionary<string, string>(value.SafeMetadata, StringComparer.OrdinalIgnoreCase)
        {
            ["succeeded"] = value.Succeeded.ToString().ToLowerInvariant()
        };
        metadata.Remove("resourceId");
        if (!string.IsNullOrWhiteSpace(value.ResourceId))
            metadata["resourceId"] = value.ResourceId;
        return metadata;
    }

    private DeploymentProofStageResult Failure(DeploymentProofStage stage, string code, string message) =>
        new(stage, DeploymentProofStageStatus.Failed, code, DeploymentProofEvidence.SanitizeMessage(message), _timeProvider.GetUtcNow(), _timeProvider.GetUtcNow(), new Dictionary<string, string>(StringComparer.Ordinal));

    private static void AddSkippedStages(List<DeploymentProofStageResult> stages, DeploymentProofStage? cleanupStage)
    {
        var existing = stages.Select(stage => stage.Stage).ToHashSet();
        foreach (var stage in Enum.GetValues<DeploymentProofStage>())
        {
            if (existing.Contains(stage))
                continue;
            if (stage == DeploymentProofStage.Cleanup && cleanupStage is null)
                continue;

            stages.Add(new DeploymentProofStageResult(
                stage,
                DeploymentProofStageStatus.Skipped,
                "proof.stage.skipped",
                cleanupStage == stage ? "Cleanup was not attempted because provisioning did not produce a disposable resource." : "Stage was skipped after an earlier failure.",
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
    }

    private static bool TryValidateInput(DeploymentProofInput input, DeploymentProofEnvironment environment, out string code, out string message)
    {
        if (string.IsNullOrWhiteSpace(input.ElsaVersion))
            return Invalid("proof.selection.versionRequired", "An exact Elsa version is required.", out code, out message);
        if (string.IsNullOrWhiteSpace(input.Topology))
            return Invalid("proof.selection.topologyRequired", "An exact deployment topology is required.", out code, out message);
        if (input.Features is null)
            return Invalid("proof.selection.featuresRequired", "The selected feature set must be explicit.", out code, out message);
        if (input.Features.Any(string.IsNullOrWhiteSpace))
            return Invalid("proof.selection.featureInvalid", "Selected feature names cannot be blank.", out code, out message);
        if (input.Features
            .Where(feature => feature is not null)
            .GroupBy(feature => feature.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            return Invalid("proof.selection.featureDuplicate", "Selected feature names must be unique.", out code, out message);
        if (string.IsNullOrWhiteSpace(input.ImageReference))
            return Invalid("proof.selection.imageReferenceRequired", "An immutable image reference is required.", out code, out message);
        if (HasCredentialBearingImageReference(input.ImageReference))
            return Invalid("proof.selection.imageReferenceUnsafe", "Image references must not include embedded credentials.", out code, out message);
        if (!IsSha256Digest(input.ImageDigest))
            return Invalid("proof.selection.imageDigestRequired", "An immutable sha256 image digest is required.", out code, out message);
        if (string.IsNullOrWhiteSpace(environment.Name) || string.IsNullOrWhiteSpace(environment.Region) || string.IsNullOrWhiteSpace(environment.Provider))
            return Invalid("proof.selection.environmentRequired", "Environment name, region, and provider are required.", out code, out message);
        if (environment.SecretReferenceNames.Any(reference => reference.Contains('=') || reference.Contains(':')))
            return Invalid("proof.selection.secretReferenceOnly", "Secret inputs must be reference names, never values or locators with embedded credentials.", out code, out message);

        code = string.Empty;
        message = string.Empty;
        return true;
    }

    private static bool SelectionMatches(DeploymentProofInput input, DeploymentProofSelection selection) =>
        string.Equals(input.ElsaVersion, selection.ElsaVersion, StringComparison.Ordinal)
        && string.Equals(input.Topology, selection.Topology, StringComparison.Ordinal)
        && input.Features.SequenceEqual(selection.Features, StringComparer.Ordinal)
        && string.Equals(input.ImageReference, selection.ImageReference, StringComparison.Ordinal)
        && string.Equals(input.ImageDigest, selection.ImageDigest, StringComparison.Ordinal);

    private static bool HasCredentialBearingImageReference(string imageReference)
    {
        var value = imageReference.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            return true;

        var schemeDelimiter = value.IndexOf("://", StringComparison.Ordinal);
        var authorityStart = value.StartsWith("//", StringComparison.Ordinal)
            ? 2
            : schemeDelimiter >= 0 ? schemeDelimiter + "://".Length : 0;
        var authorityEnd = value.IndexOfAny(['/', '\\', ' ', '\t', '\r', '\n', ',', ';'], authorityStart);
        var authority = authorityEnd < 0 ? value[authorityStart..] : value[authorityStart..authorityEnd];
        return authority.Contains('@');
    }

    private static bool IsSha256Digest(string digest) =>
        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
        && digest.Length == "sha256:".Length + 64
        && digest[7..].All(character => Uri.IsHexDigit(character));

    private static bool Invalid(string invalidCode, string invalidMessage, out string code, out string message)
    {
        code = invalidCode;
        message = invalidMessage;
        return false;
    }

}
