using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace ElsaControl.Api.ReleaseCatalog;

/// <summary>
/// Reads one configured OCI repository through the ACR data plane. The authority is
/// server-owned; neither registry URLs nor OAuth challenge values are accepted from a
/// caller. A reader creates short-lived sessions so bearer tokens are never cached
/// globally or persisted.
/// </summary>
internal sealed class AcrReleaseRegistryReader : IReleaseRegistryReader
{
    private const string AcrAudience = "https://containerregistry.azure.net/.default";
    private const int MaximumOAuthResponseBytes = 64 * 1024;
    private const int MaximumReferrerPages = 4;
    private const int MaximumReferrerDescriptors = 64;
    private const int MaximumRedirectUrlLength = 8 * 1024;
    private const int MaximumRepositoryLength = 255;
    private const int MaximumRegistryNameLength = 50;
    private static readonly TimeSpan MaximumRequestTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumRequestTimeout = TimeSpan.FromMilliseconds(100);

    private readonly AcrReleaseRegistryAuthority _authority;
    private readonly TokenCredential _credential;
    private readonly HttpClient _httpClient;

    public AcrReleaseRegistryReader(
        AcrReleaseRegistryAuthority authority,
        TokenCredential credential,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(httpClient);

        ValidateAuthority(authority);
        _authority = Snapshot(authority);
        _credential = credential;
        _httpClient = httpClient;
    }

    public ValueTask<IReleaseRegistrySession> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReleaseRegistrySession session = new Session(_authority, _credential, _httpClient);
        return ValueTask.FromResult(session);
    }

    internal static void ValidateAuthority(AcrReleaseRegistryAuthority authority)
    {
        if (!IsCanonicalRegistryHost(authority.RegistryHost))
            throw InvalidAuthority();
        if (!IsSafeRepository(authority.Repository))
            throw InvalidAuthority();
        if (!IsCanonicalGuid(authority.TenantId) || !IsCanonicalGuid(authority.ManagedIdentityClientId))
            throw InvalidAuthority();
        if (authority.BlobRedirectHosts is null || authority.BlobRedirectHosts.Count > 32)
            throw InvalidAuthority();
        if (authority.BlobRedirectHosts.Any(host => !IsAllowedBlobHost(host)))
            throw InvalidAuthority();
        if (authority.BlobRedirectHosts.Distinct(StringComparer.Ordinal).Count() != authority.BlobRedirectHosts.Count)
            throw InvalidAuthority();
        if (authority.RequestTimeout < MinimumRequestTimeout || authority.RequestTimeout > MaximumRequestTimeout)
            throw InvalidAuthority();
    }

    private static AcrReleaseRegistryAuthority Snapshot(AcrReleaseRegistryAuthority authority) =>
        authority with
        {
            RegistryHost = authority.RegistryHost,
            Repository = authority.Repository,
            TenantId = authority.TenantId,
            ManagedIdentityClientId = authority.ManagedIdentityClientId,
            BlobRedirectHosts = authority.BlobRedirectHosts.ToArray()
        };

    private static bool IsCanonicalRegistryHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumRegistryNameLength + ".azurecr.io".Length)
            return false;
        if (value != value.ToLowerInvariant() || value.EndsWith(".", StringComparison.Ordinal))
            return false;

        var suffix = ".azurecr.io";
        if (!value.EndsWith(suffix, StringComparison.Ordinal))
            return false;
        var name = value[..^suffix.Length];
        return name.Length is >= 5 and <= MaximumRegistryNameLength
            && name[0] != '-' && name[^1] != '-'
            && name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    private static bool IsSafeRepository(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumRepositoryLength ||
            value[0] == '/' || value[^1] == '/' || value.Contains("//", StringComparison.Ordinal))
            return false;

        return value.Split('/').All(segment =>
            segment.Length > 0 && segment.Length <= 128 &&
            segment.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-') &&
            segment is not "." and not ".." &&
            !segment.StartsWith(".", StringComparison.Ordinal) &&
            !segment.EndsWith(".", StringComparison.Ordinal) &&
            !segment.StartsWith("-", StringComparison.Ordinal) &&
            !segment.EndsWith("-", StringComparison.Ordinal));
    }

    private static bool IsCanonicalGuid(string value) =>
        Guid.TryParseExact(value, "D", out var parsed) &&
        string.Equals(value, parsed.ToString("D"), StringComparison.Ordinal);

    private static bool IsAllowedBlobHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.ToLowerInvariant() || value.EndsWith(".", StringComparison.Ordinal) ||
            value.Contains(':') || value.Contains('*') || value.Contains('/') || value.Contains('\\'))
            return false;

        if (value.EndsWith(".blob.core.windows.net", StringComparison.Ordinal))
            return IsDnsHost(value[..^".blob.core.windows.net".Length]);

        const string dataSuffix = ".data.azurecr.io";
        if (!value.EndsWith(dataSuffix, StringComparison.Ordinal))
            return false;
        var prefix = value[..^dataSuffix.Length];
        var separator = prefix.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 && separator < prefix.Length - 1 &&
            IsDnsHost(prefix[..separator]) && IsDnsHost(prefix[(separator + 1)..]);
    }

    private static bool IsDnsHost(string value) =>
        value.Length is >= 1 and <= 63 &&
        value[0] != '-' && value[^1] != '-' &&
        value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static ArgumentException InvalidAuthority() =>
        new("Release registry authority is invalid.");

    private sealed class Session : IReleaseRegistrySession
    {
        private readonly AcrReleaseRegistryAuthority _authority;
        private readonly TokenCredential _credential;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _authenticationLock = new(1, 1);
        private string? _acrAccessToken;
        private int _disposed;

        public Session(AcrReleaseRegistryAuthority authority, TokenCredential credential, HttpClient httpClient)
        {
            _authority = authority;
            _credential = credential;
            _httpClient = httpClient;
        }

        public async ValueTask<byte[]> ReadManifestAsync(string digest, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateDigest(digest);

            using var response = await SendAuthorizedAsync(
                HttpMethod.Get,
                BuildRegistryUri($"/v2/{_authority.Repository}/manifests/{digest}"),
                ReleaseRegistryProtocol.ManifestMediaType,
                allowRedirect: false,
                cancellationToken).ConfigureAwait(false);
            RequireMediaType(response, ReleaseRegistryProtocol.ManifestMediaType);
            var bytes = await ReadBodyAsync(response, ReleaseRegistryProtocol.MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
            if (!DigestMatches(digest, bytes))
                throw Failure();
            return bytes;
        }

        public async ValueTask<byte[]> ReadBlobAsync(string digest, int maximumBytes, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateDigest(digest);
            if (maximumBytes is <= 0 or > ReleaseRegistryProtocol.MaximumManifestBytes)
                throw Failure();

            Uri redirect;
            using (var response = await SendAuthorizedAsync(
                HttpMethod.Get,
                BuildRegistryUri($"/v2/{_authority.Repository}/blobs/{digest}"),
                accept: null,
                allowRedirect: true,
                cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect)
                    redirect = ValidateBlobRedirect(response.Headers.Location);
                else
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw Failure();
                    var bytes = await ReadBodyAsync(response, maximumBytes, cancellationToken).ConfigureAwait(false);
                    if (!DigestMatches(digest, bytes))
                        throw Failure();
                    return bytes;
                }
            }

            // Release the registry response and its timeout before contacting the approved
            // blob host. The credential-bearing response has one explicit ownership scope.
            using var redirectedResponse = await SendUnauthenticatedAsync(redirect, cancellationToken).ConfigureAwait(false);
            if (redirectedResponse.StatusCode != HttpStatusCode.OK)
                throw Failure();
            var redirectedBytes = await ReadBodyAsync(redirectedResponse, maximumBytes, cancellationToken).ConfigureAwait(false);
            if (!DigestMatches(digest, redirectedBytes))
                throw Failure();
            return redirectedBytes;
        }

        public async ValueTask<IReadOnlyList<ReleaseRegistryDescriptor>> ReadReferrersAsync(
            string subjectDigest,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateDigest(subjectDigest);

            var descriptors = new List<ReleaseRegistryDescriptor>();
            var initialPath = $"/v2/{_authority.Repository}/referrers/{subjectDigest}";
            var nextUri = BuildRegistryUri(initialPath + "?artifactType=" + Uri.EscapeDataString(ReleaseRegistryProtocol.BundleMediaType));
            var visited = new HashSet<string>(StringComparer.Ordinal) { nextUri.AbsoluteUri };

            for (var page = 0; page < MaximumReferrerPages; page++)
            {
                using var response = await SendAuthorizedAsync(
                    HttpMethod.Get,
                    nextUri,
                    ReleaseRegistryProtocol.IndexMediaType,
                    allowRedirect: false,
                    cancellationToken).ConfigureAwait(false);
                RequireMediaType(response, ReleaseRegistryProtocol.IndexMediaType);
                var body = await ReadBodyAsync(response, ReleaseRegistryProtocol.MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
                ParseReferrerPage(body, descriptors);
                if (descriptors.Count > MaximumReferrerDescriptors)
                    throw Failure();

                var link = response.Headers.TryGetValues("Link", out var linkValues)
                    ? FindNextLink(linkValues)
                    : null;
                if (link is null)
                    return descriptors;

                nextUri = ValidateNextLink(link, initialPath);
                if (!visited.Add(nextUri.AbsoluteUri))
                    throw Failure();
            }

            throw Failure();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _acrAccessToken = null;
                _authenticationLock.Dispose();
            }

            return ValueTask.CompletedTask;
        }

        private async Task<ResponseLease> SendAuthorizedAsync(
            HttpMethod method,
            Uri uri,
            string? accept,
            bool allowRedirect,
            CancellationToken cancellationToken)
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (accept is not null)
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            return await SendAsync(request, allowRedirect, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ResponseLease> SendUnauthenticatedAsync(Uri uri, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            return await SendAsync(request, allowRedirect: false, cancellationToken).ConfigureAwait(false);
        }

        private async Task<ResponseLease> SendAsync(HttpRequestMessage request, bool allowRedirect, CancellationToken cancellationToken)
        {
            // The blob Location query may carry a SAS credential. Do not emit HTTP
            // spans or captured headers, even if global URL redaction is disabled.
            using var telemetry = OpenTelemetry.SuppressInstrumentationScope.Begin();
            var timeout = CreateTimeout(cancellationToken);
            try
            {
                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                if (!allowRedirect && (int)response.StatusCode is >= 300 and < 400)
                {
                    response.Dispose();
                    timeout.Dispose();
                    throw Failure();
                }

                if (response.StatusCode != HttpStatusCode.OK &&
                    (!allowRedirect || response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect)))
                {
                    response.Dispose();
                    timeout.Dispose();
                    throw Failure();
                }

                return new ResponseLease(response, timeout);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                timeout.Dispose();
                throw;
            }
            catch (OperationCanceledException)
            {
                timeout.Dispose();
                throw Failure();
            }
            catch (ReleaseRegistryReadException)
            {
                timeout.Dispose();
                throw;
            }
            catch (Exception)
            {
                timeout.Dispose();
                throw Failure();
            }
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (_acrAccessToken is not null)
                return _acrAccessToken;

            using var authenticationTimeout = CreateTimeout(cancellationToken);
            var lockTaken = false;
            try
            {
                await _authenticationLock.WaitAsync(authenticationTimeout.Token).ConfigureAwait(false);
                lockTaken = true;
                if (_acrAccessToken is not null)
                    return _acrAccessToken;

                AccessToken aadToken;
                try
                {
                    aadToken = await _credential.GetTokenAsync(
                        new TokenRequestContext([AcrAudience]),
                        authenticationTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw Failure();
                }
                catch (Exception)
                {
                    throw Failure();
                }

                if (string.IsNullOrWhiteSpace(aadToken.Token) || aadToken.Token.Length > MaximumOAuthResponseBytes)
                    throw Failure();

                var refreshToken = await ExchangeAadTokenAsync(aadToken.Token, authenticationTimeout.Token).ConfigureAwait(false);
                _acrAccessToken = await ExchangeRefreshTokenAsync(refreshToken, authenticationTimeout.Token).ConfigureAwait(false);
                return _acrAccessToken;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw Failure();
            }
            finally
            {
                if (lockTaken)
                    _authenticationLock.Release();
            }
        }

        private async Task<string> ExchangeAadTokenAsync(string aadToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildRegistryUri("/oauth2/exchange"))
            {
                Content = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("grant_type", "access_token"),
                    new KeyValuePair<string, string>("service", _authority.RegistryHost),
                    new KeyValuePair<string, string>("tenant", _authority.TenantId),
                    new KeyValuePair<string, string>("access_token", aadToken)])
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await SendAsync(request, allowRedirect: false, cancellationToken).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, MaximumOAuthResponseBytes, cancellationToken).ConfigureAwait(false);
            return ReadToken(body, "refresh_token");
        }

        private async Task<string> ExchangeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildRegistryUri("/oauth2/token"))
            {
                Content = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("service", _authority.RegistryHost),
                    new KeyValuePair<string, string>("scope", $"repository:{_authority.Repository}:pull"),
                    new KeyValuePair<string, string>("refresh_token", refreshToken)])
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await SendAsync(request, allowRedirect: false, cancellationToken).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, MaximumOAuthResponseBytes, cancellationToken).ConfigureAwait(false);
            return ReadToken(body, "access_token");
        }

        private Uri BuildRegistryUri(string pathAndQuery) =>
            new($"https://{_authority.RegistryHost}{pathAndQuery}", UriKind.Absolute);

        private Uri ValidateBlobRedirect(Uri? location)
        {
            if (location is null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(location.UserInfo) || !string.IsNullOrEmpty(location.Fragment) ||
                (location.Port != -1 && location.Port != 443) || location.AbsoluteUri.Length > MaximumRedirectUrlLength ||
                !_authority.BlobRedirectHosts.Contains(location.Host, StringComparer.Ordinal))
                throw Failure();
            return location;
        }

        private Uri ValidateNextLink(string link, string expectedPath)
        {
            if (!Uri.TryCreate(link, UriKind.RelativeOrAbsolute, out var parsed))
                throw Failure();
            var absolute = parsed.IsAbsoluteUri ? parsed : new Uri(BuildRegistryUri("/"), parsed);
            if (absolute.Scheme != Uri.UriSchemeHttps || absolute.Host != _authority.RegistryHost ||
                (absolute.Port != -1 && absolute.Port != 443) || !string.IsNullOrEmpty(absolute.UserInfo) ||
                !string.IsNullOrEmpty(absolute.Fragment) || absolute.AbsolutePath != expectedPath ||
                absolute.AbsoluteUri.Length > MaximumRedirectUrlLength)
                throw Failure();
            return absolute;
        }

        private static string? FindNextLink(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                var cursor = 0;
                while (cursor < value.Length)
                {
                    var start = value.IndexOf('<', cursor);
                    if (start < 0)
                        break;
                    var end = value.IndexOf('>', start + 1);
                    if (end < 0)
                        throw Failure();
                    var parametersEnd = value.IndexOf(',', end + 1);
                    if (parametersEnd < 0)
                        parametersEnd = value.Length;
                    var parameters = value[(end + 1)..parametersEnd];
                    if (HasNextRelation(parameters))
                        return value[(start + 1)..end];
                    cursor = parametersEnd + 1;
                }
            }

            return null;
        }

        private static bool HasNextRelation(string parameters)
        {
            foreach (var parameter in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = parameter.IndexOf('=');
                if (separator <= 0 || !parameter[..separator].Equals("rel", StringComparison.OrdinalIgnoreCase))
                    continue;
                var relation = parameter[(separator + 1)..].Trim().Trim('"');
                if (relation.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals("next", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private static void ParseReferrerPage(
            byte[] body,
            List<ReleaseRegistryDescriptor> descriptors)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("mediaType", out var mediaType) ||
                    mediaType.ValueKind != JsonValueKind.String ||
                    mediaType.GetString() != ReleaseRegistryProtocol.IndexMediaType ||
                    !root.TryGetProperty("manifests", out var manifests) ||
                    manifests.ValueKind != JsonValueKind.Array)
                    throw Failure();

                foreach (var item in manifests.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("mediaType", out var itemMediaType) ||
                        itemMediaType.ValueKind != JsonValueKind.String ||
                        !item.TryGetProperty("digest", out var digest) ||
                        digest.ValueKind != JsonValueKind.String ||
                        !ReleaseRegistryProtocol.IsDigest(digest.GetString()) ||
                        !item.TryGetProperty("size", out var size) ||
                        size.ValueKind != JsonValueKind.Number ||
                        !size.TryGetInt64(out var sizeValue) ||
                        sizeValue < 0 || sizeValue > ReleaseRegistryProtocol.MaximumManifestBytes)
                        throw Failure();

                    var descriptorMediaType = itemMediaType.GetString()!;
                    if (descriptorMediaType is not (ReleaseRegistryProtocol.ManifestMediaType or ReleaseRegistryProtocol.IndexMediaType))
                        throw Failure();

                    string? artifactType = null;
                    if (item.TryGetProperty("artifactType", out var artifactTypeElement))
                    {
                        if (artifactTypeElement.ValueKind != JsonValueKind.String)
                            throw Failure();
                        artifactType = artifactTypeElement.GetString();
                    }

                    descriptors.Add(new(descriptorMediaType, digest.GetString()!, sizeValue, artifactType));
                }
            }
            catch (ReleaseRegistryReadException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw Failure();
            }
        }

        private static async Task<byte[]> ReadBodyAsync(
            ResponseLease response,
            int maximumBytes,
            CancellationToken callerToken)
        {
            if (response.Content.Headers.ContentLength is > int.MaxValue or < 0 ||
                response.Content.Headers.ContentLength > maximumBytes)
                throw Failure();

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(response.Token).ConfigureAwait(false);
                using var buffer = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
                var chunk = new byte[Math.Min(16 * 1024, maximumBytes)];
                while (true)
                {
                    var read = await stream.ReadAsync(chunk.AsMemory(), response.Token).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    if (buffer.Length > maximumBytes - read)
                        throw Failure();
                    buffer.Write(chunk, 0, read);
                }

                return buffer.ToArray();
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ReleaseRegistryReadException)
            {
                throw;
            }
            catch (Exception)
            {
                throw Failure();
            }
        }

        private static string ReadToken(byte[] body, string propertyName)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty(propertyName, out var value) ||
                    value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) ||
                    value.GetString()!.Length > MaximumOAuthResponseBytes)
                    throw Failure();
                return value.GetString()!;
            }
            catch (ReleaseRegistryReadException)
            {
                throw;
            }
            catch (JsonException)
            {
                throw Failure();
            }
        }

        private static bool DigestMatches(string expectedDigest, byte[] bytes) =>
            string.Equals(expectedDigest, "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), StringComparison.Ordinal);

        private static void RequireMediaType(ResponseLease response, string expected)
        {
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, expected, StringComparison.Ordinal))
                throw Failure();
        }

        private static void ValidateDigest(string digest)
        {
            if (!ReleaseRegistryProtocol.IsDigest(digest))
                throw Failure();
        }

        private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_authority.RequestTimeout);
            return timeout;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw Failure();
        }

        private static ReleaseRegistryReadException Failure() =>
            new("Release registry read failed.");

        private sealed class ResponseLease : IDisposable
        {
            private readonly CancellationTokenSource _timeout;
            public ResponseLease(HttpResponseMessage response, CancellationTokenSource timeout)
            {
                Response = response;
                _timeout = timeout;
            }

            public HttpResponseMessage Response { get; }
            public HttpStatusCode StatusCode => Response.StatusCode;
            public HttpResponseHeaders Headers => Response.Headers;
            public HttpContent Content => Response.Content;
            public CancellationToken Token => _timeout.Token;

            public void Dispose()
            {
                Response.Dispose();
                _timeout.Dispose();
            }
        }
    }
}

internal sealed class ReleaseRegistryReadException : InvalidOperationException
{
    public ReleaseRegistryReadException(string message) : base(message) { }
}
