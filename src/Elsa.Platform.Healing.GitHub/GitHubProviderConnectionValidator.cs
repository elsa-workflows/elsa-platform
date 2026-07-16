using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Ownership;

namespace Elsa.Platform.Healing.GitHub;

public sealed class GitHubProviderConnectionValidator(
    HttpClient httpClient,
    GitHubAppTokenProvider tokenProvider,
    IHealingProviderCredentialResolver credentialResolver) : IProviderConnectionValidator
{
    public async ValueTask<ProviderConnectionValidationResult> ValidateAsync(
        ProviderConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!string.Equals(connection.Provider, "GitHub", StringComparison.OrdinalIgnoreCase))
            return ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderValidationUnavailable);

        var rawCredential = await credentialResolver.ResolveAsync(
            connection.WorkspaceId,
            connection.CredentialReference,
            cancellationToken);
        if (!GitHubAppCredential.TryParse(rawCredential, out var credential))
            return ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderValidationFailed);

        var token = await tokenProvider.CreateRepositoryTokenAsync(
            credential!, connection.InstallationId, connection.RepositoryName, cancellationToken);
        if (token is null)
            return ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderValidationFailed);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"repos/{Uri.EscapeDataString(connection.RepositoryOwner)}/{Uri.EscapeDataString(connection.RepositoryName)}");
        GitHubAppTokenProvider.AddGitHubHeaders(request, token.Value);
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderValidationFailed);

            var repository = await response.Content.ReadFromJsonAsync<GitHubRepositoryIdentity>(cancellationToken);
            var expectedFullName = $"{connection.RepositoryOwner}/{connection.RepositoryName}";
            return repository is null || repository.Id <= 0 ||
                   !string.Equals(repository.FullName, expectedFullName, StringComparison.OrdinalIgnoreCase)
                ? ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderRepositoryMismatch)
                : ProviderConnectionValidationResult.Valid(repository.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (GitHubAppTokenProvider.IsRecoverableProviderFailure(exception, cancellationToken))
        {
            return ProviderConnectionValidationResult.Invalid(HealingOwnershipReasonCodes.ProviderValidationFailed);
        }
    }

    private sealed record GitHubRepositoryIdentity(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("full_name")] string FullName);
}
