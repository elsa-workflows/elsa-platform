using System.Text.Json;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class DisposableAzureProofSecretsTests
{
    [Fact]
    public async Task Resolves_same_ephemeral_admin_password_without_serializing_it()
    {
        using var secrets = new DisposableAzureProofSecrets();
        await using var first = await secrets.ResolveAsync(Request(
            "admin:password", "secret://proof/admin-password"));
        await using var second = await secrets.ResolvePasswordAsync();

        Assert.Equal(first.Value.ToString(), second.Value.ToString());
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(first));
        Assert.Equal(nameof(AzureSecretLease), first.ToString());
    }

    [Fact]
    public async Task Derives_managed_identity_connection_from_verified_foundation_references()
    {
        using var secrets = new DisposableAzureProofSecrets();
        var clientId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var resources = new AzureProviderResourceReferences(
            WorkloadIdentityClientId: clientId,
            SqlServerFqdn: "proof-sql.database.windows.net");

        await using var lease = await secrets.ResolveAsync(Request(
            "database:connectionstring", "secret://proof/sql-connection", resources));

        var connection = lease.Value.ToString();
        Assert.Contains("proof-sql.database.windows.net", connection, StringComparison.Ordinal);
        Assert.Contains(clientId, connection, StringComparison.Ordinal);
        Assert.Contains("Active Directory Managed Identity", connection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_connection_without_verified_foundation_references()
    {
        using var secrets = new DisposableAzureProofSecrets();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await secrets.ResolveAsync(Request(
                "database:connectionstring", "secret://proof/sql-connection")));
    }

    private static AzureSecretResolutionRequest Request(
        string name,
        string reference,
        AzureProviderResourceReferences? resources = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "proof-assignment", name, reference, resources);
}
