using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

public sealed class ElsaInstanceProviderReconciliationService(
    IElsaInstanceProviderReconciliationStore store,
    IElsaInstanceProviderReconciliationPort provider,
    TimeProvider? timeProvider = null)
{
    public const string ConvergedCode = "provider.reconciliation.converged";
    public const string UnknownCode = "provider.reconciliation.unknown";
    public const string AmbiguousCode = "provider.reconciliation.ambiguous";
    public const string InProgressCode = "provider.reconciliation.in-progress";
    public const string HealthFailedCode = "provider.reconciliation.health-failed";
    public const string HealthUnknownCode = "provider.reconciliation.health-unknown";
    public const string FailedCode = "provider.reconciliation.failed";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElsaInstanceProviderReconciliationResult> ReconcileAsync(
        Guid workspaceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID is required.", nameof(operationId));

        var replay = await store.GetResultAsync(workspaceId, operationId, cancellationToken);
        if (replay is not null)
            return replay with { Replayed = true };

        var target = await store.GetTargetAsync(workspaceId, operationId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider reconciliation target does not exist.");
        target.Validate();

        var instance = target.Instance;
        var operation = target.Operation;
        var observation = await provider.ObserveAsync(new(
            workspaceId,
            instance.Id,
            operation.Id,
            operation.AttemptNumber,
            instance.DesiredLifecycle,
            instance.ResolvedPlanReference,
            instance.CurrentDeploymentReference), cancellationToken);
        ArgumentNullException.ThrowIfNull(observation);

        var projection = Project(instance, operation, observation, _timeProvider.GetUtcNow());
        return await store.CommitAsync(new(
            workspaceId,
            instance.Id,
            operation.Id,
            instance.Version,
            operation.AttemptNumber,
            target.ReconciliationVersion,
            observation.ComputeFingerprint(),
            projection.Instance,
            projection.Operation,
            projection.Code,
            projection.Operation.State == ElsaInstanceOperationState.RecoveryRequired && observation.RetryEvidence is not null,
            projection.At), cancellationToken);
    }

    private static (ElsaInstance Instance, ElsaInstanceOperation Operation, string Code, DateTimeOffset At) Project(
        ElsaInstance instance,
        ElsaInstanceOperation operation,
        ElsaInstanceProviderObservation observation,
        DateTimeOffset now)
    {
        if (observation.Kind != ElsaInstanceProviderObservationKind.Confirmed)
            return (Project(instance, ElsaObservedLifecycle.Unknown, ElsaInstanceHealth.Unknown), operation,
                observation.Kind == ElsaInstanceProviderObservationKind.Unknown ? UnknownCode : AmbiguousCode, now);

        if (observation.ObservedLifecycle == ElsaObservedLifecycle.Ready)
        {
            if (observation.HealthGate == ElsaInstanceProviderHealthGate.Passed &&
                instance.DesiredLifecycle == ElsaDesiredLifecycle.Running)
                return (Project(instance, ElsaObservedLifecycle.Ready, ElsaInstanceHealth.Healthy),
                    operation.TransitionTo(ElsaInstanceOperationState.Succeeded), ConvergedCode, now);

            var health = observation.HealthGate == ElsaInstanceProviderHealthGate.Failed
                ? ElsaInstanceHealth.Degraded
                : ElsaInstanceHealth.Unknown;
            var code = observation.HealthGate == ElsaInstanceProviderHealthGate.Failed
                ? HealthFailedCode
                : HealthUnknownCode;
            return (Project(instance, ElsaObservedLifecycle.Degraded, health),
                operation.TransitionTo(ElsaInstanceOperationState.Failed), code, now);
        }

        if (observation.ObservedLifecycle == ElsaObservedLifecycle.Stopped &&
            instance.DesiredLifecycle == ElsaDesiredLifecycle.Stopped)
            return (Project(instance, ElsaObservedLifecycle.Stopped, ElsaInstanceHealth.Unknown),
                operation.TransitionTo(ElsaInstanceOperationState.Succeeded), ConvergedCode, now);

        if (observation.ObservedLifecycle == ElsaObservedLifecycle.Deleted &&
            instance.DesiredLifecycle == ElsaDesiredLifecycle.Deleting)
            return (Project(instance, ElsaObservedLifecycle.Deleted, ElsaInstanceHealth.Unknown, now),
                operation.TransitionTo(ElsaInstanceOperationState.Succeeded), ConvergedCode, now);

        if (observation.ObservedLifecycle == ElsaObservedLifecycle.Failed)
            return (Project(instance, ElsaObservedLifecycle.Failed, ElsaInstanceHealth.Unreachable),
                operation.TransitionTo(ElsaInstanceOperationState.Failed), FailedCode, now);

        return (Project(instance, ElsaObservedLifecycle.Unknown, ElsaInstanceHealth.Unknown),
            operation, InProgressCode, now);
    }

    private static ElsaInstance Project(
        ElsaInstance instance,
        ElsaObservedLifecycle observed,
        ElsaInstanceHealth health,
        DateTimeOffset? deletedAt = null) =>
        ElsaInstance.Hydrate(
            instance.Id,
            instance.OrganizationId,
            instance.WorkspaceId,
            instance.Name,
            instance.Slug,
            instance.Intent,
            observed,
            health,
            instance.Version,
            instance.IdentityBinding,
            instance.DesiredStateRevisionId,
            instance.ResolvedPlanReference,
            instance.CurrentResolvedRelease,
            instance.CurrentDeploymentReference,
            instance.PlacementAssignmentReference,
            instance.ElsaTenantReference,
            instance.LastOperationId,
            deletedAt);
}
