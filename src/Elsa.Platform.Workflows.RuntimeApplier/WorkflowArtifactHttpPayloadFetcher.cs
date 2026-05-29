using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class WorkflowArtifactHttpPayloadFetcher(
    HttpClient httpClient,
    WorkflowArtifactRuntimeOptions options,
    TimeProvider? timeProvider = null,
    IWorkflowArtifactPayloadHostResolver? hostResolver = null) : IWorkflowArtifactPayloadFetcher
{
    private const int BufferSize = 81920;
    private readonly WorkflowArtifactRuntimeOptions _options = ValidateOptions(options);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IWorkflowArtifactPayloadHostResolver _hostResolver = hostResolver ?? DnsWorkflowArtifactPayloadHostResolver.Instance;

    public async Task<WorkflowArtifactPayload> FetchAsync(
        ArtifactPayloadReference reference,
        CancellationToken cancellationToken = default)
    {
        if (_options.AllowedPayloadReferenceProviders is not { Count: > 0 }
            || !_options.AllowedPayloadReferenceProviders.Contains(reference.Provider, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workflow artifact payload reference provider is not approved by this runtime.");
        if (reference.ExpiresAt is not null && reference.ExpiresAt <= _timeProvider.GetUtcNow())
            throw new InvalidOperationException("Workflow artifact payload reference has expired.");
        if (reference.SizeBytes is < 0)
            throw new InvalidOperationException("Workflow artifact payload reference size is invalid.");
        if (reference.SizeBytes is > 0 && reference.SizeBytes > _options.MaxPayloadBytes)
            throw new InvalidOperationException("Workflow artifact payload reference exceeds the configured runtime size limit.");

        var uri = await PayloadUriAsync(reference, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(reference.MediaType))
        {
            if (reference.MediaType.Contains(',') || !MediaTypeWithQualityHeaderValue.TryParse(reference.MediaType, out var mediaType))
                throw new InvalidOperationException("Workflow artifact payload media type is invalid.");
            request.Headers.Accept.Add(mediaType);
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

    private async Task<Uri> PayloadUriAsync(ArtifactPayloadReference reference, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(reference.Uri, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Workflow artifact payload reference URI is invalid.");
        if (uri.Scheme is not "http" and not "https")
            throw new InvalidOperationException("Workflow artifact payload reference scheme is not supported by this runtime.");
        if (_options.AllowedPayloadHosts is not { Count: > 0 } || !_options.AllowedPayloadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workflow artifact payload host is not approved by this runtime.");

        var addresses = IPAddress.TryParse(uri.Host, out var literal)
            ? [literal]
            : await ResolveHostAsync(uri.IdnHost, cancellationToken);
        if (addresses.Count == 0)
            throw new InvalidOperationException("Workflow artifact payload host could not be resolved.");
        if (addresses.Any(IsBlockedAddress))
            throw new InvalidOperationException("Workflow artifact payload host resolves to a non-public address.");

        return uri;
    }

    private async Task<IReadOnlyList<IPAddress>> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await _hostResolver.ResolveAsync(host, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            throw new InvalidOperationException("Workflow artifact payload host could not be resolved.", ex);
        }
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 or 127
                || bytes[0] >= 224
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal
                || address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Any)
                || (bytes[0] & 0xfe) == 0xfc;
        }

        return true;
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

public interface IWorkflowArtifactPayloadHostResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default);
}

public sealed class DnsWorkflowArtifactPayloadHostResolver : IWorkflowArtifactPayloadHostResolver
{
    public static DnsWorkflowArtifactPayloadHostResolver Instance { get; } = new();

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
