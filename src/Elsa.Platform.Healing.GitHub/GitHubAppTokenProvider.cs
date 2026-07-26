using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Platform.Healing.GitHub;

public sealed record GitHubInstallationToken(string Value, DateTimeOffset ExpiresAt);

public sealed record GitHubInstallationTokenRequest(
    string RepositoryName,
    IReadOnlyDictionary<string, string> Permissions)
{
    public static GitHubInstallationTokenRequest MetadataRead(string repositoryName) =>
        new(repositoryName, PermissionSet(("metadata", "read")));

    public static GitHubInstallationTokenRequest IssueWrite(string repositoryName) =>
        new(repositoryName, PermissionSet(("issues", "write"), ("metadata", "read")));

    public static GitHubInstallationTokenRequest WorkflowDispatch(string repositoryName) =>
        new(repositoryName, PermissionSet(("actions", "write"), ("metadata", "read")));

    public static GitHubInstallationTokenRequest ContentAndPullRequestWrite(string repositoryName) =>
        new(repositoryName, PermissionSet(("contents", "write"), ("pull_requests", "write"), ("metadata", "read")));

    public static GitHubInstallationTokenRequest PullRequestRead(string repositoryName) =>
        new(repositoryName, PermissionSet(("pull_requests", "read"), ("metadata", "read")));

    public static GitHubInstallationTokenRequest BranchProtectionRead(string repositoryName) =>
        new(repositoryName, PermissionSet(("administration", "read"), ("metadata", "read")));

    public static GitHubInstallationTokenRequest ChecksAndStatusesRead(string repositoryName) =>
        new(repositoryName, PermissionSet(("checks", "read"), ("statuses", "read"), ("metadata", "read")));

    public static GitHubInstallationTokenRequest MergeWrite(string repositoryName) =>
        new(repositoryName, PermissionSet(("contents", "write"), ("metadata", "read")));

    private static IReadOnlyDictionary<string, string> PermissionSet(params (string Name, string Access)[] permissions) =>
        permissions.ToDictionary(x => x.Name, x => x.Access, StringComparer.Ordinal);
}

public sealed class GitHubAppTokenProvider(HttpClient httpClient, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<GitHubInstallationToken?> CreateRepositoryTokenAsync(
        GitHubAppCredential credential,
        string installationId,
        string repositoryName,
        CancellationToken cancellationToken = default)
        => await CreateRepositoryTokenAsync(
            credential,
            installationId,
            GitHubInstallationTokenRequest.MetadataRead(repositoryName),
            cancellationToken);

    public async ValueTask<GitHubInstallationToken?> CreateRepositoryTokenAsync(
        GitHubAppCredential credential,
        string installationId,
        GitHubInstallationTokenRequest tokenRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(tokenRequest);
        if (string.IsNullOrWhiteSpace(installationId) ||
            !IsRepositoryName(tokenRequest.RepositoryName) ||
            !AreNarrowPermissions(tokenRequest.Permissions))
            return null;

        string appJwt;
        try
        {
            appJwt = CreateAppJwt(credential);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"app/installations/{Uri.EscapeDataString(installationId)}/access_tokens")
        {
            Content = JsonContent.Create(new InstallationTokenPayload(
                [tokenRequest.RepositoryName],
                tokenRequest.Permissions))
        };
        AddGitHubHeaders(request, appJwt);
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<InstallationTokenResponse>(cancellationToken);
            return payload is null || string.IsNullOrWhiteSpace(payload.Token) || payload.ExpiresAt <= _timeProvider.GetUtcNow()
                ? null
                : new GitHubInstallationToken(payload.Token, payload.ExpiresAt);
        }
        catch (Exception exception) when (IsRecoverableProviderFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private static bool IsRepositoryName(string value) =>
        value.Length is > 0 and <= 100 && value.All(x => char.IsLetterOrDigit(x) || x is '.' or '-' or '_');

    private static bool AreNarrowPermissions(IReadOnlyDictionary<string, string> permissions)
    {
        if (permissions.Count is < 1 or > 3 || !permissions.ContainsKey("metadata"))
            return false;

        foreach (var (permission, access) in permissions)
        {
            if (permission is not ("metadata" or "issues" or "actions" or "contents" or "pull_requests" or
                "administration" or "checks" or "statuses") ||
                access is not ("read" or "write") ||
                permission == "metadata" && access != "read")
                return false;
        }

        return true;
    }

    private string CreateAppJwt(GitHubAppCredential credential)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credential.PrivateKeyPem);
        var now = _timeProvider.GetUtcNow();
        var signingKey = new RsaSecurityKey(rsa)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = credential.AppId,
            IssuedAt = now.AddSeconds(-60).UtcDateTime,
            NotBefore = now.AddSeconds(-60).UtcDateTime,
            Expires = now.AddMinutes(9).UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    internal static void AddGitHubHeaders(HttpRequestMessage request, string bearerToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.UserAgent.ParseAdd("Elsa-Platform-Healing/1.0");
    }

    internal static bool IsRecoverableProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or IOException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private sealed record InstallationTokenPayload(
        [property: JsonPropertyName("repositories")] IReadOnlyList<string> Repositories,
        [property: JsonPropertyName("permissions")] IReadOnlyDictionary<string, string> Permissions);

    private sealed record InstallationTokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
