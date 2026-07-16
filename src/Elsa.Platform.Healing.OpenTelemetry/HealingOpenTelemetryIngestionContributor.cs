using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Healing.OpenTelemetry;

/// <summary>
/// Server-authoritative routing for one authenticated telemetry source. Implementations must derive this scope
/// from the collector/deployment context, never from telemetry attributes supplied by the monitored process.
/// </summary>
public sealed record HealingTelemetryScope(Guid WorkspaceId, Guid ApplicationId, Guid EnvironmentId);

/// <summary>Authenticated claim names emitted by a Platform-owned per-source OTLP authenticator.</summary>
public static class HealingTelemetryScopeClaims
{
    public const string WorkspaceId = "elsa.platform.workspace.id";
    public const string ApplicationId = "elsa.platform.application.id";
    public const string EnvironmentId = "elsa.platform.environment.id";
}

/// <summary>
/// Resolves an authenticated, per-source ingestion identity to Healing scope. The resource is untrusted evidence
/// that may be validated against server records; it must never provide or override routing authority.
/// </summary>
public interface IHealingTelemetryScopeResolver
{
    ValueTask<HealingTelemetryScope?> ResolveAsync(
        OpenTelemetryIngestionContext ingestionContext,
        TelemetryResource resource,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves Healing scope exclusively from immutable claims established by the host authenticator. The generic
/// Foundation API-key context contains no scope claims and therefore fails closed.
/// </summary>
public sealed class AuthenticatedClaimHealingTelemetryScopeResolver : IHealingTelemetryScopeResolver
{
    public ValueTask<HealingTelemetryScope?> ResolveAsync(
        OpenTelemetryIngestionContext ingestionContext,
        TelemetryResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ingestionContext);
        ArgumentNullException.ThrowIfNull(resource);
        cancellationToken.ThrowIfCancellationRequested();

        if (!ingestionContext.IsAuthenticated ||
            string.IsNullOrWhiteSpace(ingestionContext.SourceIdentity) ||
            !TryGetGuid(ingestionContext.Claims, HealingTelemetryScopeClaims.WorkspaceId, out var workspaceId) ||
            !TryGetGuid(ingestionContext.Claims, HealingTelemetryScopeClaims.ApplicationId, out var applicationId) ||
            !TryGetGuid(ingestionContext.Claims, HealingTelemetryScopeClaims.EnvironmentId, out var environmentId))
        {
            return ValueTask.FromResult<HealingTelemetryScope?>(null);
        }

        return ValueTask.FromResult<HealingTelemetryScope?>(new(workspaceId, applicationId, environmentId));
    }

    private static bool TryGetGuid(
        IReadOnlyDictionary<string, string> claims,
        string claimName,
        out Guid value)
    {
        value = Guid.Empty;
        return claims.TryGetValue(claimName, out var claimValue) &&
               Guid.TryParse(claimValue, out value) &&
               value != Guid.Empty;
    }
}

/// <summary>
/// Durable, idempotent inbox boundary. Implementations own replay/conflict behavior for the item idempotency key.
/// </summary>
public interface IHealingSignalInboxAppender
{
    ValueTask AppendAsync(HealingSignalInboxItem item, CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts structurally valid, already-redacted OpenTelemetry exceptions into durable Healing inbox items.
/// Classification, attribution, deduplication, and dispatch deliberately remain background concerns.
/// </summary>
public sealed class HealingOpenTelemetryIngestionContributor(
    IServiceScopeFactory serviceScopeFactory,
    TimeProvider timeProvider) : IOpenTelemetryIngestionContributor
{
    private const string FoundationGlobalApiKeySourceIdentity = "elsa:otlp:configured-api-key";
    private const int MaximumIdempotencyKeyLength = 256;
    private const int MaximumProfileVersionLength = 32;
    private const int MaximumOperationNameLength = 512;
    private const int MaximumExceptionTypeLength = 2_048;
    private const int MaximumExceptionMessageLength = 8_192;
    private const int MaximumStackTraceLength = 131_072;
    private const int MaximumMetadataValueLength = 2_048;
    private const int MaximumClassificationValueLength = 128;

    public async ValueTask ContributeAsync(
        OpenTelemetryBatch redactedBatch,
        OpenTelemetryIngestionContext ingestionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redactedBatch);
        ArgumentNullException.ThrowIfNull(ingestionContext);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ingestionContext.IsAuthenticated ||
            string.IsNullOrWhiteSpace(ingestionContext.SourceIdentity) ||
            ingestionContext.SourceIdentity.Equals(FoundationGlobalApiKeySourceIdentity, StringComparison.Ordinal))
            return;

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IHealingTelemetryScopeResolver>();
        var appender = scope.ServiceProvider.GetRequiredService<IHealingSignalInboxAppender>();
        var resources = redactedBatch.Resources
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);

        foreach (var log in redactedBatch.Logs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resources.TryGetValue(log.ResourceId, out var resource))
                continue;

            var item = await TryMapLogAsync(log, resource, ingestionContext, resolver, cancellationToken);
            if (item is not null)
                await appender.AppendAsync(item, cancellationToken);
        }

        foreach (var span in redactedBatch.Spans.Where(x => x.Status == SpanStatus.Error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resources.TryGetValue(span.ResourceId, out var resource))
                continue;

            foreach (var exceptionEvent in span.Events.Where(x => x.Name.Equals("exception", StringComparison.OrdinalIgnoreCase)))
            {
                var attributes = MergeAttributes(resource.Attributes, span.Attributes, exceptionEvent.Attributes);
                var item = await TryMapCandidateAsync(
                    resource,
                    exceptionEvent.Timestamp,
                    span.TraceId,
                    span.SpanId,
                    nameof(SpanStatus.Error),
                    attributes,
                    ingestionContext,
                    resolver,
                    cancellationToken);
                if (item is not null)
                    await appender.AppendAsync(item, cancellationToken);
            }
        }
    }

    private async ValueTask<HealingSignalInboxItem?> TryMapLogAsync(
        OtlpLogRecord log,
        TelemetryResource resource,
        OpenTelemetryIngestionContext ingestionContext,
        IHealingTelemetryScopeResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!IsError(log))
            return null;

        return await TryMapCandidateAsync(
            resource,
            log.Timestamp,
            log.TraceId,
            log.SpanId,
            !string.IsNullOrWhiteSpace(log.SeverityText)
                ? log.SeverityText
                : MapSeverityNumber(log.SeverityNumber),
            MergeAttributes(resource.Attributes, log.Attributes),
            ingestionContext,
            resolver,
            cancellationToken);
    }

    private async ValueTask<HealingSignalInboxItem?> TryMapCandidateAsync(
        TelemetryResource resource,
        DateTimeOffset occurredAt,
        string? traceId,
        string? spanId,
        string? severity,
        IDictionary<string, string?> attributes,
        OpenTelemetryIngestionContext ingestionContext,
        IHealingTelemetryScopeResolver resolver,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource.ServiceName) ||
            !TryGetRequired(attributes, HealingSignalAttributes.ProfileVersion, out var profileVersion) ||
            !TryGetRequired(attributes, HealingSignalAttributes.OperationName, out var operationName) ||
            !TryGetRequired(attributes, "exception.type", out var exceptionType) ||
            !TryGetRequired(attributes, "exception.stacktrace", out var stackTrace) ||
            profileVersion.Length > MaximumProfileVersionLength ||
            operationName.Length > MaximumOperationNameLength ||
            exceptionType.Length > MaximumExceptionTypeLength)
            return null;

        var trustedScope = await resolver.ResolveAsync(ingestionContext, resource, cancellationToken);
        if (trustedScope is null ||
            !ClaimMatches(attributes, HealingSignalAttributes.ApplicationId, trustedScope.ApplicationId) ||
            !ClaimMatches(attributes, HealingSignalAttributes.EnvironmentId, trustedScope.EnvironmentId))
        {
            return null;
        }

        var omittedFields = new List<string>();
        var message = Bound(
            GetValue(attributes, "exception.message"),
            MaximumExceptionMessageLength,
            "exception.message",
            omittedFields);
        stackTrace = Bound(
            stackTrace,
            MaximumStackTraceLength,
            "exception.stacktrace",
            omittedFields)!;
        var failureClass = Bound(
                GetValue(attributes, HealingSignalAttributes.FailureClass),
                MaximumClassificationValueLength,
                HealingSignalAttributes.FailureClass,
                omittedFields)
            ?? HealingFailureClasses.Unknown;
        var retryState = Bound(
                GetValue(attributes, HealingSignalAttributes.RetryState),
                MaximumClassificationValueLength,
                HealingSignalAttributes.RetryState,
                omittedFields)
            ?? HealingRetryStates.None;
        var occurrenceId = GetValue(attributes, HealingSignalAttributes.OccurrenceId);
        if (occurrenceId?.Length > MaximumIdempotencyKeyLength)
        {
            occurrenceId = null;
            omittedFields.Add(HealingSignalAttributes.OccurrenceId);
        }
        var sourceRevision = BoundMetadata(attributes, HealingSignalAttributes.SourceRevision, omittedFields);
        var manifestDigest = BoundMetadata(attributes, HealingSignalAttributes.ComponentManifestDigest, omittedFields);
        var componentKey = BoundMetadata(attributes, HealingSignalAttributes.ComponentKey, omittedFields);
        var workflowDefinitionId = BoundMetadata(attributes, HealingSignalAttributes.WorkflowDefinitionId, omittedFields);
        var workflowActivityType = BoundMetadata(attributes, HealingSignalAttributes.WorkflowActivityType, omittedFields);
        var serviceName = Bound(resource.ServiceName, MaximumMetadataValueLength, "service.name", omittedFields);
        var resourceIdentity = Bound(resource.Id, MaximumMetadataValueLength, "resource.identity", omittedFields);
        severity = Bound(severity, MaximumClassificationValueLength, "severity", omittedFields);
        var signal = new HealingSignal(
            profileVersion,
            trustedScope.ApplicationId,
            trustedScope.EnvironmentId,
            ParseGuid(attributes, HealingSignalAttributes.RevisionId),
            occurredAt,
            operationName,
            failureClass,
            retryState,
            new HealingExceptionEvidence(
                exceptionType,
                message,
                stackTrace,
                []),
            new HealingEvidenceMetadata(true, omittedFields.Count > 0, omittedFields),
            occurrenceId,
            sourceRevision,
            manifestDigest,
            ParseBoolean(attributes, HealingSignalAttributes.Explicit),
            componentKey,
            workflowDefinitionId,
            workflowActivityType,
            new HealingTraceContext(traceId, spanId),
            serviceName,
            resourceIdentity,
            severity);
        var envelope = JsonSerializer.Serialize(signal);
        var envelopeHash = Sha256(envelope);

        return new HealingSignalInboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = trustedScope.WorkspaceId,
            ApplicationId = trustedScope.ApplicationId,
            EnvironmentId = trustedScope.EnvironmentId,
            IdempotencyKey = occurrenceId ?? DeriveOccurrenceKey(
                traceId,
                spanId,
                occurredAt,
                resource,
                operationName,
                exceptionType,
                stackTrace),
            Source = HealingSignalSource.OpenTelemetry,
            ProfileVersion = profileVersion,
            OccurredAt = occurredAt,
            AcceptedAt = timeProvider.GetUtcNow(),
            RedactedEnvelopeJson = envelope,
            EnvelopeHash = envelopeHash,
            Status = HealingInboxStatus.Pending
        };
    }

    private static bool IsError(OtlpLogRecord log) =>
        log.SeverityNumber is >= 17 ||
        log.SeverityText.Equals("error", StringComparison.OrdinalIgnoreCase) ||
        log.SeverityText.Equals("fatal", StringComparison.OrdinalIgnoreCase) ||
        log.SeverityText.Equals("critical", StringComparison.OrdinalIgnoreCase);

    private static string? MapSeverityNumber(int? severityNumber) => severityNumber switch
    {
        >= 21 => "fatal",
        >= 17 => "error",
        >= 13 => "warning",
        >= 9 => "informational",
        null => null,
        _ => "informational"
    };

    private static bool ClaimMatches(
        IDictionary<string, string?> attributes,
        string name,
        Guid trustedValue) =>
        !attributes.TryGetValue(name, out var claimed) ||
        (Guid.TryParse(claimed, out var parsed) && parsed == trustedValue);

    private static Guid? ParseGuid(IDictionary<string, string?> attributes, string name) =>
        Guid.TryParse(GetValue(attributes, name), out var value) ? value : null;

    private static bool ParseBoolean(IDictionary<string, string?> attributes, string name) =>
        bool.TryParse(GetValue(attributes, name), out var value) && value;

    private static bool TryGetRequired(
        IDictionary<string, string?> attributes,
        string name,
        out string value)
    {
        value = GetValue(attributes, name) ?? string.Empty;
        return value.Length > 0;
    }

    private static string? GetValue(IDictionary<string, string?> attributes, string name) =>
        attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? Bound(
        string? value,
        int maximumLength,
        string fieldName,
        ICollection<string> omittedFields)
    {
        if (value is null || value.Length <= maximumLength)
            return value;

        omittedFields.Add(fieldName);
        return value[..maximumLength];
    }

    private static string? BoundMetadata(
        IDictionary<string, string?> attributes,
        string name,
        ICollection<string> omittedFields) =>
        Bound(GetValue(attributes, name), MaximumMetadataValueLength, name, omittedFields);

    private static Dictionary<string, string?> MergeAttributes(params IDictionary<string, string?>[] sources)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var source in sources)
        foreach (var pair in source)
            result[pair.Key] = pair.Value;
        return result;
    }

    private static string DeriveOccurrenceKey(
        string? traceId,
        string? spanId,
        DateTimeOffset occurredAt,
        TelemetryResource resource,
        string operationName,
        string exceptionType,
        string stackTrace)
    {
        var stableEvidence = string.Join('\n',
            traceId ?? string.Empty,
            spanId ?? string.Empty,
            occurredAt.ToUniversalTime().ToString("O"),
            resource.Id,
            resource.ServiceName,
            operationName,
            exceptionType,
            stackTrace);
        return $"otel:v1:{Sha256(stableEvidence)}";
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
