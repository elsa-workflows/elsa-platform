using System.Security.Cryptography;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Holds one run's generated proof credentials in erasable memory and derives the managed-
/// identity SQL connection only from the runner's verified foundation references.
/// </summary>
public sealed class DisposableAzureProofSecrets : IAzureSecretResolver, IElsaProofCredentialSource, IDisposable
{
    private char[]? _signingKey = GenerateSecret();
    private char[]? _adminPassword = GenerateSecret();

    public ValueTask<AzureSecretLease> ResolveAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();

        var normalized = request.Name.Trim().ToLowerInvariant();
        return normalized switch
        {
            "database:connectionstring" or "database:connection-string" or "sql-connection" =>
                ValueTask.FromResult(SqlConnection(request.Resources)),
            "identity:signingkey" or "identity:signing-key" or "identity-signing-key" =>
                ValueTask.FromResult(Copy(_signingKey)),
            "admin:password" or "admin-password" =>
                ValueTask.FromResult(Copy(_adminPassword)),
            _ => throw new InvalidOperationException("The proof secret reference is not governed by this host.")
        };
    }

    public ValueTask<AzureSecretLease> ResolvePasswordAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Copy(_adminPassword));
    }

    public void Dispose()
    {
        Clear(ref _signingKey);
        Clear(ref _adminPassword);
    }

    private static AzureSecretLease SqlConnection(AzureProviderResourceReferences? resources)
    {
        if (resources is null || string.IsNullOrWhiteSpace(resources.SqlServerFqdn) ||
            !Guid.TryParseExact(resources.WorkloadIdentityClientId, "D", out _))
            throw new InvalidOperationException("The verified Azure foundation references are incomplete.");
        var value = $"Server=tcp:{resources.SqlServerFqdn},1433;Initial Catalog=Elsa;Encrypt=True;Authentication=\"Active Directory Managed Identity\";User Id={resources.WorkloadIdentityClientId};TrustServerCertificate=False;Connection Timeout=30;";
        return new AzureSecretLease(value);
    }

    private static AzureSecretLease Copy(char[]? value) =>
        value is null ? throw new ObjectDisposedException(nameof(DisposableAzureProofSecrets)) : new(value);

    private static char[] GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).ToCharArray();

    private static void Clear(ref char[]? field)
    {
        var value = Interlocked.Exchange(ref field, null);
        if (value is not null)
            Array.Clear(value);
    }
}
