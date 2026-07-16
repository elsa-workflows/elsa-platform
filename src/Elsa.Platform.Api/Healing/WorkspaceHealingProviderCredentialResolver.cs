using System.Security.Cryptography;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Core.Ownership;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Platform.Api.Healing;

internal sealed class WorkspaceHealingProviderCredentialResolver(
    WorkspaceDeploymentService deployments,
    IDataProtectionProvider dataProtectionProvider) : IHealingProviderCredentialResolver
{
    private const string CredentialPrefix = "credential://";

    public async ValueTask<string?> ResolveAsync(
        Guid workspaceId,
        string credentialReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialReference) ||
            !credentialReference.StartsWith(CredentialPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(credentialReference[CredentialPrefix.Length..], out var credentialReferenceId))
            return null;

        var secret = await deployments.GetCredentialSecretAsync(
            workspaceId, credentialReferenceId, cancellationToken);
        if (secret is null ||
            secret.SecretStoreStatus != DeploymentSecretStoreStatus.Active ||
            secret.CredentialReferenceStatus != DeploymentSecretStoreStatus.Active ||
            secret.SecretStoreType != DeploymentSecretStoreType.LocalEncryptedDatabase ||
            string.IsNullOrWhiteSpace(secret.ProtectedSecret))
            return null;

        try
        {
            return dataProtectionProvider
                .CreateProtector("Elsa.Platform.EngineCredentialReferences")
                .Unprotect(secret.ProtectedSecret);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
