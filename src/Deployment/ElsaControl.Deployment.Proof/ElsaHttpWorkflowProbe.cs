using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Proof;

/// <summary>
/// Options for the opt-in live workflow smoke test. Credential values are accepted only by the
/// in-process host and are never copied into a proof model or returned as evidence.
/// </summary>
public sealed class ElsaHttpWorkflowProbeOptions
{
    public ElsaHttpWorkflowProbeOptions(
        string username,
        string workflowDefinitionId = "elsa-control-disposable-proof",
        TimeSpan? requestTimeout = null,
        TimeSpan? workflowTimeout = null,
        TimeSpan? pollInterval = null)
    {
        Username = RequireCredential(username, nameof(username));
        WorkflowDefinitionId = ValidateIdentifier(workflowDefinitionId, nameof(workflowDefinitionId));
        RequestTimeout = ValidateTimeout(requestTimeout ?? TimeSpan.FromSeconds(30), nameof(requestTimeout));
        WorkflowTimeout = ValidateTimeout(workflowTimeout ?? TimeSpan.FromMinutes(2), nameof(workflowTimeout));
        PollInterval = ValidateTimeout(pollInterval ?? TimeSpan.FromSeconds(2), nameof(pollInterval));
    }

    /// <summary>
    /// The local runtime username. It is used only while making the login request.
    /// </summary>
    public string Username { get; }

    /// <summary>
    /// A deterministic, caller-owned logical definition ID for the disposable smoke workflow.
    /// </summary>
    public string WorkflowDefinitionId { get; }

    public TimeSpan RequestTimeout { get; }

    public TimeSpan WorkflowTimeout { get; }

    public TimeSpan PollInterval { get; }

    private static string RequireCredential(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A runtime credential is required.", parameterName)
            : value;

    private static string ValidateIdentifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("The workflow definition identifier is invalid.", parameterName);

        return value;
    }

    private static TimeSpan ValidateTimeout(TimeSpan value, string parameterName) =>
        value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan
            ? throw new ArgumentOutOfRangeException(parameterName, "The timeout must be positive and finite.")
            : value;
}

/// <summary>Provides one short-lived password lease without placing credential material in proof configuration.</summary>
public interface IElsaProofCredentialSource
{
    ValueTask<AzureSecretLease> ResolvePasswordAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs the bounded, HTTP-only Elsa 3.8 Combined workflow smoke test used by the disposable
/// proof host. The implementation intentionally parses only the fields needed for safe evidence;
/// response bodies and bearer tokens do not cross the probe boundary.
/// </summary>
public sealed class ElsaHttpWorkflowProbe : IAzureProviderProofWorkflowProbe, IDisposable
{
    private const string ApiPrefix = "/elsa/api";
    private const string WorkflowInstanceHeader = "x-elsa-workflow-instance-id";
    private const int MaximumResponseBytes = 65_536;
    private readonly HttpClient httpClient;
    private readonly ElsaHttpWorkflowProbeOptions options;
    private readonly IElsaProofCredentialSource credentialSource;
    private readonly bool ownsHttpClient;

    public ElsaHttpWorkflowProbe(
        ElsaHttpWorkflowProbeOptions options,
        IElsaProofCredentialSource credentialSource)
        : this(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }), options, credentialSource, true)
    {
    }

    internal ElsaHttpWorkflowProbe(
        HttpClient httpClient,
        ElsaHttpWorkflowProbeOptions options,
        IElsaProofCredentialSource credentialSource,
        bool ownsHttpClient = false)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.credentialSource = credentialSource ?? throw new ArgumentNullException(nameof(credentialSource));
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<DeploymentProofWorkflow> RunAsync(
        string endpoint,
        DeploymentProofEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!string.Equals(environment.Provider, "azure", StringComparison.OrdinalIgnoreCase) ||
            !AzureWorkloadPlanTranslator.IsSupportedLocation(environment.Region))
            throw Failure("azure.proof.workflow.environmentInvalid", "The workflow proof environment is invalid.");

        var baseUri = ValidateEndpoint(endpoint, environment.Name);
        using var workflowTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        workflowTimeout.CancelAfter(options.WorkflowTimeout);

        try
        {
            await GetProbeEndpointAsync(baseUri, "/alive", workflowTimeout.Token);
            await GetProbeEndpointAsync(baseUri, "/health", workflowTimeout.Token);

            var token = await LoginAsync(baseUri, workflowTimeout.Token);
            await SaveDraftAsync(baseUri, token, workflowTimeout.Token);
            await PublishAsync(baseUri, token, workflowTimeout.Token);
            await VerifyPublishedAsync(baseUri, token, workflowTimeout.Token);

            var workflowId = await ExecuteAsync(baseUri, token, workflowTimeout.Token);
            return await PollInstanceAsync(baseUri, token, workflowId, workflowTimeout.Token);
        }
        catch (DeploymentProofStageException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Failure("azure.proof.workflow.timeout", "The Elsa workflow proof exceeded its bounded timeout.");
        }
        catch (Exception)
        {
            throw Failure("azure.proof.workflow.failed", "The Elsa workflow proof could not be completed.");
        }
    }

    private async Task GetProbeEndpointAsync(Uri baseUri, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Failure("azure.proof.workflow.endpointUnhealthy", "The Elsa runtime endpoint did not report success.");
    }

    private async Task<string> LoginAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        await using var password = await credentialSource.ResolvePasswordAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"{ApiPrefix}/identity/login"))
        {
            Content = new LoginJsonContent(options.Username, password)
        };
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Failure("azure.proof.workflow.loginFailed", "Elsa runtime authentication failed.");

        using var document = await ParseJsonAsync(response, cancellationToken);
        var isAuthenticated = ReadBoolean(document.RootElement, "isAuthenticated");
        var token = ReadString(document.RootElement, "accessToken");
        if (isAuthenticated != true || !IsSafeToken(token))
            throw Failure("azure.proof.workflow.loginFailed", "Elsa runtime authentication failed.");

        return token!;
    }

    private async Task SaveDraftAsync(Uri baseUri, string token, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, $"{ApiPrefix}/workflow-definitions"),
            new
            {
                model = new
                {
                    definitionId = options.WorkflowDefinitionId,
                    name = "Elsa Control disposable proof",
                    description = "Deterministic empty workflow used by the opt-in deployment proof.",
                    root = (object?)null
                },
                publish = false
            },
            token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Failure("azure.proof.workflow.createFailed", "Elsa could not create the disposable workflow.");

        using var document = await ParseJsonAsync(response, cancellationToken);
        var definition = FindProperty(document.RootElement, "workflowDefinition");
        var definitionId = ReadString(definition, "definitionId");
        if (!string.Equals(definitionId, options.WorkflowDefinitionId, StringComparison.Ordinal))
            throw Failure("azure.proof.workflow.createFailed", "Elsa returned an unexpected disposable workflow identity.");
    }

    private async Task PublishAsync(Uri baseUri, string token, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, $"{ApiPrefix}/workflow-definitions/{Uri.EscapeDataString(options.WorkflowDefinitionId)}/publish"),
            new { },
            token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Failure("azure.proof.workflow.publishFailed", "Elsa could not publish the disposable workflow.");

        using var document = await ParseJsonAsync(response, cancellationToken);
        var definition = FindProperty(document.RootElement, "workflowDefinition");
        var definitionId = ReadString(definition, "definitionId");
        var published = ReadBoolean(definition, "isPublished");
        if (!string.Equals(definitionId, options.WorkflowDefinitionId, StringComparison.Ordinal) || published != true)
            throw Failure("azure.proof.workflow.publishFailed", "Elsa did not confirm publication of the disposable workflow.");
    }

    private async Task VerifyPublishedAsync(Uri baseUri, string token, CancellationToken cancellationToken)
    {
        var path = $"{ApiPrefix}/workflow-definitions/by-definition-id/{Uri.EscapeDataString(options.WorkflowDefinitionId)}?versionOptions=Published";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Failure("azure.proof.workflow.publishedMissing", "Elsa did not return the published disposable workflow.");

        using var document = await ParseJsonAsync(response, cancellationToken);
        var definitionId = ReadString(document.RootElement, "definitionId");
        var published = ReadBoolean(document.RootElement, "isPublished");
        if (!string.Equals(definitionId, options.WorkflowDefinitionId, StringComparison.Ordinal) || published != true)
            throw Failure("azure.proof.workflow.publishedMissing", "Elsa did not return the published disposable workflow.");
    }

    private async Task<string> ExecuteAsync(Uri baseUri, string token, CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, $"{ApiPrefix}/workflow-definitions/{Uri.EscapeDataString(options.WorkflowDefinitionId)}/execute"),
            new { versionOptions = "Published" },
            token);
        using var response = await SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw Failure("azure.proof.workflow.executeFailed", "Elsa could not execute the published disposable workflow.");

        if (!response.Headers.TryGetValues(WorkflowInstanceHeader, out var values))
            throw Failure("azure.proof.workflow.instanceHeaderMissing", "Elsa did not return a workflow instance identity.");

        var workflowId = values.SingleOrDefault();
        if (!IsSafeIdentifier(workflowId))
            throw Failure("azure.proof.workflow.instanceHeaderInvalid", "Elsa returned an invalid workflow instance identity.");

        return workflowId!;
    }

    private async Task<DeploymentProofWorkflow> PollInstanceAsync(
        Uri baseUri,
        string token,
        string workflowId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(baseUri, $"{ApiPrefix}/workflow-instances/{Uri.EscapeDataString(workflowId)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw Failure("azure.proof.workflow.instanceReadFailed", "Elsa did not return the workflow instance state.");

            using var document = await ParseJsonAsync(response, cancellationToken);
            var state = ReadWorkflowState(document.RootElement);
            if (state.Status is null)
                throw Failure("azure.proof.workflow.stateInvalid", "Elsa returned an invalid workflow instance state.");

            if (string.Equals(state.Status, "Finished", StringComparison.OrdinalIgnoreCase))
            {
                var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["status"] = "Finished",
                    ["incidentCount"] = state.IncidentCount.ToString(CultureInfo.InvariantCulture)
                };
                if (state.FinishedAt is not null)
                    evidence["finishedAt"] = state.FinishedAt.Value.ToString("O", CultureInfo.InvariantCulture);

                if (state.IncidentCount != 0)
                    return new(workflowId, false, "FinishedWithIncidents", evidence);
                if (state.FinishedAt is null)
                    return new(workflowId, false, "FinishedWithoutTimestamp", evidence);

                return new(workflowId, true, "Finished", evidence);
            }

            if (!string.Equals(state.Status, "Running", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    workflowId,
                    false,
                    "TerminalFailure",
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = state.Status });
            }

            await Task.Delay(options.PollInterval, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(options.RequestTimeout);
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                response.Dispose();
                throw Failure("azure.proof.workflow.redirectRejected", "Elsa returned an unsafe redirect response.");
            }
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("azure.proof.workflow.timeout", "The Elsa workflow proof exceeded its bounded timeout.");
        }
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, Uri uri, object body, string? token = null)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class LoginJsonContent : HttpContent
    {
        private readonly string username;
        private readonly AzureSecretLease password;

        public LoginJsonContent(string username, AzureSecretLease password)
        {
            this.username = username;
            this.password = password;
            Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            using var buffer = new ZeroingPooledBufferWriter();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("username", username);
                writer.WriteString("password", password.Value.Span);
                writer.WriteEndObject();
            }

            await stream.WriteAsync(buffer.WrittenMemory, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

    }

    private sealed class ZeroingPooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[]? buffer = ArrayPool<byte>.Shared.Rent(256);
        private int written;

        public ReadOnlyMemory<byte> WrittenMemory =>
            (buffer ?? throw new ObjectDisposedException(nameof(ZeroingPooledBufferWriter))).AsMemory(0, written);

        public void Advance(int count)
        {
            var current = buffer ?? throw new ObjectDisposedException(nameof(ZeroingPooledBufferWriter));
            if (count < 0 || written > current.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer!.AsMemory(written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return buffer.AsSpan(written);
        }

        public void Dispose()
        {
            var rented = Interlocked.Exchange(ref buffer, null);
            if (rented is null)
                return;
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
            written = 0;
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            if (sizeHint == 0)
                sizeHint = 1;

            var current = buffer ?? throw new ObjectDisposedException(nameof(ZeroingPooledBufferWriter));
            if (sizeHint <= current.Length - written)
                return;

            var required = checked(written + sizeHint);
            var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(required, checked(current.Length * 2)));
            current.AsSpan(0, written).CopyTo(replacement);
            CryptographicOperations.ZeroMemory(current);
            ArrayPool<byte>.Shared.Return(current);
            buffer = replacement;
        }
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
                throw Failure("azure.proof.workflow.responseTooLarge", "Elsa returned an oversized workflow response.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var bounded = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                if (bounded.Length + read > MaximumResponseBytes)
                    throw Failure("azure.proof.workflow.responseTooLarge", "Elsa returned an oversized workflow response.");
                bounded.Write(buffer, 0, read);
            }
            bounded.Position = 0;
            return await JsonDocument.ParseAsync(bounded, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw Failure("azure.proof.workflow.responseInvalid", "Elsa returned an invalid workflow response.");
        }
    }

    private static WorkflowInstanceState ReadWorkflowState(JsonElement root)
    {
        var state = FindProperty(root, "workflowState");
        if (state.ValueKind == JsonValueKind.Undefined)
            state = root;

        var status = ReadStatus(root) ?? ReadStatus(state);
        var incidentCount = ReadInt32(root, "incidentCount") ??
                            ReadInt32(state, "incidentCount") ??
                            CountArray(state, "incidents");
        var finishedAt = ReadDateTimeOffset(root, "finishedAt") ?? ReadDateTimeOffset(state, "finishedAt");
        return new(status, incidentCount ?? -1, finishedAt);
    }

    private static string? ReadStatus(JsonElement element)
    {
        var property = FindProperty(element, "status");
        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return value switch
            {
                "Running" => "Running",
                "Finished" => "Finished",
                "Faulted" => "Faulted",
                "Cancelled" => "Cancelled",
                "Suspended" => "Suspended",
                _ => null
            };
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var numeric))
            return numeric switch
            {
                0 => "Running",
                1 => "Finished",
                _ => null
            };

        return null;
    }

    private static JsonElement FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property))
            return property;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                    return candidate.Value;
            }
        }

        return default;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static bool? ReadBoolean(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
    }

    private static int? ReadInt32(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) && value >= 0 ? value : null;
    }

    private static int? CountArray(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property.ValueKind == JsonValueKind.Array ? property.GetArrayLength() : null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out var value)
            ? value
            : null;
    }

    private static Uri ValidateEndpoint(string endpoint, string environmentName)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not "" and not "/" ||
            !uri.Host.EndsWith(".azurecontainerapps.io", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.StartsWith(environmentName + "-app.", StringComparison.OrdinalIgnoreCase))
            throw Failure("azure.proof.workflow.endpointInvalid", "The verified Elsa endpoint is invalid.");

        return new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" }.Uri;
    }

    private static bool IsSafeIdentifier(string? value) =>
        value is { Length: > 0 and <= 256 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsSafeToken(string? value) =>
        value is { Length: > 0 and <= 8192 } && value.All(character => !char.IsControl(character) && !char.IsWhiteSpace(character));

    private static DeploymentProofStageException Failure(string code, string message) =>
        new(DeploymentProofStage.Workflow, code, message);

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private readonly record struct WorkflowInstanceState(
        string? Status,
        int IncidentCount,
        DateTimeOffset? FinishedAt);
}
