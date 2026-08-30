using ElsaControl.Deployment.Abstractions.Instances;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Provider-neutral acceptance service for managed Elsa lifecycle intent. This
/// service only commits customer intent, operation identity and durable work; a
/// worker resolves the plan and invokes a provider after that transaction commits.
/// </summary>
public sealed class ElsaInstanceLifecycleService(
    IElsaInstanceLifecycleStore store,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElsaInstanceLifecycleAcceptance> CreateAsync(
        ElsaInstanceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateWorkspace(request.WorkspaceId);
        ValidateOrganization(request.OrganizationId);
        ArgumentNullException.ThrowIfNull(request.Intent);
        ValidateRequired(request.Name, nameof(request.Name), "Instance name is required.");
        ValidateRequired(request.Slug, nameof(request.Slug), "Instance slug is required.");
        var key = RequireKey(request.IdempotencyKey);
        var requestHash = ComputeRequestHash(
            ElsaInstanceOperationAction.Create,
            expectedVersion: 1,
            request.Intent.ComputeCanonicalHash(),
            request.OrganizationId.ToString("D"),
            request.WorkspaceId.ToString("D"),
            request.Name,
            request.Slug,
            request.InstanceId?.ToString("D") ?? "generated");

        var existingOperation = await store.FindOperationByKeyAsync(request.WorkspaceId, key, cancellationToken);
        if (existingOperation is not null)
        {
            if (existingOperation.Action != ElsaInstanceOperationAction.Create)
                throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.");
            if (!string.Equals(existingOperation.RequestHash, requestHash, StringComparison.Ordinal))
                throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.");
            var existing = await store.GetInstanceAsync(request.WorkspaceId, existingOperation.InstanceId, cancellationToken)
                ?? throw new ElsaInstanceLifecycleConflictException("Lifecycle operation outbox record is orphaned.");
            if (existing.OrganizationId != request.OrganizationId)
                throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.");
            var replayTransition = ElsaInstanceStateMachine.Request(
                existing,
                ElsaInstanceOperationAction.Create,
                existingOperation,
                existing.Version,
                key,
                requestHash);
            return await CommitAsync(existing, replayTransition, cancellationToken);
        }

        var instanceId = request.InstanceId ?? Guid.NewGuid();
        if (instanceId == Guid.Empty)
            throw new ArgumentException("Instance ID cannot be empty.", nameof(request.InstanceId));
        var instance = new ElsaInstance(
            instanceId,
            request.OrganizationId,
            request.WorkspaceId,
            request.Name,
            request.Slug,
            request.Intent);
        var transition = ElsaInstanceStateMachine.Request(
            instance,
            ElsaInstanceOperationAction.Create,
            idempotencyKey: key,
            requestHash: requestHash,
            expectedVersion: instance.Version);
        return await CommitAsync(null, transition, cancellationToken);
    }

    public Task<ElsaInstanceLifecycleAcceptance> UpdateIntentAsync(
        ElsaInstanceIntentUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.UpdateIntent,
            request.ExpectedVersion, request.IdempotencyKey, request.Intent, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> UpdateAsync(
        ElsaInstanceIntentUpdateRequest request,
        CancellationToken cancellationToken = default) => UpdateIntentAsync(request, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> StartAsync(
        ElsaInstanceLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.Start,
            request.ExpectedVersion, request.IdempotencyKey, null, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> StopAsync(
        ElsaInstanceLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.Stop,
            request.ExpectedVersion, request.IdempotencyKey, null, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> RestartAsync(
        ElsaInstanceLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.Restart,
            request.ExpectedVersion, request.IdempotencyKey, null, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> ReconcileAsync(
        ElsaInstanceLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.Reconcile,
            request.ExpectedVersion, request.IdempotencyKey, null, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> RecoverAsync(
        ElsaInstanceLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.Recover,
            request.ExpectedVersion, request.IdempotencyKey, null, cancellationToken);

    public Task<ElsaInstanceLifecycleAcceptance> DeleteAsync(
        ElsaInstanceLifecycleRequest request,
        CancellationToken cancellationToken = default) =>
        AcceptAsync(request.WorkspaceId, request.InstanceId, ElsaInstanceOperationAction.Delete,
            request.ExpectedVersion, request.IdempotencyKey, null, cancellationToken);

    private async Task<ElsaInstanceLifecycleAcceptance> AcceptAsync(
        Guid workspaceId,
        Guid instanceId,
        ElsaInstanceOperationAction action,
        int expectedVersion,
        string idempotencyKey,
        ElsaInstanceIntent? requestedIntent,
        CancellationToken cancellationToken)
    {
        ValidateWorkspace(workspaceId);
        if (instanceId == Guid.Empty)
            throw new ArgumentException("Instance ID is required.", nameof(instanceId));
        if (expectedVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion), "Expected version must be positive.");
        var key = RequireKey(idempotencyKey);
        var instance = await store.GetInstanceAsync(workspaceId, instanceId, cancellationToken)
            ?? throw new KeyNotFoundException("Elsa instance does not exist in the workspace.");
        var existingOperation = await store.FindOperationByKeyAsync(workspaceId, key, cancellationToken);
        if (existingOperation is not null)
        {
            if (existingOperation.InstanceId != instanceId || existingOperation.Action != action)
                throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.");

            var requestHash = existingOperation.RequestHash;
            if (requestedIntent is not null &&
                !string.Equals(
                    requestHash,
                    ComputeRequestHash(action, existingOperation.ExpectedVersion, requestedIntent.ComputeCanonicalHash()),
                    StringComparison.Ordinal))
                throw new ElsaInstanceLifecycleConflictException("Idempotency key was already used for a different request.");

            // Supplying the existing operation to the state machine makes an exact
            // replay independent of the caller's current If-Match value, including
            // replays after the operation has reached a terminal state.
            var replayTransition = ElsaInstanceStateMachine.Request(
                instance,
                action,
                existingOperation,
                existingOperation.ExpectedVersion,
                key,
                requestHash,
                requestedIntent);
            return await CommitAsync(instance, replayTransition, cancellationToken);
        }

        var requestHash = ComputeRequestHash(
            action,
            expectedVersion,
            requestedIntent?.ComputeCanonicalHash() ?? instance.ComputeCanonicalIntentHash());
        var activeOperation = await store.GetActiveOperationAsync(workspaceId, instanceId, cancellationToken);
        var transition = ElsaInstanceStateMachine.Request(
            instance,
            action,
            activeOperation,
            expectedVersion,
            key,
            requestHash,
            requestedIntent);
        return await CommitAsync(instance, transition, cancellationToken);
    }

    private Task<ElsaInstanceLifecycleAcceptance> CommitAsync(
        ElsaInstance? expectedInstance,
        ElsaInstanceTransitionResult transition,
        CancellationToken cancellationToken)
    {
        var outbox = new ElsaInstanceLifecycleOutboxMessage(
            Guid.NewGuid(),
            transition.Instance.WorkspaceId,
            transition.Instance.Id,
            transition.Operation.Id,
            transition.Operation.Action,
            transition.Operation.RequestHash,
            _timeProvider.GetUtcNow());
        return store.CommitAcceptedAsync(expectedInstance, transition.Instance, transition.Operation, outbox, cancellationToken);
    }

    private static string RequireKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Idempotency key is required.", nameof(value));
        return value.Trim();
    }

    private static void ValidateRequired(string? value, string parameterName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(message, parameterName);
    }

    private static string ComputeRequestHash(
        ElsaInstanceOperationAction action,
        int expectedVersion,
        params string[] values)
    {
        var canonical = new StringBuilder()
            .Append(action).Append('\n')
            .Append(expectedVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var value in values)
            canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void ValidateWorkspace(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(workspaceId));
    }

    private static void ValidateOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization ID is required.", nameof(organizationId));
    }
}
