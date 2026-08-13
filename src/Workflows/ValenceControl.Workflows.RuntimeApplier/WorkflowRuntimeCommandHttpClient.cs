using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValenceControl.Deployment.Abstractions.Artifacts;

namespace ValenceControl.Workflows.RuntimeApplier;

public sealed class WorkflowRuntimeCommandHttpClient(
    HttpClient httpClient,
    WorkflowArtifactRuntimeOptions options) : IWorkflowRuntimeCommandClient
{
    private const string EngineSecretHeaderName = "X-Elsa-Engine-Secret";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly WorkflowArtifactRuntimeOptions _options = ValidateOptions(options);

    public async Task<IReadOnlyList<WorkflowRuntimeCommand>> PollAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new InvalidOperationException("Runtime command poll limit must be positive.");

        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri($"/deployments/runtime/engines/{_options.EngineId!.Value:D}/commands?limit={limit}"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync<RuntimeCommandListResponse>(response, cancellationToken)
            ?? throw new InvalidOperationException("Control runtime command poll response could not be read.");
        return body.Commands.Select(ToCommand).ToList();
    }

    public async Task<WorkflowRuntimeCommandClaimResult> ClaimAsync(
        Guid commandId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendJsonAsync(
            BuildUri($"/deployments/runtime/commands/{commandId:D}/claim"),
            new RuntimeCommandClaimRequest(_options.EngineId!.Value, _options.WorkerId, (int)_options.ClaimLeaseDuration.TotalSeconds),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await ReadJsonAsync<RuntimeCommandClaimResponse>(response, cancellationToken);
            return body is null
                ? new WorkflowRuntimeCommandClaimResult(WorkflowRuntimeCommandClientStatus.RetryableError, "Control claim response could not be read.")
                : new WorkflowRuntimeCommandClaimResult(
                    WorkflowRuntimeCommandClientStatus.Succeeded,
                    "Runtime command claimed.",
                    new WorkflowRuntimeCommandClaim(ToCommand(body.Command), body.LeaseToken));
        }

        return new WorkflowRuntimeCommandClaimResult(MapStatus(response.StatusCode), await SafeResponseMessageAsync(response, cancellationToken));
    }

    public Task<WorkflowRuntimeCommandReportResult> HeartbeatAsync(
        Guid commandId,
        string leaseToken,
        CancellationToken cancellationToken = default) =>
        PostCommandMutationAsync(
            commandId,
            "heartbeat",
            new RuntimeCommandHeartbeatRequest(leaseToken, _options.WorkerId),
            cancellationToken);

    public Task<WorkflowRuntimeCommandReportResult> ReportProgressAsync(
        Guid commandId,
        string leaseToken,
        string status,
        int? percentComplete,
        string message,
        CancellationToken cancellationToken = default) =>
        PostCommandMutationAsync(
            commandId,
            "progress",
            new RuntimeCommandProgressRequest(leaseToken, status, percentComplete, message),
            cancellationToken);

    public Task<WorkflowRuntimeCommandReportResult> CompleteAsync(
        Guid commandId,
        string leaseToken,
        ArtifactDigest? observedDigest,
        string? runtimeReference,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken = default) =>
        PostCommandMutationAsync(
            commandId,
            "complete",
            new RuntimeCommandCompleteRequest(leaseToken, observedDigest is null ? null : ToDigest(observedDigest.Value), runtimeReference, ToDiagnostics(diagnostics)),
            cancellationToken);

    public Task<WorkflowRuntimeCommandReportResult> FailAsync(
        Guid commandId,
        string leaseToken,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken = default) =>
        PostCommandMutationAsync(
            commandId,
            "fail",
            new RuntimeCommandFailRequest(leaseToken, ToDiagnostics(diagnostics)),
            cancellationToken);

    public Task<WorkflowRuntimeCommandReportResult> RejectAsync(
        Guid commandId,
        string leaseToken,
        IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
        CancellationToken cancellationToken = default) =>
        PostCommandMutationAsync(
            commandId,
            "reject",
            new RuntimeCommandRejectRequest(leaseToken, ToDiagnostics(diagnostics)),
            cancellationToken);

    private async Task<WorkflowRuntimeCommandReportResult> PostCommandMutationAsync<TRequest>(
        Guid commandId,
        string action,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendJsonAsync(
            BuildUri($"/deployments/runtime/commands/{commandId:D}/{action}"),
            request,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await ReadJsonAsync<RuntimeCommandDto>(response, cancellationToken);
            return body is null
                ? new WorkflowRuntimeCommandReportResult(WorkflowRuntimeCommandClientStatus.RetryableError, "Control runtime command response could not be read.")
                : new WorkflowRuntimeCommandReportResult(WorkflowRuntimeCommandClientStatus.Succeeded, "Runtime command report accepted.", ToCommand(body));
        }

        return new WorkflowRuntimeCommandReportResult(MapStatus(response.StatusCode), await SafeResponseMessageAsync(response, cancellationToken));
    }

    private Uri BuildUri(string relativePath)
    {
        var endpoint = _options.ControlEndpoint ?? httpClient.BaseAddress
            ?? throw new InvalidOperationException("Control endpoint is required before runtime command sync can start.");
        return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/api/workspaces/{_options.WorkspaceId!.Value:D}{relativePath}");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrWhiteSpace(_options.EngineSecret))
            request.Headers.TryAddWithoutValidation(EngineSecretHeaderName, _options.EngineSecret);
        return request;
    }

    private async Task<HttpResponseMessage> SendJsonAsync<TRequest>(
        Uri uri,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, uri);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static WorkflowArtifactRuntimeOptions ValidateOptions(WorkflowArtifactRuntimeOptions options)
    {
        options.Validate();
        return options;
    }

    private static WorkflowRuntimeCommandClientStatus MapStatus(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.NotFound => WorkflowRuntimeCommandClientStatus.NotFound,
            HttpStatusCode.Conflict => WorkflowRuntimeCommandClientStatus.Conflict,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WorkflowRuntimeCommandClientStatus.Unauthorized,
            HttpStatusCode.BadRequest => WorkflowRuntimeCommandClientStatus.ValidationFailed,
            _ when (int)statusCode >= 500 => WorkflowRuntimeCommandClientStatus.RetryableError,
            _ => WorkflowRuntimeCommandClientStatus.ValidationFailed
        };

    private static async Task<string> SafeResponseMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(JsonOptions, cancellationToken);
            return WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(problem?.Title ?? problem?.Detail ?? response.ReasonPhrase);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(response.ReasonPhrase);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return default;
        }
    }

    private static WorkflowRuntimeCommand ToCommand(RuntimeCommandDto dto) =>
        new(
            dto.Id,
            dto.WorkspaceId,
            dto.RunId,
            dto.EnvironmentId,
            dto.EngineId,
            ToAction(dto.Action),
            ToStatus(dto.Status),
            dto.Artifact is null ? null : new WorkflowRuntimeCommandArtifactReference(
                dto.Artifact.ArtifactRecordId,
                dto.Artifact.ArtifactId,
                dto.Artifact.ArtifactTypeId,
                dto.Artifact.ContentDigest is null ? null : ToArtifactDigest(dto.Artifact.ContentDigest)),
            dto.Revision is null ? null : new WorkflowRuntimeCommandRevisionReference(dto.Revision.RevisionId),
            dto.IdempotencyKey,
            dto.WorkerId,
            dto.ClaimedAt,
            dto.LeaseExpiresAt,
            dto.HeartbeatAt,
            dto.AttemptNumber,
            dto.PercentComplete,
            dto.ProgressMessage,
            dto.ObservedArtifactDigest is null ? null : ToArtifactDigest(dto.ObservedArtifactDigest),
            dto.RuntimeReference,
            dto.Diagnostics.Select(ToDiagnostic).ToList(),
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.AvailableAt,
            dto.ExpiresAt,
            dto.CompletedAt);

    private static WorkflowRuntimeCommandAction ToAction(string action) =>
        Enum.TryParse<WorkflowRuntimeCommandAction>(action, ignoreCase: true, out var value) ? value : WorkflowRuntimeCommandAction.Unknown;

    private static WorkflowRuntimeCommandStatus ToStatus(string status) =>
        Enum.TryParse<WorkflowRuntimeCommandStatus>(status, ignoreCase: true, out var value) ? value : WorkflowRuntimeCommandStatus.Unknown;

    private static WorkflowArtifactDiagnostic ToDiagnostic(RuntimeCommandDiagnostic diagnostic) =>
        new(diagnostic.Code, ToSeverity(diagnostic.Severity), WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(diagnostic.Message));

    private static IReadOnlyList<RuntimeCommandDiagnostic> ToDiagnostics(IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics) =>
        diagnostics
            .Select(x => new RuntimeCommandDiagnostic(x.Code, ToSeverity(x.Severity), WorkflowArtifactRuntimeDiagnosticSanitizer.SafeMessage(x.Message)))
            .ToList();

    private static WorkflowArtifactDiagnosticSeverity ToSeverity(string severity) =>
        Enum.TryParse<WorkflowArtifactDiagnosticSeverity>(severity, ignoreCase: true, out var value) ? value : WorkflowArtifactDiagnosticSeverity.Info;

    private static string ToSeverity(WorkflowArtifactDiagnosticSeverity severity) =>
        severity.ToString();

    private static ArtifactDigest ToArtifactDigest(RuntimeCommandDigest digest) =>
        new(digest.Algorithm, digest.Value);

    private static RuntimeCommandDigest ToDigest(ArtifactDigest digest) =>
        new(digest.Algorithm, digest.Value);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private sealed record RuntimeCommandListResponse(IReadOnlyList<RuntimeCommandDto> Commands);

    private sealed record RuntimeCommandClaimRequest(Guid EngineId, string WorkerId, int LeaseSeconds);

    private sealed record RuntimeCommandClaimResponse(RuntimeCommandDto Command, string LeaseToken);

    private sealed record RuntimeCommandHeartbeatRequest(string LeaseToken, string WorkerId);

    private sealed record RuntimeCommandProgressRequest(string LeaseToken, string Status, int? PercentComplete, string Message);

    private sealed record RuntimeCommandCompleteRequest(
        string LeaseToken,
        RuntimeCommandDigest? ObservedArtifactDigest,
        string? RuntimeReference,
        IReadOnlyList<RuntimeCommandDiagnostic> Diagnostics);

    private sealed record RuntimeCommandFailRequest(string LeaseToken, IReadOnlyList<RuntimeCommandDiagnostic> Diagnostics);

    private sealed record RuntimeCommandRejectRequest(string LeaseToken, IReadOnlyList<RuntimeCommandDiagnostic> Diagnostics);

    private sealed record RuntimeCommandDto(
        Guid Id,
        Guid WorkspaceId,
        Guid RunId,
        Guid EnvironmentId,
        Guid EngineId,
        string Action,
        string Status,
        RuntimeCommandArtifactReference? Artifact,
        RuntimeCommandRevisionReference? Revision,
        string IdempotencyKey,
        string? WorkerId,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? LeaseExpiresAt,
        DateTimeOffset? HeartbeatAt,
        int AttemptNumber,
        int? PercentComplete,
        string? ProgressMessage,
        RuntimeCommandDigest? ObservedArtifactDigest,
        string? RuntimeReference,
        IReadOnlyList<RuntimeCommandDiagnostic> Diagnostics,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? AvailableAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? CompletedAt);

    private sealed record RuntimeCommandArtifactReference(
        Guid? ArtifactRecordId,
        string? ArtifactId,
        string? ArtifactTypeId,
        RuntimeCommandDigest? ContentDigest);

    private sealed record RuntimeCommandRevisionReference(Guid? RevisionId);

    private sealed record RuntimeCommandDigest(string Algorithm, string Value);

    private sealed record RuntimeCommandDiagnostic(string Code, string Severity, string Message);

    private sealed record ProblemDetailsResponse(string? Title, string? Detail);
}
