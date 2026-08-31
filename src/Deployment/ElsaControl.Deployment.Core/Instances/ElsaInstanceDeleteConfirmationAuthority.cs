using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Workspace;

namespace ElsaControl.Deployment.Core.Instances;

/// <summary>
/// Atomic authority used by lifecycle stores to validate and consume a delete
/// confirmation. Implementations must make a successful consumption one-time.
/// </summary>
public interface IElsaInstanceDeleteConfirmationAuthority
{
    bool TryConsume(
        ElsaInstance instance,
        ElsaInstanceDeleteConfirmationRequirement requirement,
        DateTimeOffset consumedAt);
}

/// <summary>
/// In-memory confirmation authority for deterministic tests and local composition.
/// It validates the same workspace, target, account, action and expiry constraints
/// as the relational confirmation update and consumes a confirmation atomically.
/// </summary>
public sealed class InMemoryElsaInstanceDeleteConfirmationAuthority : IElsaInstanceDeleteConfirmationAuthority
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ActionConfirmation> _confirmations = [];

    public InMemoryElsaInstanceDeleteConfirmationAuthority(
        IEnumerable<ActionConfirmation>? confirmations = null)
    {
        if (confirmations is null)
            return;

        foreach (var confirmation in confirmations)
            Add(confirmation);
    }

    public IReadOnlyCollection<ActionConfirmation> Confirmations
    {
        get
        {
            lock (_gate)
                return _confirmations.Values.ToArray();
        }
    }

    public void Add(ActionConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        if (confirmation.Id == Guid.Empty)
            throw new ArgumentException("Confirmation ID is required.", nameof(confirmation));

        lock (_gate)
            _confirmations[confirmation.Id] = confirmation;
    }

    public ActionConfirmation? Get(Guid confirmationId)
    {
        lock (_gate)
            return _confirmations.GetValueOrDefault(confirmationId);
    }

    public bool TryConsume(
        ElsaInstance instance,
        ElsaInstanceDeleteConfirmationRequirement requirement,
        DateTimeOffset consumedAt)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(requirement);

        if (requirement.ConfirmationId == Guid.Empty || requirement.AccountId == Guid.Empty)
            return false;

        var consumedAtUtc = consumedAt.ToUniversalTime();
        lock (_gate)
        {
            if (!_confirmations.TryGetValue(requirement.ConfirmationId, out var confirmation) ||
                confirmation.WorkspaceId != instance.WorkspaceId ||
                confirmation.ActionType != ConfirmationActionType.DeleteManagedInstance ||
                !string.Equals(confirmation.TargetId, instance.Id.ToString("D"), StringComparison.Ordinal) ||
                confirmation.ConfirmedByAccountId != requirement.AccountId ||
                confirmation.UsedAt is not null ||
                confirmation.ExpiresAt <= consumedAtUtc)
                return false;

            _confirmations[confirmation.Id] = confirmation with { UsedAt = consumedAtUtc };
            return true;
        }
    }
}
