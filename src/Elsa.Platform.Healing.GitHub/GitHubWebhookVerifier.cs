using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Platform.Healing.GitHub;

public sealed record GitHubWebhookVerificationRequest(
    Guid WorkspaceId,
    ReadOnlyMemory<byte> Body,
    string? Signature256,
    string? DeliveryId,
    string? Event,
    ReadOnlyMemory<byte> Secret,
    string ExpectedInstallationId,
    string ExpectedRepositoryId,
    string ExpectedRepositoryFullName,
    IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedEventActions);

public sealed record VerifiedGitHubWebhook(
    string DeliveryId,
    string Event,
    string Action,
    string InstallationId,
    string RepositoryId,
    string RepositoryFullName,
    string BodyDigest,
    DateTimeOffset ReceivedAt);

public sealed record GitHubWebhookVerificationResult(
    bool Succeeded,
    bool IsReplay,
    string ReasonCode,
    VerifiedGitHubWebhook? Webhook)
{
    public static GitHubWebhookVerificationResult Valid(VerifiedGitHubWebhook webhook, bool isReplay = false) =>
        new(true, isReplay, isReplay ? GitHubSecurityReasonCodes.WebhookReplay : "github-webhook-valid", webhook);

    public static GitHubWebhookVerificationResult Invalid(string reasonCode) =>
        new(false, false, reasonCode, null);
}

public enum GitHubWebhookReplayDisposition
{
    Accepted,
    ExactReplay,
    Conflict
}

public sealed record GitHubWebhookReplayResult(
    GitHubWebhookReplayDisposition Disposition,
    string? ExistingBodyDigest = null)
{
    public static GitHubWebhookReplayResult Accepted() =>
        new(GitHubWebhookReplayDisposition.Accepted);

    public static GitHubWebhookReplayResult ExactReplay(string bodyDigest) =>
        new(GitHubWebhookReplayDisposition.ExactReplay, bodyDigest);

    public static GitHubWebhookReplayResult Conflict(string? existingBodyDigest = null) =>
        new(GitHubWebhookReplayDisposition.Conflict, existingBodyDigest);
}

public interface IGitHubWebhookReplayStore
{
    /// <summary>Atomically appends the canonical tenant-scoped delivery. Raw bodies are deliberately excluded.</summary>
    ValueTask<GitHubWebhookReplayResult> TryAcceptAsync(
        GitHubWebhookReplayRecord delivery,
        CancellationToken cancellationToken = default);
}

public sealed record GitHubWebhookReplayRecord(
    Guid WorkspaceId,
    string DeliveryId,
    string InstallationId,
    string RepositoryProviderId,
    string RepositoryFullName,
    string Event,
    string Action,
    string BodyDigest,
    DateTimeOffset ReceivedAt);

public sealed class GitHubWebhookVerifier(
    IGitHubWebhookReplayStore replayStore,
    TimeProvider? timeProvider = null,
    int maximumBodyBytes = 1_048_576)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<GitHubWebhookVerificationResult> VerifyAsync(
        GitHubWebhookVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WorkspaceId == Guid.Empty || request.Body.Length == 0 || request.Body.Length > maximumBodyBytes || request.Secret.Length is < 16 or > 4096 ||
            !IsSafeHeader(request.DeliveryId, 100) || !IsSafeHeader(request.Event, 100) ||
            !TryReadSignature(request.Signature256, out var suppliedSignature))
            return GitHubWebhookVerificationResult.Invalid(GitHubSecurityReasonCodes.WebhookInvalid);

        var computedSignature = HMACSHA256.HashData(request.Secret.Span, request.Body.Span);
        var signatureValid = CryptographicOperations.FixedTimeEquals(computedSignature, suppliedSignature);
        CryptographicOperations.ZeroMemory(computedSignature);
        CryptographicOperations.ZeroMemory(suppliedSignature);
        if (!signatureValid)
            return GitHubWebhookVerificationResult.Invalid(GitHubSecurityReasonCodes.WebhookInvalid);

        ParsedWebhook? parsed;
        try
        {
            using var document = JsonDocument.Parse(request.Body, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            parsed = Parse(document.RootElement);
        }
        catch (JsonException)
        {
            return GitHubWebhookVerificationResult.Invalid(GitHubSecurityReasonCodes.WebhookInvalid);
        }

        var action = parsed?.Action ?? string.Empty;
        if (parsed is null || !FixedEquals(parsed.InstallationId, request.ExpectedInstallationId) ||
            !FixedEquals(parsed.RepositoryId, request.ExpectedRepositoryId) ||
            !FixedEquals(parsed.RepositoryFullName, request.ExpectedRepositoryFullName) ||
            !request.AllowedEventActions.TryGetValue(request.Event!, out var allowedActions) ||
            !allowedActions.Contains(action))
            return GitHubWebhookVerificationResult.Invalid(GitHubSecurityReasonCodes.WebhookInvalid);

        var bodyDigest = Convert.ToHexString(SHA256.HashData(request.Body.Span)).ToLowerInvariant();
        var receivedAt = _timeProvider.GetUtcNow();
        var replayRecord = new GitHubWebhookReplayRecord(
            request.WorkspaceId, request.DeliveryId!, parsed.InstallationId, parsed.RepositoryId,
            parsed.RepositoryFullName, request.Event!, action, bodyDigest, receivedAt);
        var replayResult = await replayStore.TryAcceptAsync(replayRecord, cancellationToken);
        if (replayResult.Disposition == GitHubWebhookReplayDisposition.Conflict)
            return GitHubWebhookVerificationResult.Invalid(GitHubSecurityReasonCodes.WebhookReplay);

        return GitHubWebhookVerificationResult.Valid(new VerifiedGitHubWebhook(
            request.DeliveryId!, request.Event!, action, parsed.InstallationId, parsed.RepositoryId,
            parsed.RepositoryFullName, bodyDigest, receivedAt),
            replayResult.Disposition == GitHubWebhookReplayDisposition.ExactReplay);
    }

    private static ParsedWebhook? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("installation", out var installation) || installation.ValueKind != JsonValueKind.Object ||
            !installation.TryGetProperty("id", out var installationId) || !TryGetProviderId(installationId, out var installationValue) ||
            !root.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object ||
            !repository.TryGetProperty("id", out var repositoryId) || !TryGetProviderId(repositoryId, out var repositoryValue) ||
            !repository.TryGetProperty("full_name", out var fullName) || fullName.ValueKind != JsonValueKind.String ||
            fullName.GetString() is not { Length: > 0 and <= 201 } repositoryFullName)
            return null;

        var action = root.TryGetProperty("action", out var actionElement) && actionElement.ValueKind == JsonValueKind.String
            ? actionElement.GetString()
            : string.Empty;
        return action is null || action.Length > 100
            ? null
            : new ParsedWebhook(installationValue!, repositoryValue!, repositoryFullName, action);
    }

    private static bool TryGetProviderId(JsonElement element, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };
        return value is { Length: > 0 and <= 40 } && value.All(char.IsDigit);
    }

    private static bool TryReadSignature(string? value, out byte[] signature)
    {
        signature = [];
        if (value is null || value.Length != 71 || !value.StartsWith("sha256=", StringComparison.Ordinal))
            return false;
        try
        {
            signature = Convert.FromHexString(value.AsSpan(7));
            return signature.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsSafeHeader(string? value, int maximumLength) =>
        value is { Length: > 0 } && value.Length <= maximumLength && value.All(x => x is >= '!' and <= '~');

    private static bool FixedEquals(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private sealed record ParsedWebhook(string InstallationId, string RepositoryId, string RepositoryFullName, string Action);
}
