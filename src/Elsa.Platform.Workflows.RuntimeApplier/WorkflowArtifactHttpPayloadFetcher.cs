using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class WorkflowArtifactHttpPayloadFetcher(
    HttpClient httpClient,
    WorkflowArtifactRuntimeOptions options,
    TimeProvider? timeProvider = null) : IWorkflowArtifactPayloadFetcher
{
    private const int BufferSize = 81920;
    private readonly WorkflowArtifactRuntimeOptions _options = ValidateOptions(options);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<WorkflowArtifactPayload> FetchAsync(
        ArtifactPayloadReference reference,
        CancellationToken cancellationToken = default)
    {
        if (reference.ExpiresAt is not null && reference.ExpiresAt <= _timeProvider.GetUtcNow())
            throw new InvalidOperationException("Workflow artifact payload reference has expired.");
        if (reference.SizeBytes is > 0 && reference.SizeBytes > _options.MaxPayloadBytes)
            throw new InvalidOperationException("Workflow artifact payload reference exceeds the configured runtime size limit.");

        var uri = PayloadUri(reference);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(reference.MediaType))
        {
            if (!request.Headers.Accept.TryParseAdd(reference.MediaType))
                throw new InvalidOperationException("Workflow artifact payload media type is invalid.");
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Workflow artifact payload request failed with status {(int)response.StatusCode}.");

        var content = await ReadBoundedAsync(response.Content, cancellationToken);
        return new WorkflowArtifactPayload(
            reference,
            content,
            reference.MediaType ?? response.Content.Headers.ContentType?.MediaType);
    }

    private static WorkflowArtifactRuntimeOptions ValidateOptions(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        return options;
    }

    private static Uri PayloadUri(ArtifactPayloadReference reference)
    {
        if (!Uri.TryCreate(reference.Uri, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Workflow artifact payload reference URI is invalid.");
        if (uri.Scheme is not "http" and not "https")
            throw new InvalidOperationException("Workflow artifact payload reference scheme is not supported by this runtime.");

        return uri;
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > _options.MaxPayloadBytes)
            throw new InvalidOperationException("Workflow artifact payload exceeds the configured runtime size limit.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[BufferSize];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return output.ToArray();

            total += read;
            if (total > _options.MaxPayloadBytes)
                throw new InvalidOperationException("Workflow artifact payload exceeds the configured runtime size limit.");

            output.Write(buffer, 0, read);
        }
    }
}
