using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed class UnconfiguredAzureSecretResolver : IAzureSecretResolver
{
    public ValueTask<AzureSecretLease> ResolveAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No transient Azure secret resolver is configured.");
}
