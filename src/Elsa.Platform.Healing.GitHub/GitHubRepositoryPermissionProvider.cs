using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Healing.GitHub;

public sealed record GitHubRepositoryPermissionSnapshot(
    string ProviderActorId,
    string ProviderActorLogin,
    string Permission,
    DateTimeOffset ObservedAt)
{
    public bool IsMaintainer => Permission is "admin" or "maintain" or "write";
}

public interface IGitHubRepositoryPermissionProvider
{
    ValueTask<GitHubRepositoryPermissionSnapshot> GetAsync(
        ProviderRepositoryReference repository,
        string providerActorId,
        string providerActorLogin,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubRepositoryPermissionProvider(
    HttpClient httpClient,
    GitHubAppTokenProvider tokenProvider,
    IGitHubRepositoryAuthorizationResolver authorizationResolver,
    TimeProvider timeProvider) : IGitHubRepositoryPermissionProvider
{
    public async ValueTask<GitHubRepositoryPermissionSnapshot> GetAsync(
        ProviderRepositoryReference repository,
        string providerActorId,
        string providerActorLogin,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerActorId) || !long.TryParse(providerActorId, out var expectedId) || expectedId <= 0 ||
            string.IsNullOrWhiteSpace(providerActorLogin) || providerActorLogin.Length > 100 ||
            providerActorLogin.Any(x => !(char.IsLetterOrDigit(x) || x == '-')))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.InvalidRequest);
        var authorization = await authorizationResolver.ResolveAsync(repository.ProviderConnectionId, cancellationToken);
        if (authorization is null || authorization.RepositoryProviderId != repository.RepositoryProviderId ||
            authorization.Owner != repository.Owner || authorization.Name != repository.Name)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.RepositoryNotAuthorized);
        var token = await tokenProvider.CreateRepositoryTokenAsync(
            authorization.Credential,
            authorization.InstallationId,
            GitHubInstallationTokenRequest.MetadataRead(authorization.Name),
            cancellationToken) ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.TokenUnavailable);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"repos/{Uri.EscapeDataString(authorization.Owner)}/{Uri.EscapeDataString(authorization.Name)}/collaborators/{Uri.EscapeDataString(providerActorLogin)}/permission");
        GitHubAppTokenProvider.AddGitHubHeaders(request, token.Value);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new(providerActorId, providerActorLogin, "none", timeProvider.GetUtcNow());
        if (!response.IsSuccessStatusCode)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        var payload = await response.Content.ReadFromJsonAsync<PermissionResponse>(cancellationToken)
                      ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        if (payload.User is null || payload.User.Id != expectedId ||
            !string.Equals(payload.User.Login, providerActorLogin, StringComparison.OrdinalIgnoreCase) ||
            payload.Permission is not ("admin" or "maintain" or "write" or "triage" or "read" or "none"))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        return new(providerActorId, payload.User.Login, payload.Permission, timeProvider.GetUtcNow());
    }

    private sealed record PermissionResponse(
        [property: JsonPropertyName("permission")] string Permission,
        [property: JsonPropertyName("user")] UserResponse? User);

    private sealed record UserResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("login")] string Login);
}
