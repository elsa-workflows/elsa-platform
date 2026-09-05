using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Core.Instances;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed partial class ProductionAzureLifecycleProofTests
{
    [Theory]
    [InlineData(AzureProviderOperationStatus.Accepted, false)]
    [InlineData(AzureProviderOperationStatus.Queued, false)]
    [InlineData(AzureProviderOperationStatus.Running, false)]
    [InlineData(AzureProviderOperationStatus.Succeeded, false)]
    [InlineData(AzureProviderOperationStatus.Failed, true)]
    [InlineData(AzureProviderOperationStatus.Cancelled, true)]
    [InlineData(AzureProviderOperationStatus.RecoveryRequired, true)]
    [InlineData((AzureProviderOperationStatus)999, true)]
    public void Accepted_lifecycle_handoff_uses_actual_provider_status(
        AzureProviderOperationStatus status, bool failed)
    {
        var (assignment, operation, lifecycleId) = ProviderObservationFixture();
        Assert.Equal(failed, HasFailedProviderObservation(
            assignment, operation with { Status = status }, lifecycleId,
            ElsaInstanceOperationAction.Create, AzureProviderOperationAction.Reconcile));
        // False means keep polling, never a successful proof. Readiness/health and cleanup
        // success are still established by the production loop's positive evidence gates.
    }

    [Fact]
    public void Prior_assignment_checkpoint_is_ignored_until_current_provider_operation_arrives()
    {
        var (assignment, operation, lifecycleId) = ProviderObservationFixture();
        Assert.False(HasFailedProviderObservation(
            assignment, operation with
            {
                IdempotencyKey = $"elsa-instance-operation:{Guid.NewGuid():D}",
                Status = AzureProviderOperationStatus.Failed
            }, lifecycleId, ElsaInstanceOperationAction.Create, AzureProviderOperationAction.Reconcile));
        Assert.False(HasFailedProviderObservation(
            assignment, null, lifecycleId, ElsaInstanceOperationAction.Create, AzureProviderOperationAction.Reconcile));
    }

    [Fact]
    public void Provider_binding_mismatches_fail_closed_using_actual_records()
    {
        var (assignment, operation, lifecycleId) = ProviderObservationFixture();
        AzureProviderOperation[] mismatches =
        [
            operation with { Id = Guid.NewGuid() },
            operation with { WorkspaceId = Guid.NewGuid() },
            operation with { OrganizationId = Guid.NewGuid() },
            operation with { InstanceId = Guid.NewGuid() },
            operation with { ProviderAssignmentId = Guid.NewGuid() },
            operation with { TargetKey = "another-instance" },
            operation with { ProviderScopeFingerprint = new string('f', 64) },
            operation with { Action = AzureProviderOperationAction.Delete },
            operation with { LifecycleAction = ElsaInstanceOperationAction.Delete }
        ];
        foreach (var mismatch in mismatches)
            Assert.True(HasFailedProviderObservation(
                assignment, mismatch, lifecycleId,
                ElsaInstanceOperationAction.Create, AzureProviderOperationAction.Reconcile));
    }

    [Theory]
    [InlineData(AzureProviderOperationStatus.Accepted, false)]
    [InlineData(AzureProviderOperationStatus.Queued, false)]
    [InlineData(AzureProviderOperationStatus.Running, false)]
    [InlineData(AzureProviderOperationStatus.Succeeded, true)]
    [InlineData(AzureProviderOperationStatus.RecoveryRequired, true)]
    public void Recovery_required_delete_fails_fast_after_correlated_provider_completion(
        AzureProviderOperationStatus status, bool failed)
    {
        var (assignment, operation, lifecycleId) = ProviderObservationFixture();
        var deletion = operation with
        {
            Action = AzureProviderOperationAction.Delete,
            LifecycleAction = ElsaInstanceOperationAction.Delete,
            IdempotencyKey = operation.IdempotencyKey + ":delete",
            Status = status
        };
        Assert.Equal(failed, HasFailedProviderObservation(
            assignment, deletion, lifecycleId,
            ElsaInstanceOperationAction.Delete, AzureProviderOperationAction.Delete));
    }

    private static async Task<bool> HasCorrelatedProviderFailureAsync(
        IServiceProvider services,
        ProofState state,
        ElsaInstance instance,
        ElsaInstanceOperationSummary lifecycleOperation,
        AzureProviderOperationAction expectedProviderAction,
        CancellationToken cancellationToken)
    {
        // Create/reconcile currently use RecoveryRequired during accepted hand-off.
        // Delete uses durable deferral instead; a recovery-required delete cannot finish
        // automatically even when its provider eventually confirms cleanup.
        var waitingDelete = lifecycleOperation.State == ElsaInstanceOperationState.WaitingForPriorOperation &&
            expectedProviderAction == AzureProviderOperationAction.Delete;
        if (lifecycleOperation.State != ElsaInstanceOperationState.RecoveryRequired && !waitingDelete)
            return false;
        var reference = instance.PlacementAssignmentReference?.AssignmentId;
        if (string.IsNullOrWhiteSpace(reference))
            return false;
        if (!Guid.TryParseExact(reference, "D", out var assignmentId))
            return true;
        var assignment = await services.GetRequiredService<IAzureProviderResourceAssignmentStore>()
            .GetAsync(state.WorkspaceId, assignmentId, cancellationToken);
        if (assignment is null || assignment.Id != assignmentId ||
            assignment.WorkspaceId != state.WorkspaceId || assignment.OrganizationId != state.OrganizationId ||
            assignment.InstanceId != state.InstanceId)
            return true;
        if (assignment.LastOperationId is not { } providerId)
            return false;
        var operation = await services.GetRequiredService<IAzureProviderOperationStore>()
            .GetAsync(state.WorkspaceId, providerId, cancellationToken);
        if (waitingDelete)
        {
            // Only the predecessor submitted by this isolated proof can establish that its
            // successor cannot advance without explicit operator recovery.
            var priorId = state.ReconcileOperationId ?? state.CreateOperationId;
            if (priorId is null)
                return false;
            var prior = await services.GetRequiredService<IManagedElsaInstanceApiStore>()
                .GetOperationAsync(state.WorkspaceId, state.InstanceId, priorId.Value, cancellationToken);
            return IsBlockedByPriorProviderRecovery(assignment, operation, prior, priorId.Value);
        }
        return HasFailedProviderObservation(assignment, operation, lifecycleOperation.Id,
            lifecycleOperation.Action, expectedProviderAction);
    }

    private static bool IsBlockedByPriorProviderRecovery(
        AzureProviderResourceAssignment assignment,
        AzureProviderOperation? operation,
        ElsaInstanceOperationSummary? prior,
        Guid expectedPriorId) =>
        operation?.Status == AzureProviderOperationStatus.RecoveryRequired &&
        prior is { State: ElsaInstanceOperationState.RecoveryRequired } && prior.Id == expectedPriorId &&
        prior.InstanceId == assignment.InstanceId &&
        prior.Action is ElsaInstanceOperationAction.Create or ElsaInstanceOperationAction.Reconcile &&
        operation.Action == AzureProviderOperationAction.Reconcile && operation.LifecycleAction == prior.Action &&
        string.Equals(operation.IdempotencyKey, $"elsa-instance-operation:{prior.Id:D}", StringComparison.Ordinal) &&
        IsProviderBoundToAssignment(assignment, operation);

    [Theory]
    [InlineData(AzureProviderOperationStatus.Accepted, false)]
    [InlineData(AzureProviderOperationStatus.Running, false)]
    [InlineData(AzureProviderOperationStatus.Succeeded, false)]
    [InlineData(AzureProviderOperationStatus.Failed, false)]
    [InlineData(AzureProviderOperationStatus.Cancelled, false)]
    [InlineData(AzureProviderOperationStatus.RecoveryRequired, true)]
    public void Waiting_delete_only_fails_fast_for_exact_recovery_blocked_predecessor(
        AzureProviderOperationStatus status, bool blocked)
    {
        var (assignment, operation, lifecycleId) = ProviderObservationFixture();
        var prior = new ElsaInstanceOperationSummary(lifecycleId, assignment.InstanceId,
            ElsaInstanceOperationAction.Create, ElsaInstanceOperationState.RecoveryRequired,
            1, 1, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null);
        operation = operation with { Status = status };
        Assert.Equal(blocked, IsBlockedByPriorProviderRecovery(assignment, operation, prior, lifecycleId));
        Assert.False(IsBlockedByPriorProviderRecovery(assignment, operation, prior with { Id = Guid.NewGuid() }, lifecycleId));
        Assert.False(IsBlockedByPriorProviderRecovery(assignment, operation, prior with { State = ElsaInstanceOperationState.Failed }, lifecycleId));
        Assert.False(IsBlockedByPriorProviderRecovery(assignment, operation with { InstanceId = Guid.NewGuid() }, prior, lifecycleId));
        Assert.False(IsBlockedByPriorProviderRecovery(assignment, operation with { IdempotencyKey = "prior" }, prior, lifecycleId));
    }

    private static bool HasFailedProviderObservation(
        AzureProviderResourceAssignment assignment,
        AzureProviderOperation? operation,
        Guid lifecycleOperationId,
        ElsaInstanceOperationAction lifecycleAction,
        AzureProviderOperationAction expectedProviderAction)
    {
        if (operation is null)
            return false;
        if (!IsProviderBoundToAssignment(assignment, operation))
            return true;
        // An assignment may still point to the previous operation during hand-off. It cannot
        // establish either success or failure for the current lifecycle operation.
        var correlated = expectedProviderAction == AzureProviderOperationAction.Delete
            ? AzureProviderOperationValidation.IsLifecycleDeleteIdempotencyKey(operation.IdempotencyKey, lifecycleOperationId)
            : string.Equals(operation.IdempotencyKey,
                $"elsa-instance-operation:{lifecycleOperationId:D}", StringComparison.Ordinal);
        if (!correlated)
            return false;
        if (operation.Action != expectedProviderAction || operation.LifecycleAction != lifecycleAction)
            return true;
        return expectedProviderAction == AzureProviderOperationAction.Delete &&
               operation.Status == AzureProviderOperationStatus.Succeeded ||
               operation.Status is not (AzureProviderOperationStatus.Accepted or AzureProviderOperationStatus.Queued or
                   AzureProviderOperationStatus.Running or AzureProviderOperationStatus.Succeeded);
    }

    private static bool IsProviderBoundToAssignment(
        AzureProviderResourceAssignment assignment, AzureProviderOperation operation) =>
        operation.Id == assignment.LastOperationId &&
        operation.WorkspaceId == assignment.WorkspaceId && operation.OrganizationId == assignment.OrganizationId &&
        operation.InstanceId == assignment.InstanceId && operation.ProviderAssignmentId == assignment.Id &&
        string.Equals(operation.TargetKey, assignment.WorkloadName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(operation.ProviderScopeFingerprint, assignment.ProviderScopeFingerprint, StringComparison.Ordinal);

    private static (AzureProviderResourceAssignment Assignment, AzureProviderOperation Operation, Guid LifecycleId)
        ProviderObservationFixture()
    {
        var workspaceId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var lifecycleId = Guid.NewGuid();
        var scope = new string('a', 64);
        var operation = CreateProviderOperation(workspaceId, organizationId, instanceId,
            assignmentId, lifecycleId, "instance", scope, ElsaInstanceOperationAction.Create);
        var now = DateTimeOffset.UtcNow;
        var assignment = new AzureProviderResourceAssignment(assignmentId, workspaceId, organizationId,
            instanceId, scope, 1, Guid.NewGuid().ToString("D"), "owned-group", "instance", "owner",
            "westeurope", AzureProviderAssignmentState.Provisioning, new("owned-group"), operation.Id, 1, now, now);
        return (assignment, operation, lifecycleId);
    }

    private static AzureProviderOperation CreateProviderOperation(
        Guid workspaceId,
        Guid organizationId,
        Guid instanceId,
        Guid assignmentId,
        Guid lifecycleOperationId,
        string target,
        string providerScope,
        ElsaInstanceOperationAction lifecycleAction) =>
        new(
            Guid.NewGuid(),
            workspaceId,
            target,
            AzureProviderOperationAction.Reconcile,
            $"elsa-instance-operation:{lifecycleOperationId:D}",
            new('a', 64),
            new('b', 64),
            new('c', 64),
            new('d', 64),
            "3.8.0",
            "3.8",
            "combined",
            "Dedicated",
            "westeurope",
            "valenceruntimeimages.azurecr.io/runtime-combined",
            "sha256:" + new string('e', 64),
            null,
            null,
            AzureProviderOperationStatus.Running,
            AzureProviderOperationPhase.Planned,
            0,
            1,
            1,
            new(),
            null,
            AzureProviderHealth.Unknown,
            [],
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            ProviderScopeFingerprint: providerScope,
            OrganizationId: organizationId,
            InstanceId: instanceId,
            LifecycleAction: lifecycleAction,
            ProviderAssignmentId: assignmentId);
}
