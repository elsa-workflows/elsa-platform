using System.Text.Json.Serialization;

namespace ElsaControl.Deployment.Azure;

public sealed record AzureSecretResolutionRequest(Guid WorkspaceId, string Name, string Reference)
{
    public void Validate()
    {
        if (WorkspaceId == Guid.Empty)
            throw new ArgumentException("Workspace ID is required.", nameof(WorkspaceId));
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 256 || Name.Any(char.IsControl))
            throw new ArgumentException("The secret name is unsafe.", nameof(Name));
        if (!AzureProviderOperationValidation.IsSafeSecretReference(Reference))
            throw new ArgumentException("The secret reference is unsafe.", nameof(Reference));
    }
}

/// <summary>
/// Short-lived, explicitly erasable secret material. The value is excluded from serialization,
/// has no value-bearing string representation, and is zeroed when the lease is disposed.
/// </summary>
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
