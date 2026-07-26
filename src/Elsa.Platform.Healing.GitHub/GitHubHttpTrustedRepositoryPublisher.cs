using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Healing.GitHub;

/// <summary>
/// Uses GitHub's Git Data API to create one commit, a deterministic repair branch, and one pull request.
/// It never accepts credentials or arbitrary request data from the repair workload.
/// </summary>
public sealed class GitHubHttpTrustedRepositoryPublisher(HttpClient httpClient) : ITrustedGitHubRepositoryPublisher
{
    public async ValueTask<string?> GetTargetRevisionAsync(
        GitHubRepositoryAuthorization authorization,
        string targetBranch,
        string installationToken,
        CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, Uri(authorization, $"git/ref/heads/{EscapePath(targetBranch)}"), installationToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        EnsureSuccess(response);
        return (await response.Content.ReadFromJsonAsync<GitReferenceResponse>(cancellationToken))?.Object.Sha;
    }

    public async ValueTask<bool> IsCommitReachableAsync(
        GitHubRepositoryAuthorization authorization,
        string ancestorRevision,
        string targetRevision,
        string installationToken,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(ancestorRevision, targetRevision, StringComparison.OrdinalIgnoreCase))
            return true;
        using var request = Request(
            HttpMethod.Get,
            Uri(authorization, $"compare/{EscapePath(ancestorRevision)}...{EscapePath(targetRevision)}"),
            installationToken);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        EnsureSuccess(response);
        var comparison = await response.Content.ReadFromJsonAsync<CompareResponse>(cancellationToken);
        return comparison?.Status is "ahead" or "identical";
    }

    public async ValueTask<ProviderPullRequestReference> PublishAsync(
        GitHubRepositoryAuthorization authorization,
        string installationToken,
        TrustedGitHubPatchPlan plan,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var commitMessage = $"{plan.CommitMessage}\n\nElsa-Healing-Idempotency: {EscapeMarker(idempotencyKey)}";
        var existingBranchRevision = await GetTargetRevisionAsync(
            authorization, plan.Branch, installationToken, cancellationToken);
        var baseCommit = await GetAsync<GitCommitResponse>(authorization, installationToken,
            $"git/commits/{Escape(plan.ExpectedBaseRevision)}", cancellationToken);
        var baseTree = await GetAsync<GitTreeResponse>(authorization, installationToken,
            $"git/trees/{Escape(baseCommit.Tree.Sha)}?recursive=1", cancellationToken);
        if (baseTree.Truncated)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        var baseEntries = baseTree.Tree.ToDictionary(x => x.Path, StringComparer.Ordinal);
        var entries = new List<object>(plan.Patch.Files.Count);
        foreach (var file in plan.Patch.Files)
        {
            var existed = baseEntries.TryGetValue(file.OldPath ?? file.EffectivePath, out var baseEntry);
            if (file.IsNew && existed || !file.IsNew && !existed ||
                existed && (baseEntry!.Type != "blob" || baseEntry.Mode is not ("100644" or "100755")))
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);

            if (file.IsDeleted)
            {
                var deletionBaseText = await GetFileTextAsync(
                    authorization, installationToken, file.OldPath!, plan.ExpectedBaseRevision, cancellationToken);
                if (Apply(file, deletionBaseText).Length != 0)
                    throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
                entries.Add(new { path = file.EffectivePath, mode = "100644", type = "blob", sha = (string?)null });
                continue;
            }

            var baseText = file.IsNew
                ? string.Empty
                : await GetFileTextAsync(authorization, installationToken, file.OldPath!, plan.ExpectedBaseRevision, cancellationToken);
            var content = Apply(file, baseText);
            var blob = await PostAsync<GitObjectResponse>(authorization, installationToken, "git/blobs",
                new { content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)), encoding = "base64" }, cancellationToken);
            entries.Add(new
            {
                path = file.EffectivePath,
                mode = file.IsNew ? "100644" : baseEntry!.Mode,
                type = "blob",
                sha = blob.Sha
            });
        }

        var tree = await PostAsync<GitObjectResponse>(authorization, installationToken, "git/trees",
            new { base_tree = baseCommit.Tree.Sha, tree = entries }, cancellationToken);
        if (existingBranchRevision is not null)
        {
            await ValidateExistingCommitAsync(
                authorization, installationToken, existingBranchRevision, plan.ExpectedBaseRevision,
                tree.Sha, commitMessage, cancellationToken);
            return await UpsertPullRequestAsync(
                authorization, installationToken, plan, existingBranchRevision, idempotencyKey, cancellationToken);
        }

        var commit = await PostAsync<GitObjectResponse>(authorization, installationToken, "git/commits",
            new { message = commitMessage, tree = tree.Sha, parents = new[] { plan.ExpectedBaseRevision } }, cancellationToken);
        var branchRevision = await CreateBranchAsync(
            authorization, installationToken, plan.Branch, commit.Sha, plan.ExpectedBaseRevision,
            tree.Sha, commitMessage, cancellationToken);
        return await UpsertPullRequestAsync(authorization, installationToken, plan, branchRevision, idempotencyKey, cancellationToken);
    }

    internal static string Apply(UnifiedDiffFile file, string original)
    {
        if (original.IndexOf('\0') >= 0 || original.Contains('\r'))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
        var source = original.Split('\n').ToList();
        if (source.Count == 1 && source[0].Length == 0 && file.IsNew)
            source.Clear();
        var output = new List<string>();
        var sourceIndex = 0;
        foreach (var hunk in file.Hunks)
        {
            var hunkIndex = hunk.OldStart == 0 ? 0 : hunk.OldStart - 1;
            if (hunkIndex < sourceIndex || hunkIndex > source.Count)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
            output.AddRange(source.GetRange(sourceIndex, hunkIndex - sourceIndex));
            sourceIndex = hunkIndex;
            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case ' ':
                        ExpectSource(source, sourceIndex, line.Text);
                        output.Add(line.Text);
                        sourceIndex++;
                        break;
                    case '-':
                        ExpectSource(source, sourceIndex, line.Text);
                        sourceIndex++;
                        break;
                    case '+':
                        output.Add(line.Text);
                        break;
                    default:
                        throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
                }
            }
        }
        output.AddRange(source.Skip(sourceIndex));
        return string.Join('\n', output);
    }

    private async ValueTask<string> GetFileTextAsync(
        GitHubRepositoryAuthorization authorization, string token, string path, string revision, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get,
            Uri(authorization, $"contents/{EscapePath(path)}?ref={Escape(revision)}"), token);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 2_097_152 || bytes.AsSpan().Contains((byte)0))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
        }
    }

    private async ValueTask<string> CreateBranchAsync(
        GitHubRepositoryAuthorization authorization,
        string token,
        string branch,
        string revision,
        string expectedBaseRevision,
        string expectedTreeRevision,
        string commitMessage,
        CancellationToken cancellationToken)
    {
        using var create = Request(HttpMethod.Post, Uri(authorization, "git/refs"), token);
        create.Content = JsonContent.Create(new { @ref = $"refs/heads/{branch}", sha = revision });
        using var createResponse = await httpClient.SendAsync(create, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (createResponse.IsSuccessStatusCode)
            return revision;
        if (createResponse.StatusCode != HttpStatusCode.UnprocessableEntity)
            EnsureSuccess(createResponse);

        var concurrentRevision = await GetTargetRevisionAsync(authorization, branch, token, cancellationToken)
                                 ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        await ValidateExistingCommitAsync(
            authorization, token, concurrentRevision, expectedBaseRevision, expectedTreeRevision,
            commitMessage, cancellationToken);
        return concurrentRevision;
    }

    private async ValueTask ValidateExistingCommitAsync(
        GitHubRepositoryAuthorization authorization,
        string token,
        string revision,
        string expectedBaseRevision,
        string expectedTreeRevision,
        string expectedMessage,
        CancellationToken cancellationToken)
    {
        var commit = await GetAsync<GitCommitResponse>(
            authorization, token, $"git/commits/{Escape(revision)}", cancellationToken);
        if (!string.Equals(commit.Message, expectedMessage, StringComparison.Ordinal) ||
            !string.Equals(commit.Tree.Sha, expectedTreeRevision, StringComparison.Ordinal) ||
            commit.Parents is not { Count: 1 } || !string.Equals(commit.Parents[0].Sha, expectedBaseRevision, StringComparison.Ordinal))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.IdempotencyConflict);
    }

    private async ValueTask<ProviderPullRequestReference> UpsertPullRequestAsync(
        GitHubRepositoryAuthorization authorization,
        string token,
        TrustedGitHubPatchPlan plan,
        string headRevision,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var head = $"{authorization.Owner}:{plan.Branch}";
        var existing = await GetAsync<IReadOnlyList<PullRequestResponse>>(authorization, token,
            $"pulls?state=all&head={Escape(head)}&base={Escape(plan.BaseBranch)}&per_page=10", cancellationToken);
        var body = $"{plan.PullRequestBody}\n\n<!-- elsa-healing:idempotency:{EscapeMarker(idempotencyKey)} -->";
        PullRequestResponse pullRequest;
        if (existing.FirstOrDefault() is { } current)
        {
            if (plan.IsDraft && !current.Draft)
                throw new GitHubSecurityException(GitHubSecurityReasonCodes.PublicationDenied);
            pullRequest = await PatchAsync<PullRequestResponse>(authorization, token, $"pulls/{current.Number}",
                new { title = plan.PullRequestTitle, body, @base = plan.BaseBranch, state = "open" }, cancellationToken);
        }
        else
        {
            pullRequest = await PostAsync<PullRequestResponse>(authorization, token, "pulls",
                new { title = plan.PullRequestTitle, body, head = plan.Branch, @base = plan.BaseBranch, draft = plan.IsDraft }, cancellationToken);
        }
        if (pullRequest.Id <= 0 || pullRequest.Number <= 0 || pullRequest.HtmlUrl is null)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
        return new ProviderPullRequestReference(
            pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), pullRequest.Number, pullRequest.HtmlUrl, headRevision,
            plan.ExpectedBaseRevision, pullRequest.Draft, null);
    }

    private async ValueTask<T> GetAsync<T>(GitHubRepositoryAuthorization authorization, string token, string path, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, Uri(authorization, path), token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
               ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
    }

    private ValueTask<T> PostAsync<T>(GitHubRepositoryAuthorization authorization, string token, string path, object payload, CancellationToken cancellationToken) =>
        WriteAsync<T>(authorization, token, HttpMethod.Post, path, payload, cancellationToken);

    private ValueTask<T> PatchAsync<T>(GitHubRepositoryAuthorization authorization, string token, string path, object payload, CancellationToken cancellationToken) =>
        WriteAsync<T>(authorization, token, HttpMethod.Patch, path, payload, cancellationToken);

    private async ValueTask<T> WriteAsync<T>(GitHubRepositoryAuthorization authorization, string token, HttpMethod method, string path, object payload, CancellationToken cancellationToken)
    {
        using var request = Request(method, Uri(authorization, path), token);
        request.Content = JsonContent.Create(payload);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
               ?? throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
    }

    private static HttpRequestMessage Request(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        GitHubAppTokenProvider.AddGitHubHeaders(request, token);
        return request;
    }

    private static string Uri(GitHubRepositoryAuthorization authorization, string path) =>
        $"repos/{Escape(authorization.Owner)}/{Escape(authorization.Name)}/{path}";

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.ProviderRejected);
    }

    private static void ExpectSource(IReadOnlyList<string> source, int index, string expected)
    {
        if (index >= source.Count || !string.Equals(source[index], expected, StringComparison.Ordinal))
            throw new GitHubSecurityException(GitHubSecurityReasonCodes.PatchInvalid);
    }

    private static string Escape(string value) => System.Uri.EscapeDataString(value);
    private static string EscapePath(string value) => string.Join('/', value.Split('/').Select(System.Uri.EscapeDataString));
    private static string EscapeMarker(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    private sealed record GitReferenceResponse([property: JsonPropertyName("object")] GitObjectResponse Object);
    private sealed record CompareResponse([property: JsonPropertyName("status")] string Status);
    private sealed record GitCommitResponse(
        [property: JsonPropertyName("tree")] GitObjectResponse Tree,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("parents")] IReadOnlyList<GitObjectResponse>? Parents);
    private sealed record GitObjectResponse([property: JsonPropertyName("sha")] string Sha);
    private sealed record GitTreeResponse(
        [property: JsonPropertyName("tree")] IReadOnlyList<GitTreeEntry> Tree,
        [property: JsonPropertyName("truncated")] bool Truncated);
    private sealed record GitTreeEntry(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("mode")] string Mode,
        [property: JsonPropertyName("type")] string Type);
    private sealed record PullRequestResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("number")] long Number,
        [property: JsonPropertyName("html_url")] System.Uri? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft);
}
