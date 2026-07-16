namespace Elsa.Platform.Healing.Core.Ownership;

public sealed record ProviderConnectionValidationResult(
    bool Succeeded,
    string ReasonCode,
    string? RepositoryProviderId = null)
{
    public static ProviderConnectionValidationResult Valid(string repositoryProviderId) =>
        new(true, HealingOwnershipReasonCodes.Succeeded, repositoryProviderId);

    public static ProviderConnectionValidationResult Invalid(string reasonCode) =>
        new(false, reasonCode);
}

/// <summary>
/// Provider-specific proof that the configured credential resolves, belongs to the installation,
/// and grants access to the named repository. Implementations return the provider's immutable
/// repository identity; owner-entered repository metadata is never sufficient authority.
/// </summary>
public interface IProviderConnectionValidator
{
    ValueTask<ProviderConnectionValidationResult> ValidateAsync(
        ProviderConnection connection,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves a workspace-scoped protected credential for a provider adapter. Implementations must
/// fail closed for missing, archived, external, or undecryptable references and must never log or
/// persist the resolved value outside the configured secret store.
/// </summary>
public interface IHealingProviderCredentialResolver
{
    ValueTask<string?> ResolveAsync(
        Guid workspaceId,
        string credentialReference,
        CancellationToken cancellationToken = default);
}

public sealed class FailClosedProviderConnectionValidator : IProviderConnectionValidator
{
    public ValueTask<ProviderConnectionValidationResult> ValidateAsync(
        ProviderConnection connection,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderValidationUnavailable));
}
