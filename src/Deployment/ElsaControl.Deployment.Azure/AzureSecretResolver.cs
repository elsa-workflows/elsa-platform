using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace ElsaControl.Deployment.Azure;

public sealed record AzureSecretResolutionRequest(
    Guid WorkspaceId,
    Guid OrganizationId,
    Guid InstanceId,
    string ProviderAssignmentId,
    string Name,
    string Reference,
    AzureProviderResourceReferences? Resources = null)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty || OrganizationId == Guid.Empty || InstanceId == Guid.Empty)
            throw new ArgumentException("The provider secret ownership identity is invalid.", nameof(WorkspaceId));
        if (string.IsNullOrWhiteSpace(ProviderAssignmentId) || ProviderAssignmentId.Length > 128 ||
            ProviderAssignmentId.Any(char.IsControl) || ProviderAssignmentId.Any(char.IsWhiteSpace))
            throw new ArgumentException("The provider assignment identity is invalid.", nameof(ProviderAssignmentId));
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 256 ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                Name,
                "^[a-z0-9][a-z0-9._:-]{0,255}\\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant |
                System.Text.RegularExpressions.RegexOptions.NonBacktracking))
            throw new ArgumentException("The secret name is unsafe.", nameof(Name));
        if (!AzureProviderOperationValidation.IsSafeSecretReference(Reference))
            throw new ArgumentException("The secret reference is unsafe.", nameof(Reference));
        if (Resources is not null)
            AzureProviderOperationValidation.ValidateReferences(Resources);
    }
}

/// <summary>
/// Short-lived, explicitly erasable secret material. The value is excluded from serialization,
/// has no value-bearing string representation, and is zeroed when the lease is disposed.
/// </summary>
[JsonConverter(typeof(AzureSecretLeaseJsonConverter))]
public sealed class AzureSecretLease : IAsyncDisposable, IDisposable
{
    private char[]? _value;

    public AzureSecretLease(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            throw new ArgumentException("Resolved secret material cannot be empty.", nameof(value));
        _value = value.ToArray();
    }

    [JsonIgnore]
    public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(AzureSecretLease));

    public void Dispose()
    {
        var value = Interlocked.Exchange(ref _value, null);
        if (value is not null)
            Array.Clear(value);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(AzureSecretLease);
}

public sealed class AzureSecretLeaseJsonConverter : JsonConverter<AzureSecretLease>
{
    public override AzureSecretLease? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Secret leases cannot be deserialized.");

    public override void Write(Utf8JsonWriter writer, AzureSecretLease value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Secret leases cannot be serialized.");
}

public interface IAzureSecretResolver
{
    ValueTask<AzureSecretLease> ResolveAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The provider-only durable authorization snapshot used by the external secret resolver.
/// It deliberately contains references and identities only; secret material is never part of
/// this contract.
/// </summary>
public sealed record AzureSecretAuthorization(
    AzureProviderResourceAssignment Assignment,
    AzureProviderOperation Operation);

/// <summary>
/// Reads the provider-owned authorization snapshot for a secret resolution request.
/// </summary>
public interface IAzureSecretAuthorizationStore
{
    Task<AzureSecretAuthorization?> GetAsync(
        Guid workspaceId,
        Guid providerAssignmentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Joins the durable provider assignment and its latest operation snapshot. Secret references
/// are read from the operation's provider-only persistence projection, never from customer
/// payloads or configuration values.
/// </summary>
public sealed class DurableAzureSecretAuthorizationStore(
    IAzureProviderResourceAssignmentStore assignmentStore,
    IAzureProviderOperationStore operationStore) : IAzureSecretAuthorizationStore
{
    public async Task<AzureSecretAuthorization?> GetAsync(
        Guid workspaceId,
        Guid providerAssignmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignmentStore);
        ArgumentNullException.ThrowIfNull(operationStore);
        var assignment = await assignmentStore.GetAsync(workspaceId, providerAssignmentId, cancellationToken);
        if (assignment?.LastOperationId is not { } operationId)
            return null;

        var operation = await operationStore.GetAsync(workspaceId, operationId, cancellationToken);
        return operation is null ? null : new AzureSecretAuthorization(assignment, operation);
    }
}

/// <summary>
/// Minimal Key Vault read boundary. Tests can provide an in-memory implementation, while the
/// production implementation below uses the Azure SDK and a managed identity credential.
/// </summary>
public interface IAzureKeyVaultSecretReader
{
    ValueTask<AzureSecretLease> GetAsync(
        Uri vaultUri,
        string name,
        string version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Production Key Vault reader. The credential type is intentionally restricted to
/// <see cref="ManagedIdentityCredential"/> so this boundary cannot silently fall back to
/// developer credentials, environment secrets or a client secret.
/// </summary>
// Credential: https://learn.microsoft.com/en-us/dotnet/api/azure.identity.managedidentitycredential?view=azure-dotnet
public sealed class AzureKeyVaultSecretReader : IAzureKeyVaultSecretReader
{
    private readonly ManagedIdentityCredential _credential;
    private readonly ConcurrentDictionary<string, SecretClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public AzureKeyVaultSecretReader(ManagedIdentityCredential credential)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    }

    public async ValueTask<AzureSecretLease> GetAsync(
        Uri vaultUri,
        string name,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vaultUri);
        if (!AzureKeyVaultSecretLocator.TryParseVaultUri(vaultUri, out var normalizedVaultUri))
            throw new ArgumentException("The Key Vault URI is unsafe.", nameof(vaultUri));
        if (!AzureKeyVaultSecretLocator.IsSafeSecretName(name))
            throw new ArgumentException("The Key Vault secret name is unsafe.", nameof(name));
        if (!AzureKeyVaultSecretLocator.IsSafeVersion(version))
            throw new ArgumentException("The Key Vault secret version is unsafe.", nameof(version));

        var client = _clients.GetOrAdd(
            normalizedVaultUri.AbsoluteUri,
            _ => new SecretClient(normalizedVaultUri, _credential));
        // Versioned secret read: https://learn.microsoft.com/en-us/dotnet/api/azure.security.keyvault.secrets.secretclient.getsecretasync?view=azure-dotnet
        var response = await client.GetSecretAsync(name, version, cancellationToken: cancellationToken);
        var value = response.Value.Value;
        if (string.IsNullOrEmpty(value) || value.Contains('\0'))
            throw new InvalidOperationException("The Key Vault secret value is empty or invalid.");
        return new AzureSecretLease(value.AsSpan());
    }
}

/// <summary>
/// Parses a versioned Key Vault secret identifier. The value is a locator only; it is never
/// dereferenced by this parser.
/// </summary>
public sealed record AzureKeyVaultSecretLocator(
    Uri VaultUri,
    string Name,
    string Version)
{
    private static readonly Regex VaultHostPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{1,22}[a-z0-9])?\\.vault\\.azure\\.net$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | RegexOptions.IgnoreCase);
    private static readonly Regex SecretNamePattern = new(
        "^[A-Za-z0-9-]{1,127}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex VersionPattern = new(
        "^[A-Fa-f0-9]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryParse(string? value, out AzureKeyVaultSecretLocator? locator)
    {
        locator = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsWhiteSpace) ||
            value.Any(char.IsControl) || value.Contains('%') || value.Contains('\\') ||
            value.Contains("/../", StringComparison.Ordinal) || value.Contains("/./", StringComparison.Ordinal) ||
            value.EndsWith("/..", StringComparison.Ordinal) || value.EndsWith("/.", StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            string.IsNullOrWhiteSpace(uri.Host) || !VaultHostPattern.IsMatch(uri.DnsSafeHost) ||
            uri.DnsSafeHost.Contains("--", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.None);
        if (segments.Length != 4 || segments[0].Length != 0 ||
            !string.Equals(segments[1], "secrets", StringComparison.OrdinalIgnoreCase) ||
            !IsSafeSecretName(segments[2]) || !IsSafeVersion(segments[3]))
            return false;

        var vaultUri = new Uri($"https://{uri.DnsSafeHost}/", UriKind.Absolute);
        locator = new AzureKeyVaultSecretLocator(vaultUri, segments[2], segments[3]);
        return true;
    }

    internal static bool TryParseVaultUri(Uri uri, out Uri normalized)
    {
        normalized = null!;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            string.IsNullOrWhiteSpace(uri.Host) || !VaultHostPattern.IsMatch(uri.DnsSafeHost) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.DnsSafeHost.Contains("--", StringComparison.Ordinal))
            return false;
        normalized = new Uri($"https://{uri.DnsSafeHost}/", UriKind.Absolute);
        return true;
    }

    internal static bool IsSafeSecretName(string? value) =>
        value is not null && SecretNamePattern.IsMatch(value);

    internal static bool IsSafeVersion(string? value) =>
        value is not null && VersionPattern.IsMatch(value);
}

/// <summary>
/// Resolves only a versioned external Key Vault locator which is retained in the exact durable
/// provider operation bound to the requested organization, workspace, instance and assignment.
/// </summary>
public sealed class ManagedIdentityAzureSecretResolver : IAzureSecretResolver
{
    private readonly IAzureSecretAuthorizationStore _authorizationStore;
    private readonly IAzureKeyVaultSecretReader _reader;

    public ManagedIdentityAzureSecretResolver(
        IAzureSecretAuthorizationStore authorizationStore,
        IAzureKeyVaultSecretReader reader)
    {
        _authorizationStore = authorizationStore ?? throw new ArgumentNullException(nameof(authorizationStore));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public ManagedIdentityAzureSecretResolver(
        IAzureProviderResourceAssignmentStore assignmentStore,
        IAzureProviderOperationStore operationStore,
        IAzureKeyVaultSecretReader reader)
        : this(new DurableAzureSecretAuthorizationStore(assignmentStore, operationStore), reader)
    {
    }

    public async ValueTask<AzureSecretLease> ResolveAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (!Guid.TryParseExact(request.ProviderAssignmentId, "D", out var assignmentId) ||
            !AzureKeyVaultSecretLocator.TryParse(request.Reference, out var locator))
            throw new ArgumentException("The Key Vault secret locator is unsafe.", nameof(request));

        var authorization = await _authorizationStore.GetAsync(request.WorkspaceId, assignmentId, cancellationToken);
        if (!IsAuthorized(request, assignmentId, locator!, authorization))
            throw new InvalidOperationException("The requested Azure secret is not authorized.");

        return await _reader.GetAsync(locator!.VaultUri, locator.Name, locator.Version, cancellationToken);
    }

    private static bool IsAuthorized(
        AzureSecretResolutionRequest request,
        Guid assignmentId,
        AzureKeyVaultSecretLocator locator,
        AzureSecretAuthorization? authorization)
    {
        if (authorization is null)
            return false;

        var assignment = authorization.Assignment;
        var operation = authorization.Operation;
        if (assignment.Id != assignmentId || assignment.WorkspaceId != request.WorkspaceId ||
            assignment.OrganizationId != request.OrganizationId || assignment.InstanceId != request.InstanceId ||
            assignment.State is AzureProviderAssignmentState.Deleted or AzureProviderAssignmentState.Unknown ||
            operation.WorkspaceId != request.WorkspaceId || operation.OrganizationId != request.OrganizationId ||
            operation.InstanceId != request.InstanceId || operation.ProviderAssignmentId != assignmentId ||
            operation.PersistedMetadataInvalid ||
            !AzureProviderOperationValidation.IsSafeSecretReferences(operation.SecretReferences) ||
            !operation.SafeSecretReferences.TryGetValue(request.Name, out var persistedReference) ||
            !string.Equals(persistedReference, request.Reference, StringComparison.OrdinalIgnoreCase))
            return false;

        string expectedName;
        try
        {
            expectedName = AzureProviderOperationValidation.MapSecretName(request.Name);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return string.Equals(locator.Name, expectedName, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class UnconfiguredAzureSecretResolver : IAzureSecretResolver
{
    public ValueTask<AzureSecretLease> ResolveAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No transient Azure secret resolver is configured.");
}
