using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using System.Text.Json;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Safe target identities needed to reserve an existing deployment environment.
/// It contains no provider resource IDs, credentials, or command payload.
/// </summary>
public sealed record ElsaInstanceLifecycleDeploymentTarget(
    Guid ApplicationId,
    Guid EnvironmentId,
    Guid EngineId,
    Guid SourceRevisionId,
    Guid ConfirmationId,
    Guid ActorAccountId)
{
    public void Validate()
    {
        if (ApplicationId == Guid.Empty || EnvironmentId == Guid.Empty || EngineId == Guid.Empty ||
            SourceRevisionId == Guid.Empty || ConfirmationId == Guid.Empty || ActorAccountId == Guid.Empty)
            throw new InvalidOperationException("Lifecycle deployment target identity is invalid.");
    }
}

/// <summary>
/// Safe resolution inputs retained by a worker-facing store. The lifecycle outbox
/// remains ID-only; an adapter reconstructs this typed request from governed data.
/// </summary>
public sealed record ElsaInstanceLifecycleResolutionInput(
    ElsaInstancePlanResolutionRequest PlanRequest,
    ElsaInstanceLifecycleDeploymentTarget DeploymentTarget)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(PlanRequest);
        ArgumentNullException.ThrowIfNull(PlanRequest.InstanceIntent);
        ArgumentNullException.ThrowIfNull(PlanRequest.BuilderIntent);
        ArgumentNullException.ThrowIfNull(PlanRequest.ReleaseManifest);
        DeploymentTarget.Validate();
    }
}

public sealed record ElsaInstanceLifecycleWorkItem(
    ElsaInstanceLifecycleOutboxMessage Outbox,
    ElsaInstanceOperation Operation,
    ElsaInstance Instance,
    ElsaInstanceLifecycleResolutionInput Resolution)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Outbox);
        ArgumentNullException.ThrowIfNull(Operation);
        ArgumentNullException.ThrowIfNull(Instance);
        ArgumentNullException.ThrowIfNull(Resolution);
        if (Outbox.Id == Guid.Empty || Outbox.WorkspaceId == Guid.Empty || Outbox.InstanceId == Guid.Empty ||
            Outbox.OperationId == Guid.Empty || Operation.Id == Guid.Empty || Instance.Id == Guid.Empty ||
            Outbox.OperationId != Operation.Id || Outbox.InstanceId != Operation.InstanceId ||
            Outbox.InstanceId != Instance.Id || Outbox.WorkspaceId != Instance.WorkspaceId ||
            Outbox.Action != Operation.Action ||
            !string.Equals(Outbox.RequestHash, Operation.RequestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Persisted lifecycle work item identity is invalid.");
        if (Operation.State != ElsaInstanceOperationState.Accepted)
            throw new InvalidOperationException("Persisted lifecycle work item is not claimed.");
        if (Resolution.PlanRequest.WorkspaceId is { } requestWorkspace && requestWorkspace != Instance.WorkspaceId)
            throw new InvalidOperationException("Lifecycle resolution workspace is invalid.");
        if (!string.Equals(Resolution.PlanRequest.InstanceIntent.ComputeCanonicalHash(), Instance.ComputeCanonicalIntentHash(), StringComparison.Ordinal))
            throw new InvalidOperationException("Lifecycle resolution intent does not match the instance.");
        Resolution.Validate();
    }
}

public sealed record ElsaInstanceLifecycleResolvedPlan(
    ElsaResolvedPlanReference Reference,
    string SerializedPlan)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Reference);
        if (string.IsNullOrWhiteSpace(SerializedPlan))
            throw new InvalidOperationException("Resolved lifecycle plan is empty.");
        try
        {
            if (!string.Equals(
                    ResolvedElsaApplicationPlanSerialization.ComputeContentHash(
                        ResolvedElsaApplicationPlanSerialization.Deserialize(SerializedPlan)),
                    Reference.ContentHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Resolved lifecycle plan identity is invalid.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException("Resolved lifecycle plan identity is invalid.");
        }
    }
}

public sealed record ElsaInstanceLifecycleResolutionCommit(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    Guid OutboxId,
    string RequestHash,
    string WorkerId,
    ElsaInstanceOperation Operation,
    ElsaInstance Instance,
    ElsaInstanceLifecycleResolvedPlan Plan,
    ElsaInstanceLifecycleDeploymentTarget DeploymentTarget,
    DateTimeOffset CommittedAt)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty || OutboxId == Guid.Empty)
            throw new InvalidOperationException("Lifecycle resolution commit identity is invalid.");
        if (string.IsNullOrWhiteSpace(WorkerId) || string.IsNullOrWhiteSpace(RequestHash) ||
            RequestHash.Length != 64 || RequestHash.Any(x => !Uri.IsHexDigit(x)))
            throw new InvalidOperationException("Lifecycle resolution commit envelope is invalid.");
        ArgumentNullException.ThrowIfNull(Operation);
        ArgumentNullException.ThrowIfNull(Instance);
        ArgumentNullException.ThrowIfNull(Plan);
        Plan.Validate();
        DeploymentTarget.Validate();
        if (Operation.State != ElsaInstanceOperationState.Queued || Operation.Id != OperationId ||
            Operation.InstanceId != InstanceId || Instance.Id != InstanceId || Instance.WorkspaceId != WorkspaceId ||
            !string.Equals(Operation.RequestHash, RequestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Lifecycle resolution commit state is invalid.");
    }
}

public sealed record ElsaInstanceLifecycleResolutionFailure(
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    Guid OutboxId,
    string RequestHash,
    string WorkerId,
    string Code,
    string Summary,
    DateTimeOffset FailedAt)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || InstanceId == Guid.Empty || OperationId == Guid.Empty || OutboxId == Guid.Empty ||
            string.IsNullOrWhiteSpace(WorkerId) || string.IsNullOrWhiteSpace(RequestHash) ||
            RequestHash.Length != 64 || RequestHash.Any(x => !Uri.IsHexDigit(x)) ||
            string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Summary) ||
            Code.Length > 128 || Code.Any(char.IsControl) || Summary.Length > 2000 || Summary.Any(char.IsControl))
            throw new InvalidOperationException("Lifecycle resolution failure envelope is invalid.");
    }
}

public enum ElsaInstanceLifecycleWorkerOutcome
{
    Queued,
    Failed,
    AlreadyCompleted,
    WaitingForPriorOperation,
    Conflict
}

public sealed record ElsaInstanceLifecycleWorkerResult(
    ElsaInstanceLifecycleWorkerOutcome Outcome,
    ElsaInstanceOperation Operation,
    ElsaInstance Instance,
    WorkspaceDeploymentRun? Run = null,
    string? FailureCode = null,
    string? FailureSummary = null);

public sealed record ElsaInstanceLifecycleWorkerBatchResult(
    IReadOnlyList<ElsaInstanceLifecycleWorkerResult> Results,
    int ProviderInvocations);

public sealed record ElsaInstanceLifecycleDeploymentRun(
    WorkspaceDeploymentRun Run,
    ElsaInstanceOperation Operation,
    Guid InstanceId);

public sealed record ElsaInstanceLifecycleRecordedFailure(
    Guid OperationId,
    string Code,
    string Summary,
    DateTimeOffset RecordedAt);
