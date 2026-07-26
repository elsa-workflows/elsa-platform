using System.Text.Json;

namespace ValenceControl.Healing.GitHub;

public sealed record GitHubWebhookSecret(string Value)
{
    public static bool TryParse(string? json, out GitHubWebhookSecret? secret)
    {
        secret = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("webhookSecret", out var value) ||
                value.ValueKind != JsonValueKind.String ||
                value.GetString() is not { Length: >= 16 and <= 4096 } parsed ||
                parsed.Any(char.IsControl))
                return false;
            secret = new GitHubWebhookSecret(parsed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
