using System.Text;

namespace ElsaControl.Deployment.Core.Workspace;

public sealed class DeploymentWebhookDispatchService(
    IWorkspaceDeploymentCommandStore store,
    IDeploymentWebhookSender sender,
    DeploymentWebhookDispatchOptions? options = null,
    TimeProvider? timeProvider = null)
{
    private readonly DeploymentWebhookDispatchOptions _options = options ?? new DeploymentWebhookDispatchOptions();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return 0;

        var now = _timeProvider.GetUtcNow();
        var targets = await store.ListPendingWebhookNotificationTargetsAsync(
            Math.Clamp(_options.BatchSize, 1, 100),
            now,
            cancellationToken);
        var processed = 0;

        foreach (var target in targets)
        {
            if (!TryBuildEndpoint(target.EngineBaseUrl, out var endpoint))
            {
                await store.MarkWebhookNotificationSkippedAsync(target.WorkspaceId, target.Id, _timeProvider.GetUtcNow(), cancellationToken);
                processed++;
                continue;
            }

            var result = await sender.SendAsync(new DeploymentWebhookDispatchRequest(target, endpoint), cancellationToken);
            var completedAt = _timeProvider.GetUtcNow();
            switch (result.Status)
            {
                case DeploymentWebhookDispatchResultStatus.Sent:
                    await store.MarkWebhookNotificationSentAsync(target.WorkspaceId, target.Id, completedAt, cancellationToken);
                    break;
                case DeploymentWebhookDispatchResultStatus.Skipped:
                    await store.MarkWebhookNotificationSkippedAsync(target.WorkspaceId, target.Id, completedAt, cancellationToken);
                    break;
                default:
                    await store.MarkWebhookNotificationFailedAsync(target.WorkspaceId, target.Id, completedAt, cancellationToken);
                    break;
            }

            processed++;
        }

        return processed;
    }

    private bool TryBuildEndpoint(string? engineBaseUrl, out Uri endpoint)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(engineBaseUrl))
            return false;
        if (!Uri.TryCreate(engineBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return false;
        if (baseUri.Scheme is not ("http" or "https"))
            return false;

        var path = string.IsNullOrWhiteSpace(_options.NotificationPath)
            ? new DeploymentWebhookDispatchOptions().NotificationPath
            : _options.NotificationPath;
        if (!Uri.TryCreate(baseUri, path.TrimStart('/'), out var resolved))
            return false;

        endpoint = resolved;
        return true;
    }
}

public sealed class HttpDeploymentWebhookSender(HttpClient httpClient) : IDeploymentWebhookSender
{
    public async Task<DeploymentWebhookDispatchResult> SendAsync(
        DeploymentWebhookDispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, request.Endpoint)
            {
                Content = new StringContent(request.Target.SafePayloadJson, Encoding.UTF8, "application/json")
            };
            message.Headers.Add("X-Elsa-Control-Webhook", "deployment-command-available");
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode
                ? DeploymentWebhookDispatchResult.Sent()
                : DeploymentWebhookDispatchResult.Failed($"Runtime webhook endpoint returned HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException)
        {
            return DeploymentWebhookDispatchResult.Failed("Runtime webhook endpoint did not accept the notification.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DeploymentWebhookDispatchResult.Failed("Runtime webhook endpoint did not accept the notification.");
        }
    }
}
