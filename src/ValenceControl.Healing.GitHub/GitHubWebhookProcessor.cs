using System.Text.Json;

namespace ValenceControl.Healing.GitHub;

public abstract record GitHubHealingObservation(string Event, string Action, long RepositoryId);

public sealed record GitHubPullRequestObservation(
    string Action,
    long RepositoryId,
    long Number,
    string HeadReference,
    string HeadRevision,
    string BaseRevision,
    bool IsDraft,
    bool IsMerged,
    string? MergeRevision,
    DateTimeOffset? MergedAt) : GitHubHealingObservation("pull_request", Action, RepositoryId);

public sealed record GitHubCheckObservation(
    string Event,
    string Action,
    long RepositoryId,
    string Name,
    string HeadRevision,
    string Status,
    string? Conclusion,
    DateTimeOffset ObservedAt) : GitHubHealingObservation(Event, Action, RepositoryId);

public sealed record GitHubIssueCommandObservation(
    string Event,
    string Action,
    long RepositoryId,
    long IssueNumber,
    string Command,
    string ProviderActorId,
    string ProviderActorLogin,
    string AuthorAssociation) : GitHubHealingObservation(Event, Action, RepositoryId);

/// <summary>Parses only bounded structural GitHub observations. Provider text never becomes executable input.</summary>
public sealed class GitHubWebhookProcessor
{
    private const string CommandPrefix = "/valence-control-healing ";

    public GitHubHealingObservation? Parse(string eventName, ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            var root = document.RootElement;
            var action = Text(root, "action", 64);
            var repositoryId = Number(root.GetProperty("repository"), "id");
            return eventName switch
            {
                "pull_request" => PullRequest(root, action, repositoryId),
                "check_run" => CheckRun(root, action, repositoryId),
                "check_suite" => CheckSuite(root, action, repositoryId),
                "issue_comment" => IssueComment(root, action, repositoryId),
                "issues" => IssueLabel(root, action, repositoryId),
                _ => null
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static GitHubPullRequestObservation PullRequest(JsonElement root, string action, long repositoryId)
    {
        var pullRequest = root.GetProperty("pull_request");
        var merged = Boolean(pullRequest, "merged");
        return new(
            action,
            repositoryId,
            PositiveNumber(pullRequest, "number"),
            Text(pullRequest.GetProperty("head"), "ref", 256),
            GitRevision(pullRequest.GetProperty("head"), "sha"),
            GitRevision(pullRequest.GetProperty("base"), "sha"),
            Boolean(pullRequest, "draft"),
            merged,
            merged ? GitRevision(pullRequest, "merge_commit_sha") : null,
            merged ? NullableDate(pullRequest, "merged_at") ?? throw new JsonException() : null);
    }

    private static GitHubCheckObservation CheckRun(JsonElement root, string action, long repositoryId)
    {
        var check = root.GetProperty("check_run");
        return new(
            "check_run",
            action,
            repositoryId,
            Text(check, "name", 256),
            Text(check, "head_sha", 128),
            Text(check, "status", 64),
            NullableText(check, "conclusion", 64),
            NullableDate(check, "completed_at") ?? DateTimeOffset.UtcNow);
    }

    private static GitHubCheckObservation CheckSuite(JsonElement root, string action, long repositoryId)
    {
        var suite = root.GetProperty("check_suite");
        return new(
            "check_suite",
            action,
            repositoryId,
            "check-suite",
            Text(suite, "head_sha", 128),
            Text(suite, "status", 64),
            NullableText(suite, "conclusion", 64),
            NullableDate(suite, "updated_at") ?? DateTimeOffset.UtcNow);
    }

    private static GitHubIssueCommandObservation? IssueComment(JsonElement root, string action, long repositoryId)
    {
        if (action is not ("created" or "edited"))
            return null;
        var command = NormalizeCommand(Text(root.GetProperty("comment"), "body", 4_096));
        return command is null ? null : new(
            "issue_comment",
            action,
            repositoryId,
            PositiveNumber(root.GetProperty("issue"), "number"),
            command,
            ProviderId(root.GetProperty("sender").GetProperty("id")),
            Text(root.GetProperty("sender"), "login", 256),
            NullableText(root.GetProperty("comment"), "author_association", 64) ?? "NONE");
    }

    private static GitHubIssueCommandObservation? IssueLabel(JsonElement root, string action, long repositoryId)
    {
        if (action != "labeled")
            return null;
        var label = Text(root.GetProperty("label"), "name", 256);
        const string prefix = "valence-control-healing-command:";
        var command = label.StartsWith(prefix, StringComparison.Ordinal)
            ? NormalizeCommand(CommandPrefix + label[prefix.Length..])
            : null;
        return command is null ? null : new(
            "issues",
            action,
            repositoryId,
            PositiveNumber(root.GetProperty("issue"), "number"),
            command,
            ProviderId(root.GetProperty("sender").GetProperty("id")),
            Text(root.GetProperty("sender"), "login", 256),
            NullableText(root.GetProperty("issue"), "author_association", 64) ?? "NONE");
    }

    private static string? NormalizeCommand(string value)
    {
        var line = value.Trim();
        if (!line.StartsWith(CommandPrefix, StringComparison.Ordinal) || line.Contains('\n') || line.Contains('\r'))
            return null;
        var command = line[CommandPrefix.Length..].Trim();
        return command is "retry" or "stop" or "request-evidence" or "waive-environment" ? command : null;
    }

    private static string ProviderId(JsonElement value)
    {
        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => string.Empty
        };
        return long.TryParse(parsed, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0
            ? parsed
            : throw new JsonException();
    }

    private static long Number(JsonElement parent, string property) => parent.GetProperty(property).GetInt64();
    private static long PositiveNumber(JsonElement parent, string property)
    {
        var value = Number(parent, property);
        return value > 0 ? value : throw new JsonException();
    }
    private static string GitRevision(JsonElement parent, string property)
    {
        var value = Text(parent, property, 64);
        return value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit) ? value : throw new JsonException();
    }
    private static bool Boolean(JsonElement parent, string property) => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement parent, string property, int maximum) =>
        NullableText(parent, property, maximum) ?? throw new JsonException();
    private static string? NullableText(JsonElement parent, string property, int maximum)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)
            ? throw new JsonException()
            : text;
    }
    private static DateTimeOffset? NullableDate(JsonElement parent, string property) =>
        NullableText(parent, property, 64) is { } value ? DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : null;
}
