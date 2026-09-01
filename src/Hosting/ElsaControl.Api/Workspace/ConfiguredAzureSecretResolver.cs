using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Configuration;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Resolves only exact, preconfigured safe secret locators. Values are held in
/// process memory for the request and are never part of provider contracts,
/// diagnostics or durable records.
/// </summary>
internal sealed class ConfiguredAzureSecretResolver : IAzureSecretResolver
{
    private const int MaximumConfiguredSecretLength = 4096;
    private readonly IReadOnlyDictionary<string, string> _values;

    private ConfiguredAzureSecretResolver(IReadOnlyDictionary<string, string> values) => _values = values;

    public static IAzureSecretResolver Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection("Deployment:AzureProvider:Secrets").GetChildren())
        {
            var reference = child["Reference"]?.Trim();
            var value = child["Value"];
            if (string.IsNullOrWhiteSpace(reference) ||
                !AzureProviderOperationValidation.IsSafeSecretReference(reference) ||
                !IsSafeConfiguredValue(value) || !values.TryAdd(reference, value!))
                throw new InvalidOperationException("Azure provider secret aliases are invalid or duplicated.");
        }

        return new ConfiguredAzureSecretResolver(values);
    }

    public ValueTask<AzureSecretLease> ResolveAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();
        return _values.TryGetValue(request.Reference, out var value)
            ? ValueTask.FromResult(new AzureSecretLease(value.AsSpan()))
            : throw new InvalidOperationException("The requested Azure secret is not configured.");
    }

    private static bool IsSafeConfiguredValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumConfiguredSecretLength &&
        !value.Contains('\0');
}
