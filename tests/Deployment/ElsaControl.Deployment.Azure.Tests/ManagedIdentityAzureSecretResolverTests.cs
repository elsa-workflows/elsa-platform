using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class ManagedIdentityAzureSecretResolverTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrganizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid InstanceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AssignmentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OperationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string Version = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecretReference = $"https://source.vault.azure.net/secrets/sql-connection/{Version}";

    [Fact]
    public async Task Resolves_an_exact_durable_secret_reference_without_network_in_the_unit()
    {
        var reader = new FakeReader();
        var resolver = CreateResolver(reader);

        await using var lease = await resolver.ResolveAsync(Request());

        Assert.Equal("resolved-value", lease.Value.ToString());
        Assert.Equal(1, reader.Calls);
        Assert.Equal(new Uri("https://source.vault.azure.net/"), reader.VaultUri);
        Assert.Equal("sql-connection", reader.Name);
        Assert.Equal(Version, reader.Version);
    }

    [Theory]
    [InlineData("Organization")]
    [InlineData("Workspace")]
    [InlineData("Instance")]
    [InlineData("Assignment")]
    [InlineData("Name")]
    [InlineData("Reference")]
    public async Task Rejects_every_non_exact_authorization_tuple_member(string changed)
    {
        var reader = new FakeReader();
        var resolver = CreateResolver(reader);
        var request = Request() with
        {
            OrganizationId = changed == "Organization" ? Guid.NewGuid() : OrganizationId,
            WorkspaceId = changed == "Workspace" ? Guid.NewGuid() : WorkspaceId,
            InstanceId = changed == "Instance" ? Guid.NewGuid() : InstanceId,
            ProviderAssignmentId = changed == "Assignment" ? Guid.NewGuid().ToString("D") : AssignmentId.ToString("D"),
            Name = changed == "Name" ? "identity-signing-key" : "database:connectionstring",
            Reference = changed == "Reference"
                ? $"https://source.vault.azure.net/secrets/other-secret/{Version}"
                : SecretReference
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await resolver.ResolveAsync(request));

        Assert.Equal("The requested Azure secret is not authorized.", exception.Message);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("https://source.vault.azure.net/secrets/sql-connection")]
    [InlineData("https://source.vault.azure.net/secrets/sql-connection/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!")]
    [InlineData("https://source.vault.azure.net/secrets/sql-connection/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa?api-version=7.4")]
    [InlineData("https://other.example/secrets/sql-connection/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://source.vault.azure.net/secrets/sql-connection/../admin/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Rejects_unversioned_or_unsafe_key_vault_locators(string reference)
    {
        var reader = new FakeReader();
        var resolver = CreateResolver(reader);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await resolver.ResolveAsync(Request() with { Reference = reference }));

        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Rejects_a_reference_not_retained_by_the_durable_operation()
    {
        var reader = new FakeReader();
        var authorization = Authorization() with
        {
            Operation = Operation() with
            {
                SecretReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["database:connectionstring"] = $"https://source.vault.azure.net/secrets/sql-connection/{new string('b', 32)}"
                }
            }
        };
        var resolver = new ManagedIdentityAzureSecretResolver(new FakeAuthorizationStore(authorization), reader);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await resolver.ResolveAsync(Request()));

        Assert.Equal(0, reader.Calls);
    }

    private static ManagedIdentityAzureSecretResolver CreateResolver(FakeReader reader) =>
        new(new FakeAuthorizationStore(Authorization()), reader);

    private static AzureSecretResolutionRequest Request() => new(
        WorkspaceId,
        OrganizationId,
        InstanceId,
        AssignmentId.ToString("D"),
        "database:connectionstring",
        SecretReference);

    private static AzureSecretAuthorization Authorization() => new(
        new AzureProviderResourceAssignment(
            AssignmentId,
            WorkspaceId,
            OrganizationId,
            InstanceId,
            new string('c', 64),
            1,
            "66666666-6666-6666-6666-666666666666",
            "workload-rg",
            "workload",
            new string('d', 64),
            "westeurope",
            AzureProviderAssignmentState.Provisioning,
            new AzureProviderResourceReferences(KeyVaultUri: "https://workload-kv.vault.azure.net/"),
            OperationId,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow),
        Operation());

    private static AzureProviderOperation Operation() => new(
        OperationId,
        WorkspaceId,
        "workload",
        AzureProviderOperationAction.Reconcile,
        "idempotency",
        new string('e', 64),
        "operation-identity",
        new string('f', 64),
        new string('0', 64),
        "3.8.0",
        "3.8",
        "combined",
        "dedicated",
        "westeurope",
        "valenceruntimeimages.azurecr.io/runtime-combined",
        "sha256:" + new string('1', 64),
        null,
        null,
        AzureProviderOperationStatus.Running,
        AzureProviderOperationPhase.FoundationSubmitted,
        1,
        1,
        1,
        new AzureProviderResourceReferences(KeyVaultUri: "https://workload-kv.vault.azure.net/"),
        null,
        AzureProviderHealth.Unknown,
        [],
        null,
        null,
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        SecretReferences: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["database:connectionstring"] = SecretReference
        },
        ProviderScopeFingerprint: new string('c', 64),
        OrganizationId: OrganizationId,
        InstanceId: InstanceId,
        LifecycleAction: ElsaControl.Deployment.Abstractions.Instances.ElsaInstanceOperationAction.Create,
        ProviderAssignmentId: AssignmentId);

    private sealed class FakeAuthorizationStore(AzureSecretAuthorization authorization) : IAzureSecretAuthorizationStore
    {
        public Task<AzureSecretAuthorization?> GetAsync(
            Guid workspaceId,
            Guid providerAssignmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AzureSecretAuthorization?>(authorization);
    }

    private sealed class FakeReader : IAzureKeyVaultSecretReader
    {
        public int Calls { get; private set; }
        public Uri? VaultUri { get; private set; }
        public string? Name { get; private set; }
        public string? Version { get; private set; }

        public ValueTask<AzureSecretLease> GetAsync(
            Uri vaultUri,
            string name,
            string version,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            VaultUri = vaultUri;
            Name = name;
            Version = version;
            return ValueTask.FromResult(new AzureSecretLease("resolved-value"));
        }
    }
}
