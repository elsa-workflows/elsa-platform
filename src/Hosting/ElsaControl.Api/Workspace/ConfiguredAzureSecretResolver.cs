using ElsaControl.Deployment.Azure;
using Microsoft.Extensions.Configuration;
using System.Collections.ObjectModel;

namespace ElsaControl.Api.Workspace;

/// <summary>
/// Development-only resolver for exact preconfigured secret locators. Production runner
/// composition rejects raw values before this resolver can be constructed.
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

    public static IReadOnlyDictionary<string, string> ReadNamedReferences(
        IConfiguration configuration,
        bool requireProviderOwnedCredentials = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var referencesByLocator = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in configuration.GetSection("Deployment:AzureProvider:Secrets").GetChildren())
        {
            var name = child["Name"]?.Trim();
            var reference = child["Reference"]?.Trim();
            var normalizedReference = reference;
            if (AzureManagedSecretReferences.IsProviderOwned(name, reference))
            {
                normalizedReference = reference;
            }
            else if (AzureManagedSecretReferences.IsProviderOwnedReference(reference) ||
                     requireProviderOwnedCredentials &&
                     (string.Equals(name, AzureManagedSecretReferences.IdentitySigningKeyName, StringComparison.Ordinal) ||
                      string.Equals(name, AzureManagedSecretReferences.AdminPasswordName, StringComparison.Ordinal)))
            {
                normalizedReference = null;
            }
            else
            {
                if (!AzureKeyVaultSecretLocator.TryParse(reference, out var locator))
                    normalizedReference = null;
                else
                    normalizedReference = locator!.PlanReference;
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(normalizedReference) ||
                !references.TryAdd(name, normalizedReference) ||
                !referencesByLocator.Add(normalizedReference))
                throw new InvalidOperationException("Azure provider named secret references are invalid, duplicated, or unsupported.");
        }

        if (!AzureProviderOperationValidation.IsSafeSecretReferences(references) ||
            AzureWorkloadPlanTranslator.RequiredSecretKeys.Any(required => !references.ContainsKey(required)))
            throw new InvalidOperationException("Azure provider named secret references are incomplete or unsafe.");

        return new ReadOnlyDictionary<string, string>(references);
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

    public ValueTask<bool> IsAuthorizedAsync(
        AzureSecretResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request.Validate();
        return ValueTask.FromResult(_values.ContainsKey(request.Reference));
    }

    private static bool IsSafeConfiguredValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumConfiguredSecretLength &&
        !value.Contains('\0');
}
