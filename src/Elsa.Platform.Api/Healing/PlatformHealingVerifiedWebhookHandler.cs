using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Healing;

public sealed class PlatformHealingVerifiedWebhookHandler(
    HealingDbContext dbContext,
    IHealingProviderCredentialResolver credentialResolver,
    GitHubWebhookVerifier verifier,
    IPlatformHealingGitHubWebhookProcessorRunner processorRunner) : IHealingVerifiedWebhookHandler
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedEvents =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["pull_request"] = Set("opened", "reopened", "synchronize", "closed", "converted_to_draft", "ready_for_review"),
            ["check_run"] = Set("created", "rerequested", "completed"),
            ["check_suite"] = Set("requested", "rerequested", "completed"),
            ["issues"] = Set("labeled", "unlabeled", "closed", "reopened"),
            ["issue_comment"] = Set("created", "edited")
        };

    public async ValueTask<HealingVerifiedWebhookReceipt> ProcessAsync(
        HealingVerifiedWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadUntrustedRouting(request.RawBody, out var installationId, out var repositoryId))
            throw Rejected();
        var candidates = await dbContext.ProviderConnections.AsNoTracking()
            .Where(x => x.Provider == "GitHub" &&
                        x.Status == ProviderConnectionStatus.Active &&
                        x.InstallationId == installationId &&
                        x.RepositoryProviderId == repositoryId)
            .OrderBy(x => x.WorkspaceId)
            .ThenBy(x => x.Id)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            throw Rejected();

        var authorities = new List<(ProviderConnection Connection, bool IsReplay)>(candidates.Length);
        foreach (var workspaceCandidates in candidates.GroupBy(x => x.WorkspaceId))
        {
            if (workspaceCandidates.Take(2).Count() != 1)
                continue;
            var candidate = workspaceCandidates.Single();
            if (string.IsNullOrWhiteSpace(candidate.WebhookSecretReference))
                continue;
            byte[]? secret = null;
            try
            {
                var protectedSecret = await credentialResolver.ResolveAsync(
                    candidate.WorkspaceId,
                    candidate.WebhookSecretReference,
                    cancellationToken);
                if (!GitHubWebhookSecret.TryParse(protectedSecret, out var webhookCredential) || webhookCredential is null)
                    continue;
                secret = Encoding.UTF8.GetBytes(webhookCredential.Value);
                var result = await verifier.VerifyAsync(new GitHubWebhookVerificationRequest(
                    candidate.WorkspaceId,
                    request.RawBody,
                    request.Signature,
                    request.DeliveryId,
                    request.Event,
                    secret,
                    candidate.InstallationId,
                    candidate.RepositoryProviderId,
                    $"{candidate.RepositoryOwner}/{candidate.RepositoryName}",
                    AllowedEvents), cancellationToken);
                if (result.Succeeded && result.Webhook is not null)
                    authorities.Add((candidate, result.IsReplay));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Workspace authorities are independent. A stale credential or tenant-scoped
                // replay-store failure must not suppress another authority that verifies cleanly.
            }
            finally
            {
                if (secret is not null)
                    CryptographicOperations.ZeroMemory(secret);
            }
        }

        if (authorities.Count == 0)
            throw Rejected();

        var outcomes = new List<string>(authorities.Count);
        Exception? processingFailure = null;
        foreach (var authority in authorities)
        {
            try
            {
                outcomes.Add(await processorRunner.ProcessAsync(
                    authority.Connection,
                    request.DeliveryId,
                    request.Event,
                    request.RawBody,
                    cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                processingFailure ??= exception;
                try
                {
                    await processorRunner.RecordFailureAsync(
                        authority.Connection.WorkspaceId,
                        request.DeliveryId,
                        cancellationToken);
                }
                catch (Exception persistenceException) when (!cancellationToken.IsCancellationRequested)
                {
                    processingFailure = new AggregateException(processingFailure, persistenceException);
                }
            }
        }
        if (processingFailure is not null)
            throw new InvalidOperationException(
                "One or more independently verified healing webhook deliveries failed during durable processing.",
                processingFailure);
        var outcome = outcomes.Distinct(StringComparer.Ordinal).Take(2).Count() == 1
            ? outcomes[0]
            : "processed";
        return new HealingVerifiedWebhookReceipt(
            request.DeliveryId,
            authorities.All(x => x.IsReplay),
            outcome);
    }

    private static bool TryReadUntrustedRouting(ReadOnlyMemory<byte> body, out string installationId, out string repositoryId)
    {
        installationId = string.Empty;
        repositoryId = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            var root = document.RootElement;
            if (!root.TryGetProperty("installation", out var installation) ||
                !installation.TryGetProperty("id", out var installationValue) ||
                !root.TryGetProperty("repository", out var repository) ||
                !repository.TryGetProperty("id", out var repositoryValue))
                return false;
            installationId = ProviderId(installationValue);
            repositoryId = ProviderId(repositoryValue);
            return installationId.Length > 0 && repositoryId.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ProviderId(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.String => value.GetString() ?? string.Empty,
        _ => string.Empty
    } is { Length: > 0 and <= 40 } parsed && parsed.All(char.IsDigit) ? parsed : string.Empty;

    private static HealingWorkflowRequestException Rejected() => new(
        HttpStatusCode.Unauthorized,
        "healing.webhook.verification-failed");

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
