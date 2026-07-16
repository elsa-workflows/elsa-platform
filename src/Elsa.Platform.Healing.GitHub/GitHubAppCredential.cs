using System.Text.Json;

namespace Elsa.Platform.Healing.GitHub;

public sealed record GitHubAppCredential(string AppId, string PrivateKeyPem)
{
    private const int MaximumCredentialLength = 16_384;

    public static bool TryParse(string? value, out GitHubAppCredential? credential)
    {
        credential = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumCredentialLength)
            return false;

        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("appId", out var appIdElement) ||
                !root.TryGetProperty("privateKeyPem", out var privateKeyElement))
                return false;

            var appId = appIdElement.ValueKind switch
            {
                JsonValueKind.String => appIdElement.GetString(),
                JsonValueKind.Number => appIdElement.GetRawText(),
                _ => null
            };
            var privateKey = privateKeyElement.ValueKind == JsonValueKind.String
                ? privateKeyElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(appId) || appId.Length > 128 ||
                !appId.All(char.IsLetterOrDigit) ||
                string.IsNullOrWhiteSpace(privateKey) ||
                privateKey.Length > 12_288 ||
                !privateKey.Contains("BEGIN", StringComparison.Ordinal))
                return false;

            credential = new GitHubAppCredential(appId, privateKey);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
