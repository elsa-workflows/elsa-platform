using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Platform.Healing.Client;

public sealed class HealingClientOptions
{
    public Uri? PlatformBaseAddress { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid EnvironmentId { get; set; }
}

public sealed record HealingTelemetryContext(
    Guid ApplicationId,
    Guid EnvironmentId,
    string OperationName,
    string FailureClass,
    string RetryState = HealingRetryStates.None,
    Guid? RevisionId = null,
    string? SourceRevision = null,
    string? ComponentManifestDigest = null,
    string? OccurrenceId = null,
    string? ComponentKey = null,
    string? WorkflowDefinitionId = null,
    string? WorkflowActivityType = null,
    bool IsExplicit = false)
{
    public IReadOnlyDictionary<string, object?> ToAttributes()
    {
        Validate();
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [HealingSignalAttributes.ProfileVersion] = HealingContractVersions.SignalProfile,
            [HealingSignalAttributes.ApplicationId] = ApplicationId.ToString("D"),
            [HealingSignalAttributes.EnvironmentId] = EnvironmentId.ToString("D"),
            [HealingSignalAttributes.OperationName] = OperationName,
            [HealingSignalAttributes.FailureClass] = FailureClass,
            [HealingSignalAttributes.RetryState] = RetryState
        };
        Add(attributes, HealingSignalAttributes.RevisionId, RevisionId?.ToString("D"));
        Add(attributes, HealingSignalAttributes.SourceRevision, SourceRevision);
        Add(attributes, HealingSignalAttributes.ComponentManifestDigest, ComponentManifestDigest);
        Add(attributes, HealingSignalAttributes.OccurrenceId, OccurrenceId);
        Add(attributes, HealingSignalAttributes.ComponentKey, ComponentKey);
        Add(attributes, HealingSignalAttributes.WorkflowDefinitionId, WorkflowDefinitionId);
        Add(attributes, HealingSignalAttributes.WorkflowActivityType, WorkflowActivityType);
        if (IsExplicit)
            attributes[HealingSignalAttributes.Explicit] = true;
        return new ReadOnlyDictionary<string, object?>(attributes);
    }

    private void Validate()
    {
        if (ApplicationId == Guid.Empty || EnvironmentId == Guid.Empty ||
            !IsBounded(OperationName, 1_024) || !IsBounded(FailureClass, 128) || !IsBounded(RetryState, 128) ||
            !HealingFailureClasses.All.Contains(FailureClass) || !HealingRetryStates.All.Contains(RetryState))
            throw new ArgumentException("The Healing telemetry context is invalid.");

        foreach (var value in new[]
                 {
                     SourceRevision, ComponentManifestDigest, OccurrenceId, ComponentKey, WorkflowDefinitionId,
                     WorkflowActivityType
                 })
        {
            if (value is not null && !IsBounded(value, 1_024))
                throw new ArgumentException("A Healing telemetry attribute is invalid.");
        }
    }

    private static void Add(IDictionary<string, object?> attributes, string name, string? value)
    {
        if (value is not null)
            attributes[name] = value;
    }

    private static bool IsBounded(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);
}

public static class HealingActivityExtensions
{
    public static Activity EnrichForHealing(this Activity activity, HealingTelemetryContext context)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(context);
        foreach (var (name, value) in context.ToAttributes())
            activity.SetTag(name, value);
        return activity;
    }
}

public interface IHealingClient
{
    ValueTask<ExplicitHealingIncidentAcceptedResponse> ReportIncidentAsync(
        ExplicitHealingIncidentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}

public sealed class HealingClient(
    HttpClient httpClient,
    IOptions<HealingClientOptions> options) : IHealingClient
{
    private const int MaximumResponseBytes = 16_384;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<ExplicitHealingIncidentAcceptedResponse> ReportIncidentAsync(
        ExplicitHealingIncidentRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = ValidateOptions(options.Value);
        ValidateRequest(request, idempotencyKey);

        var path = $"api/workspaces/{settings.WorkspaceId:D}/healing/applications/{settings.ApplicationId:D}/environments/{settings.EnvironmentId:D}/incidents";
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.PlatformBaseAddress!, path))
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };
        if (idempotencyKey is not null)
            message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw await CreateFailureAsync(response, cancellationToken);
        var payload = await ReadBoundedAsync(response, cancellationToken);
        ExplicitHealingIncidentAcceptedResponse? accepted;
        try
        {
            accepted = JsonSerializer.Deserialize<ExplicitHealingIncidentAcceptedResponse>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new HealingClientException(response.StatusCode, "healing.client.response-invalid");
        }
        return accepted is not null && accepted.InboxId != Guid.Empty
            ? accepted
            : throw new HealingClientException(response.StatusCode, "healing.client.response-invalid");
    }

    private static HealingClientOptions ValidateOptions(HealingClientOptions settings)
    {
        if (settings.PlatformBaseAddress is null || !settings.PlatformBaseAddress.IsAbsoluteUri ||
            settings.PlatformBaseAddress.Scheme != Uri.UriSchemeHttps ||
            settings.WorkspaceId == Guid.Empty || settings.ApplicationId == Guid.Empty || settings.EnvironmentId == Guid.Empty)
            throw new InvalidOperationException("Healing client options require an HTTPS Platform address and non-empty scope IDs.");
        return settings;
    }

    private static void ValidateRequest(ExplicitHealingIncidentRequest request, string? idempotencyKey)
    {
        if (!HealingContractVersion.IsCompatible(HealingContractVersions.SignalProfile, request.ProfileVersion) ||
            request.OccurredAt == default || !request.IsExplicit || request.Evidence is not { IsRedacted: true } ||
            request.Exception is null || string.IsNullOrWhiteSpace(request.Exception.Type) ||
            string.IsNullOrWhiteSpace(request.OperationName) ||
            !HealingFailureClasses.All.Contains(request.FailureClass) ||
            !HealingRetryStates.All.Contains(request.RetryState))
            throw new ArgumentException("The explicit Healing incident is invalid or has not been redacted.", nameof(request));
        if (idempotencyKey is not null &&
            (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256 || idempotencyKey.Any(char.IsControl)))
            throw new ArgumentException("The idempotency key is invalid.", nameof(idempotencyKey));
    }

    private static async ValueTask<HealingClientException> CreateFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var code = "healing.client.request-failed";
        if (response.Content.Headers.ContentLength is null or <= MaximumResponseBytes)
        {
            try
            {
                var payload = await ReadBoundedAsync(response, cancellationToken);
                using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
                if (document.RootElement.TryGetProperty("code", out var value) &&
                    value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 256 } parsed &&
                    !parsed.Any(char.IsControl))
                    code = parsed;
            }
            catch (JsonException)
            {
                // The response body is untrusted; callers receive only the bounded stable fallback code.
            }
        }

        return new(response.StatusCode, code);
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new HealingClientException(response.StatusCode, "healing.client.response-too-large");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumResponseBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken);
            if (read == 0)
                break;
            length += read;
        }
        if (length > MaximumResponseBytes)
            throw new HealingClientException(response.StatusCode, "healing.client.response-too-large");
        return buffer[..length];
    }
}

public sealed class HealingClientException(HttpStatusCode statusCode, string reasonCode) : Exception(reasonCode)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ReasonCode { get; } = reasonCode;
}

public static class HealingClientServiceCollectionExtensions
{
    public static IServiceCollection AddElsaPlatformHealingClient(
        this IServiceCollection services,
        Action<HealingClientOptions> configure,
        Action<HttpClient>? configureHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<HealingClientOptions>().Configure(configure);
        var client = services.AddHttpClient<IHealingClient, HealingClient>();
        if (configureHttpClient is not null)
            client.ConfigureHttpClient(configureHttpClient);
        return services;
    }
}
