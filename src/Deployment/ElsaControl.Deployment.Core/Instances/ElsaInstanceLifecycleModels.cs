using ElsaControl.Deployment.Abstractions.Instances;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Input for creating a managed Elsa instance. The optional ID lets callers choose
/// an opaque identity before the first request; when omitted the service allocates
/// one and reuses it when the idempotency key is replayed.
/// </summary>
public sealed record ElsaInstanceCreateRequest(
    Guid OrganizationId,
    Guid WorkspaceId,
    string Name,
    string Slug,
    ElsaInstanceIntent Intent,
    string IdempotencyKey,
    Guid? InstanceId = null,
    Guid? ActorAccountId = null);

/// <summary>
/// Input shared by lifecycle actions. HTTP adapters should map a strong If-Match
/// ETag to <see cref="ExpectedVersion"/> before calling this provider-neutral port.
/// </summary>
public sealed record ElsaInstanceLifecycleRequest(
    Guid WorkspaceId,
    Guid InstanceId,
    int ExpectedVersion,
    string IdempotencyKey,
    string? Reason = null,
    Guid? DeleteConfirmationId = null,
    Guid? ActorAccountId = null)
{
    public int IfMatchVersion => ExpectedVersion;
}

/// <summary>Input for an immutable intent revision and optional instance metadata update.</summary>
public sealed record ElsaInstanceIntentUpdateRequest(
    Guid WorkspaceId,
    Guid InstanceId,
    ElsaInstanceIntent? Intent,
    int ExpectedVersion,
    string IdempotencyKey,
    string? Name = null,
    string? Reason = null,
    Guid? ActorAccountId = null)
{
    public int IfMatchVersion => ExpectedVersion;
}

/// <summary>
/// Safe durable work notification. The worker reloads the aggregate and resolves it
/// after the acceptance transaction; intent and provider payloads are not copied to
/// the outbox.
/// </summary>
public sealed record ElsaInstanceLifecycleOutboxMessage(
    Guid Id,
    Guid WorkspaceId,
    Guid InstanceId,
    Guid OperationId,
    ElsaInstanceOperationAction Action,
    string RequestHash,
    DateTimeOffset CreatedAt);

/// <summary>Result returned after intent, operation and outbox are accepted.</summary>
public sealed record ElsaInstanceLifecycleAcceptance(
    ElsaInstance Instance,
    ElsaInstanceOperation Operation,
    ElsaInstanceLifecycleOutboxMessage Outbox,
    bool Replayed);

public sealed record ElsaInstanceDeleteConfirmationRequirement(Guid ConfirmationId, Guid AccountId);

public sealed record ElsaInstanceAcceptanceContext(
    Guid? ActorAccountId,
    string? Reason,
    ElsaInstanceDeleteConfirmationRequirement? DeleteConfirmation = null);

public sealed class ElsaInstanceDeleteConfirmationException : InvalidOperationException
{
    public ElsaInstanceDeleteConfirmationException() : base("Delete confirmation is invalid or unavailable.") { }
}

/// <summary>
/// Indicates that a request cannot be accepted because an idempotency or optimistic
/// concurrency invariant was violated. The message is deliberately stable and safe
/// for an API boundary.
/// </summary>
public sealed class ElsaInstanceLifecycleConflictException : InvalidOperationException
{
    public ElsaInstanceLifecycleConflictException(string message)
        : base(message)
    {
    }
}
