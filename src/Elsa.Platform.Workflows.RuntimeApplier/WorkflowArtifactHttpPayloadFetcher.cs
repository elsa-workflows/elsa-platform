using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Elsa.Platform.Deployment.Artifacts;

namespace Elsa.Platform.Workflows.RuntimeApplier;

public sealed class WorkflowArtifactHttpPayloadFetcher : IWorkflowArtifactPayloadFetcher, IDisposable
{
    private const int BufferSize = 81920;
    internal static readonly HttpRequestOptionsKey<IPAddress> ValidatedPayloadAddressKey = new("Elsa.WorkflowArtifact.ValidatedPayloadAddress");
    private readonly WorkflowArtifactRuntimeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowArtifactPayloadHostResolver _hostResolver;
    private readonly HttpMessageInvoker _httpClient;
    private readonly bool _ownsHttpClient;

    public WorkflowArtifactHttpPayloadFetcher(
        WorkflowArtifactRuntimeOptions options,
        TimeProvider? timeProvider = null,
        IWorkflowArtifactPayloadHostResolver? hostResolver = null)
        : this(options, timeProvider, hostResolver, CreateDefaultHttpClient(), true)
    {
    }

    internal WorkflowArtifactHttpPayloadFetcher(
        WorkflowArtifactRuntimeOptions options,
        TimeProvider? timeProvider,
        IWorkflowArtifactPayloadHostResolver? hostResolver,
        HttpMessageInvoker httpClient,
        bool ownsHttpClient = false)
    {
        _options = ValidateOptions(options);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _hostResolver = hostResolver ?? DnsWorkflowArtifactPayloadHostResolver.Instance;
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

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

        var endpoint = await PayloadEndpointAsync(reference, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Uri);
        request.Options.Set(ValidatedPayloadAddressKey, endpoint.Address);
        string? declaredMediaType = null;
        if (!string.IsNullOrWhiteSpace(reference.MediaType))
        {
            if (reference.MediaType.Contains(',')
                || !MediaTypeWithQualityHeaderValue.TryParse(reference.MediaType, out var mediaType)
                || mediaType.MediaType is null
                || mediaType.MediaType.Contains('*'))
                throw new InvalidOperationException("Workflow artifact payload media type is invalid.");
            request.Headers.Accept.Add(mediaType);
            declaredMediaType = mediaType.MediaType;
        }

        using var response = await SendAsync(request, cancellationToken);
        if (response.RequestMessage?.RequestUri is { } actualUri && actualUri != endpoint.Uri)
            throw new InvalidOperationException("Workflow artifact payload redirects are not supported.");
        if (IsRedirect(response.StatusCode))
            throw new InvalidOperationException("Workflow artifact payload redirects are not supported.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Workflow artifact payload request failed with status {(int)response.StatusCode}.");

        var content = await ReadBoundedAsync(response.Content, cancellationToken);
        return new WorkflowArtifactPayload(
            reference,
            content,
            declaredMediaType ?? response.Content.Headers.ContentType?.MediaType);
    }

    private static WorkflowArtifactRuntimeOptions ValidateOptions(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        return options;
    }

    private static HttpMessageInvoker CreateDefaultHttpClient() =>
        new(CreateDefaultHttpHandler());

    internal static SocketsHttpHandler CreateDefaultHttpHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectToValidatedAddressAsync
        };

    private async Task<ValidatedPayloadEndpoint> PayloadEndpointAsync(ArtifactPayloadReference reference, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(reference.Uri, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Workflow artifact payload reference URI is invalid.");
        if (uri.Scheme is not "http" and not "https")
            throw new InvalidOperationException("Workflow artifact payload reference scheme is not supported by this runtime.");
        var host = uri.Host.Trim('[', ']');
        if (_options.AllowedPayloadHosts is not { Count: > 0 }
            || !_options.AllowedPayloadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
                && !_options.AllowedPayloadHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workflow artifact payload host is not approved by this runtime.");

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await ResolveHostAsync(uri.IdnHost, cancellationToken);
        if (addresses.Count == 0)
            throw new InvalidOperationException("Workflow artifact payload host could not be resolved.");
        if (addresses.Any(IsBlockedAddress))
            throw new InvalidOperationException("Workflow artifact payload host resolves to a non-public address.");

        return new ValidatedPayloadEndpoint(uri, addresses[0]);
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
        if (address.IsIPv4MappedToIPv6)
            return IsBlockedAddress(address.MapToIPv4());
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 or 127
                || bytes[0] >= 224
                || bytes[0] == 100 && bytes[1] is >= 64 and <= 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2
                || bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100)
                || bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113;
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

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _httpClient is HttpClient client
            ? await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            : await _httpClient.SendAsync(request, cancellationToken);

    internal static async ValueTask<Stream> ConnectToValidatedAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        if (!context.InitialRequestMessage.Options.TryGetValue(ValidatedPayloadAddressKey, out var address))
            throw new InvalidOperationException("Workflow artifact payload host has not been validated.");

        return await ConnectToValidatedAddressAsync(context.DnsEndPoint, address, cancellationToken);
    }

    internal static async ValueTask<Stream> ConnectToValidatedAddressAsync(
        DnsEndPoint endpoint,
        IPAddress address,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MultipleChoices
            or HttpStatusCode.Moved
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

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

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed record ValidatedPayloadEndpoint(Uri Uri, IPAddress Address);
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
