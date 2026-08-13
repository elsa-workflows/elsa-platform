using System.Text.Json;
using System.Text;

namespace ValenceControl.Healing.Core.Security;

public sealed record HealingAuditWrite(
    Guid WorkspaceId,
    string AggregateType,
    Guid AggregateId,
    string EventType,
    string ReasonCode,
    string ActorType,
    string ActorId,
    Guid CorrelationId,
    Guid? CausationId,
    string? PolicyVersion,
    string? InputHash,
    string? OutputHash,
    IReadOnlyDictionary<string, string?> SafeDetails);

public sealed record HealingAuditQuery(
    Guid WorkspaceId,
    Guid? AggregateId = null,
    Guid? CorrelationId = null,
    int Limit = 200);

/// <summary>
/// Append/query-only persistence seam. Implementations must assign a monotonic sequence and must not expose
/// update or delete operations for audit events.
/// </summary>
public interface IHealingAuditStore
{
    ValueTask<HealingAuditEvent> AppendAsync(
        HealingAuditEvent auditEvent,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(
        HealingAuditQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class HealingAuditService(IHealingAuditStore store, TimeProvider? timeProvider = null)
{
    private enum SafeDetailValueKind { Code, NonNegativeInteger, Revision }

    private static readonly IReadOnlyDictionary<string, SafeDetailValueKind> AllowedSafeDetails =
        new Dictionary<string, SafeDetailValueKind>(StringComparer.Ordinal)
        {
            ["attemptCount"] = SafeDetailValueKind.NonNegativeInteger,
            ["attemptLimit"] = SafeDetailValueKind.NonNegativeInteger,
            ["environment"] = SafeDetailValueKind.Code,
            ["gateReason"] = SafeDetailValueKind.Code,
            ["operationType"] = SafeDetailValueKind.Code,
            ["outcomeCode"] = SafeDetailValueKind.Code,
            ["providerOutcome"] = SafeDetailValueKind.Code,
            ["pullRequestNumber"] = SafeDetailValueKind.NonNegativeInteger,
            ["repositoryName"] = SafeDetailValueKind.Code,
            ["repositoryOwner"] = SafeDetailValueKind.Code,
            ["revision"] = SafeDetailValueKind.Revision,
            ["status"] = SafeDetailValueKind.Code,
            ["verificationStatus"] = SafeDetailValueKind.Code
        };
    private static readonly string[] ForbiddenDetailKeyFragments =
    [
        "authorization", "connectionstring", "cookie", "credential", "password", "requestbody", "secret", "token"
    ];
    private static readonly string[] CredentialValueMarkers =
    [
        "Bearer ", "-----BEGIN ", "AccountKey=", "Password=", "Secret=", "Token=", "AKIA", "ghp_", "github_pat_", "sk-"
    ];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask<HealingAuditEvent> AppendAsync(
        HealingAuditWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ValidateWrite(write);

        var detailJson = JsonSerializer.Serialize(
            write.SafeDetails.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
        if (Encoding.UTF8.GetByteCount(detailJson) > 16_384)
            throw new ArgumentException("Safe audit details exceed the 16384 byte limit.", nameof(write));

        var auditEvent = new HealingAuditEvent
        {
            Id = Guid.NewGuid(),
            WorkspaceId = write.WorkspaceId,
            AggregateType = write.AggregateType,
            AggregateId = write.AggregateId,
            EventType = write.EventType,
            ReasonCode = write.ReasonCode,
            ActorType = write.ActorType,
            ActorId = write.ActorId,
            CorrelationId = write.CorrelationId,
            CausationId = write.CausationId,
            PolicyVersion = write.PolicyVersion,
            InputHash = write.InputHash,
            OutputHash = write.OutputHash,
            SafeDetailJson = detailJson,
            OccurredAt = _timeProvider.GetUtcNow()
        };

        return store.AppendAsync(auditEvent, cancellationToken);
    }

    public ValueTask<IReadOnlyList<HealingAuditEvent>> QueryAsync(
        HealingAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.WorkspaceId == Guid.Empty)
            throw new ArgumentException("WorkspaceId is required.", nameof(query));
        if (query.Limit is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(query), "Audit query limit must be between 1 and 1000.");

        return store.QueryAsync(query, cancellationToken);
    }

    private static void ValidateWrite(HealingAuditWrite write)
    {
        if (write.WorkspaceId == Guid.Empty)
            throw new ArgumentException("WorkspaceId is required.", nameof(write));
        if (write.AggregateId == Guid.Empty)
            throw new ArgumentException("AggregateId is required.", nameof(write));
        if (write.CorrelationId == Guid.Empty)
            throw new ArgumentException("CorrelationId is required.", nameof(write));

        ValidateRequired(write.AggregateType, nameof(write.AggregateType));
        ValidateRequired(write.EventType, nameof(write.EventType));
        ValidateRequired(write.ReasonCode, nameof(write.ReasonCode));
        ValidateRequired(write.ActorType, nameof(write.ActorType));
        ValidateRequired(write.ActorId, nameof(write.ActorId));
        ValidateOptional(write.PolicyVersion, nameof(write.PolicyVersion));
        ValidateOptional(write.InputHash, nameof(write.InputHash));
        ValidateOptional(write.OutputHash, nameof(write.OutputHash));

        ArgumentNullException.ThrowIfNull(write.SafeDetails);
        if (write.SafeDetails.Count > 32)
            throw new ArgumentException("Safe audit details may contain at most 32 fields.", nameof(write));

        foreach (var (key, value) in write.SafeDetails)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
                throw new ArgumentException("Safe audit detail keys must contain 1 to 64 characters.", nameof(write));
            if (ForbiddenDetailKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Safe audit detail key '{key}' is not permitted.", nameof(write));
            if (!AllowedSafeDetails.TryGetValue(key, out var valueKind))
                throw new ArgumentException($"Safe audit detail key '{key}' is not registered in the audit schema.", nameof(write));
            if (value?.Length > 1_024)
                throw new ArgumentException($"Safe audit detail value '{key}' exceeds 1024 characters.", nameof(write));
            if (value is not null && LooksLikeCredential(value))
                throw new ArgumentException($"Safe audit detail value '{key}' contains credential material.", nameof(write));
            if (value is not null && !IsAllowedValue(value, valueKind))
                throw new ArgumentException($"Safe audit detail value '{key}' does not match its registered safe value type.", nameof(write));
        }
    }

    private static bool IsAllowedValue(string value, SafeDetailValueKind kind) => kind switch
    {
        SafeDetailValueKind.Code => value.Length is > 0 and <= 128 && value.All(IsSafeCodeCharacter),
        SafeDetailValueKind.NonNegativeInteger => value.Length is > 0 and <= 10 && value.All(char.IsAsciiDigit),
        SafeDetailValueKind.Revision => value.Length is >= 7 and <= 64 && value.All(char.IsAsciiHexDigit),
        _ => false
    };

    private static bool IsSafeCodeCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or '/' or ':';

    private static bool LooksLikeCredential(string value)
    {
        if (CredentialValueMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        var jwtSegments = value.Split('.');
        if (jwtSegments.Length == 3 && jwtSegments.All(segment => segment.Length >= 8 && segment.All(IsBase64UrlCharacter)))
            return true;

        // Opaque application credentials are commonly long, mixed-alphabet tokens. Audit details are intended for
        // bounded codes and counters, so rejecting this shape is safer than attempting lossy redaction after intake.
        return value.Length >= 24
               && value.All(IsBase64UrlCharacter)
               && value.Any(char.IsAsciiDigit)
               && value.Any(char.IsAsciiLetterLower)
               && value.Any(char.IsAsciiLetterUpper);
    }

    private static bool IsBase64UrlCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_';

    private static void ValidateRequired(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{field} is required.", field);
        if (value.Length > 256)
            throw new ArgumentException($"{field} exceeds 256 characters.", field);
        if (value.Any(char.IsControl) || LooksLikeCredential(value))
            throw new ArgumentException($"{field} contains unsafe audit material.", field);
    }

    private static void ValidateOptional(string? value, string field)
    {
        if (value?.Length > 256)
            throw new ArgumentException($"{field} exceeds 256 characters.", field);
        if (value is not null && (value.Any(char.IsControl) || LooksLikeCredential(value)))
            throw new ArgumentException($"{field} contains unsafe audit material.", field);
    }
}
