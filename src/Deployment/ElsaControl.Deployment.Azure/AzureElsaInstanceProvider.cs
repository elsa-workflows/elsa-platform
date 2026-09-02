using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Bridges the provider-neutral managed-instance lifecycle to the durable Azure
/// operation store. Only immutable plan identities and safe observed facts cross
/// this boundary; Azure resource IDs stay in the provider store.
/// </summary>
public sealed class AzureElsaInstanceProvider(
    IAzureProviderOperationService operationService,
    IAzureProviderOperationStore operationStore,
    AzureElsaInstanceProviderOptions? options = null) :
    IElsaInstanceProviderSubmissionPort,
    IElsaInstanceProviderReconciliationPort
{
    private readonly AzureElsaInstanceProviderOptions _options = options ?? new();

    public async Task<ElsaInstanceProviderSubmissionResult> SubmitAsync(
        ElsaInstanceProviderSubmission request,
        CancellationToken cancellationToken = default)
    {
        AzureProviderOperationSubmission submission;
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Validate();
            EnsureEnabled();

            var location = request.Location?.Trim();
            if (string.IsNullOrWhiteSpace(location))
                throw new InvalidOperationException("Managed instance provider placement is unavailable.");

            var target = new AzureWorkloadTarget(WorkloadName(request.InstanceId), location);
            var translation = AzureWorkloadPlanTranslator.Translate(request.Plan, target);
            if (!translation.IsAccepted)
                throw new InvalidOperationException("The resolved plan is outside the governed Azure provider profile.");

            submission = new(
                IdempotencyKey(request.OperationId),
                _options.TemplateFingerprint,
                translation.Plan!,
                _options.ProviderScopeFingerprint);
        }
        catch (ElsaInstanceProviderSubmissionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ElsaInstanceProviderSubmissionException(
                ElsaInstanceProviderSubmissionFailureKind.Rejected,
                exception);
        }

        try
        {
            var result = operationService is IAzureProviderOperationReplayService replayService
                ? await replayService.SubmitWithReplayAsync(request.WorkspaceId, submission, cancellationToken)
                : new AzureProviderOperationSubmissionResult(
                    await operationService.SubmitAsync(request.WorkspaceId, submission, cancellationToken),
                    Replayed: false);
            return new(result.Operation.OperationIdentity, result.Replayed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ElsaInstanceProviderSubmissionException(
                ElsaInstanceProviderSubmissionFailureKind.OutcomeUnknown,
                exception);
        }
    }

    public async Task<ElsaInstanceProviderObservation> ObserveAsync(
        ElsaInstanceProviderReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId == Guid.Empty || request.InstanceId == Guid.Empty || request.OperationId == Guid.Empty ||
            request.AttemptNumber < 1)
            throw new ArgumentException("Provider reconciliation request identity is invalid.", nameof(request));
        EnsureEnabled();

        var operation = await operationStore.GetLatestReconcileAsync(
            request.WorkspaceId,
            WorkloadName(request.InstanceId),
            _options.ProviderScopeFingerprint,
            cancellationToken);
        if (operation is null)
            return Unknown(request);

        // The store lookup is intentionally broad enough to recover the latest
        // operation after a restart, but the returned row is still untrusted at
        // this boundary. Bind it to the exact lifecycle operation before exposing
        // any provider state; otherwise a stale or cross-scope operation could be
        // projected as the current instance's health.
        if (operation.WorkspaceId != request.WorkspaceId ||
            !string.Equals(operation.TargetKey, WorkloadName(request.InstanceId), StringComparison.OrdinalIgnoreCase) ||
            operation.Action != AzureProviderOperationAction.Reconcile ||
            !string.Equals(operation.IdempotencyKey, IdempotencyKey(request.OperationId), StringComparison.Ordinal) ||
            !string.Equals(operation.ProviderScopeFingerprint, NormalizeScope(_options.ProviderScopeFingerprint), StringComparison.Ordinal))
            return CorrelationMismatch(request);

        var correlation = operation.OperationIdentity;
        if (operation.Status == AzureProviderOperationStatus.Succeeded &&
            operation.Health is AzureProviderHealth.Healthy or AzureProviderHealth.Degraded &&
            !ElsaManagedEndpointOrigin.TryCreate(operation.Endpoint, out _))
            return EndpointInvalid(request);

        return operation.Status switch
        {
            AzureProviderOperationStatus.Succeeded when operation.Health == AzureProviderHealth.Healthy =>
                new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Ready,
                    ElsaInstanceProviderHealthGate.Passed, request.OperationId, request.AttemptNumber,
                    correlation, retryEvidence: null, currentDeploymentReference: CurrentDeployment(operation)),
            AzureProviderOperationStatus.Succeeded when operation.Health == AzureProviderHealth.Degraded =>
                new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Ready,
                    ElsaInstanceProviderHealthGate.Failed, request.OperationId, request.AttemptNumber,
                    correlation, retryEvidence: null, currentDeploymentReference: CurrentDeployment(operation)),
            AzureProviderOperationStatus.Succeeded when operation.Health == AzureProviderHealth.Unreachable =>
                new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Ready,
                    ElsaInstanceProviderHealthGate.Unknown, request.OperationId, request.AttemptNumber,
                    correlation),
            AzureProviderOperationStatus.Succeeded when operation.Health == AzureProviderHealth.Failed =>
                new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Failed,
                    ElsaInstanceProviderHealthGate.Failed, request.OperationId, request.AttemptNumber,
                    correlation),
            AzureProviderOperationStatus.Failed =>
                new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Failed,
                    ElsaInstanceProviderHealthGate.Failed, request.OperationId, request.AttemptNumber,
                    correlation),
            AzureProviderOperationStatus.Cancelled =>
                new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Failed,
                    ElsaInstanceProviderHealthGate.Failed, request.OperationId, request.AttemptNumber,
                    correlation),
            _ => new(ElsaInstanceProviderObservationKind.Confirmed, ElsaObservedLifecycle.Provisioning,
                ElsaInstanceProviderHealthGate.Unknown, request.OperationId, request.AttemptNumber, correlation)
        };
    }

    private void EnsureEnabled()
    {
        try
        {
            _options.Validate();
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Managed instance provider authority is invalid.");
        }
    }

    private static ElsaInstanceProviderObservation Unknown(ElsaInstanceProviderReconciliationRequest request) =>
        new(ElsaInstanceProviderObservationKind.Unknown, ElsaObservedLifecycle.Unknown,
            ElsaInstanceProviderHealthGate.Unknown, request.OperationId, request.AttemptNumber,
            "provider-operation-unavailable");

    private static ElsaInstanceProviderObservation CorrelationMismatch(ElsaInstanceProviderReconciliationRequest request) =>
        new(ElsaInstanceProviderObservationKind.Ambiguous, ElsaObservedLifecycle.Unknown,
            ElsaInstanceProviderHealthGate.Unknown, request.OperationId, request.AttemptNumber,
            "provider-operation-correlation-mismatch");

    private static ElsaInstanceProviderObservation EndpointInvalid(ElsaInstanceProviderReconciliationRequest request) =>
        new(ElsaInstanceProviderObservationKind.Ambiguous, ElsaObservedLifecycle.Unknown,
            ElsaInstanceProviderHealthGate.Unknown, request.OperationId, request.AttemptNumber,
            "provider-operation-endpoint-invalid");

    private static ElsaCurrentDeploymentReference? CurrentDeployment(AzureProviderOperation operation) =>
        new(operation.OperationIdentity, $"attempt-{operation.AttemptNumber}", operation.Endpoint);

    internal static string WorkloadName(Guid instanceId) => $"e{instanceId:N}"[..16];

    internal static string IdempotencyKey(Guid operationId) => $"elsa-instance-operation:{operationId:D}";

    private static string? NormalizeScope(string? value) => value?.Trim().ToLowerInvariant();
}
