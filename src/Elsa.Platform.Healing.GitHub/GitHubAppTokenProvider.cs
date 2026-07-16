using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Platform.Healing.GitHub;

public sealed record GitHubInstallationToken(string Value, DateTimeOffset ExpiresAt);

public sealed class GitHubAppTokenProvider(HttpClient httpClient, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<GitHubInstallationToken?> CreateRepositoryTokenAsync(
        GitHubAppCredential credential,
        string installationId,
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(installationId) || string.IsNullOrWhiteSpace(repositoryName))
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
            Content = JsonContent.Create(new InstallationTokenRequest(
                [repositoryName],
                new Dictionary<string, string>(StringComparer.Ordinal) { ["metadata"] = "read" }))
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

    private string CreateAppJwt(GitHubAppCredential credential)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(credential.PrivateKeyPem);
        var now = _timeProvider.GetUtcNow();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = credential.AppId,
            IssuedAt = now.AddSeconds(-60).UtcDateTime,
            NotBefore = now.AddSeconds(-60).UtcDateTime,
            Expires = now.AddMinutes(9).UtcDateTime,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)
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

    private sealed record InstallationTokenRequest(
        [property: JsonPropertyName("repositories")] IReadOnlyList<string> Repositories,
        [property: JsonPropertyName("permissions")] IReadOnlyDictionary<string, string> Permissions);

    private sealed record InstallationTokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
