using System.Security.Cryptography;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Elsa.Platform.Healing.Core.Repairs;

public enum EvidenceField
{
    ExceptionType,
    OperationName,
    NormalizedStack,
    OccurrenceWindow,
    AffectedEnvironments,
    ProducingRevisions,
    ComponentAttribution,
    TraceCorrelation,
    SafeAttributes
}

public sealed record EvidenceBundleRequest(Guid WorkspaceId, Guid ApplicationId, Guid IncidentId)
{
    public static EvidenceBundleRequest CreateDefault(Guid workspaceId, Guid applicationId, Guid incidentId) =>
        new(workspaceId, applicationId, incidentId);
}

public sealed record EvidenceSourceRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid IncidentId,
    IReadOnlySet<EvidenceField> Fields);

public sealed record EvidenceSourceSnapshot(
    IReadOnlyDictionary<EvidenceField, string?> Values,
    IReadOnlyDictionary<EvidenceField, string> Provenance);

public sealed record EvidenceElevationRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid IncidentId,
    Guid BaseBundleId,
    Guid TargetAttemptId,
    string RequesterId,
    string Purpose,
    IReadOnlySet<EvidenceField> RequestedFields);

public sealed record EvidenceElevationAuthorizationRequest(
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid IncidentId,
    Guid BaseBundleId,
    Guid TargetAttemptId,
    string RequesterId,
    string Purpose,
    IReadOnlySet<EvidenceField> RequestedFields);

public sealed record EvidenceElevationAuthorization(
    bool Authorized,
    string? ApprovedBy,
    IReadOnlyList<string> ReasonCodes)
{
    public static EvidenceElevationAuthorization Approved(string approvedBy) =>
        new(true, approvedBy, ["authorized-elevation"]);

    public static EvidenceElevationAuthorization Denied(params string[] reasonCodes) =>
        new(false, null, reasonCodes.Length == 0 ? ["not-authorized"] : reasonCodes);
}

public sealed record EvidenceBundleResult(bool Succeeded, string ReasonCode, EvidenceBundle? Bundle)
{
    public static EvidenceBundleResult Created(EvidenceBundle bundle) => new(true, "created", bundle);
    public static EvidenceBundleResult Rejected(string reasonCode) => new(false, reasonCode, null);
}

public interface IHealingEvidenceSource
{
    ValueTask<EvidenceSourceSnapshot> ReadAsync(
        EvidenceSourceRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Append-only persistence boundary. Implementations must reject an existing bundle ID and must never expose
/// an update operation for an evidence bundle.
/// </summary>
public interface IHealingEvidenceStore
{
    ValueTask<bool> TryAppendBundleAsync(EvidenceBundle bundle, CancellationToken cancellationToken = default);
    ValueTask<bool> TryAppendElevatedBundleAsync(
        EvidenceBundle bundle,
        EvidenceAccessDecision decision,
        Guid targetAttemptId,
        Guid expectedBaseBundleId,
        CancellationToken cancellationToken = default);
    ValueTask<EvidenceBundle?> FindBundleAsync(Guid workspaceId, Guid bundleId, CancellationToken cancellationToken = default);
    ValueTask AppendAccessDecisionAsync(EvidenceAccessDecision decision, CancellationToken cancellationToken = default);
}

public interface IHealingEvidenceElevationAuthorizer
{
    ValueTask<EvidenceElevationAuthorization> AuthorizeAsync(
        EvidenceElevationAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed partial class HealingEvidenceService(
    IHealingEvidenceStore store,
    IHealingEvidenceSource source,
    IHealingEvidenceElevationAuthorizer elevationAuthorizer,
    TimeProvider? timeProvider = null)
{
    public const int MaximumBundleBytes = 32 * 1024;
    private const int MaximumFieldBytes = 8 * 1024;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);
    private static readonly IReadOnlySet<EvidenceField> DefaultFields = new[]
    {
        EvidenceField.ExceptionType,
        EvidenceField.OperationName,
        EvidenceField.NormalizedStack,
        EvidenceField.OccurrenceWindow,
        EvidenceField.AffectedEnvironments,
        EvidenceField.ProducingRevisions,
        EvidenceField.ComponentAttribution
    }.ToFrozenSet();
    private static readonly IReadOnlySet<EvidenceField> ElevatableFields = new[]
    {
        EvidenceField.TraceCorrelation,
        EvidenceField.SafeAttributes
    }.ToFrozenSet();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<EvidenceBundleResult> CreateDefaultAsync(
        EvidenceBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(request.WorkspaceId, request.ApplicationId, request.IncidentId);

        var snapshot = await source.ReadAsync(
            new EvidenceSourceRequest(request.WorkspaceId, request.ApplicationId, request.IncidentId, DefaultFields),
            cancellationToken);
        var bundle = CreateBundle(
            request.WorkspaceId,
            request.ApplicationId,
            request.IncidentId,
            EvidenceTier.DefaultRedacted,
            new Dictionary<string, string?>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            snapshot,
            DefaultFields);

        return await store.TryAppendBundleAsync(bundle, cancellationToken)
            ? EvidenceBundleResult.Created(bundle)
            : EvidenceBundleResult.Rejected("bundle-conflict");
    }

    public async ValueTask<EvidenceBundleResult> ElevateAsync(
        EvidenceElevationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateElevationRequest(request);
        var now = _timeProvider.GetUtcNow();
        var baseBundle = await store.FindBundleAsync(request.WorkspaceId, request.BaseBundleId, cancellationToken);
        if (baseBundle is null
            || baseBundle.ApplicationId != request.ApplicationId
            || baseBundle.IncidentId != request.IncidentId
            || baseBundle.Tier != EvidenceTier.DefaultRedacted
            || baseBundle.ExpiresAt <= now)
        {
            await AppendDecisionAsync(request, false, null, null, ["invalid-or-expired-base-bundle"], now, cancellationToken);
            return EvidenceBundleResult.Rejected("invalid-or-expired-base-bundle");
        }

        if (request.RequestedFields.Count == 0 || request.RequestedFields.Any(x => !ElevatableFields.Contains(x)))
        {
            await AppendDecisionAsync(request, false, null, null, ["field-not-elevatable"], now, cancellationToken);
            return EvidenceBundleResult.Rejected("field-not-elevatable");
        }

        var authorization = await elevationAuthorizer.AuthorizeAsync(
            new EvidenceElevationAuthorizationRequest(
                request.WorkspaceId,
                request.ApplicationId,
                request.IncidentId,
                request.BaseBundleId,
                request.TargetAttemptId,
                request.RequesterId,
                request.Purpose,
                request.RequestedFields),
            cancellationToken);
        var authorizationReasonsAreSafe = authorization.ReasonCodes.Count > 0
                                          && authorization.ReasonCodes.All(IsSafeCode);
        if (!authorization.Authorized
            || string.IsNullOrWhiteSpace(authorization.ApprovedBy)
            || authorization.ApprovedBy.Length > 256
            || !authorizationReasonsAreSafe)
        {
            IReadOnlyList<string> reasons = authorizationReasonsAreSafe
                ? authorization.ReasonCodes
                : ["invalid-authorization-decision"];
            await AppendDecisionAsync(request, false, null, null, reasons, now, cancellationToken);
            return EvidenceBundleResult.Rejected(reasons[0]);
        }

        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(baseBundle.CanonicalJson)
                     ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        var baseProvenance = JsonSerializer.Deserialize<Dictionary<string, string>>(baseBundle.ProvenanceJson)
                             ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshot = await source.ReadAsync(
            new EvidenceSourceRequest(
                request.WorkspaceId,
                request.ApplicationId,
                request.IncidentId,
                request.RequestedFields),
            cancellationToken);
        var elevated = CreateBundle(
            request.WorkspaceId,
            request.ApplicationId,
            request.IncidentId,
            EvidenceTier.Elevated,
            values,
            baseProvenance,
            snapshot,
            request.RequestedFields);

        var approvedDecision = CreateDecision(
            request,
            true,
            elevated.Id,
            authorization.ApprovedBy,
            authorization.ReasonCodes.Count == 0 ? ["authorized-elevation"] : authorization.ReasonCodes,
            now);
        if (!await store.TryAppendElevatedBundleAsync(
                elevated,
                approvedDecision,
                request.TargetAttemptId,
                request.BaseBundleId,
                cancellationToken))
        {
            await AppendDecisionAsync(request, false, null, authorization.ApprovedBy, ["bundle-conflict"], now, cancellationToken);
            return EvidenceBundleResult.Rejected("bundle-conflict");
        }

        return EvidenceBundleResult.Created(elevated);
    }

    private EvidenceBundle CreateBundle(
        Guid workspaceId,
        Guid applicationId,
        Guid incidentId,
        EvidenceTier tier,
        IDictionary<string, string?> initialValues,
        IDictionary<string, string> initialProvenance,
        EvidenceSourceSnapshot snapshot,
        IReadOnlySet<EvidenceField> permittedFields)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = new SortedDictionary<string, string?>(initialValues, StringComparer.Ordinal);
        var provenance = new SortedDictionary<string, string>(initialProvenance, StringComparer.Ordinal);
        var omissions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var field in permittedFields.OrderBy(FieldName, StringComparer.Ordinal))
        {
            var name = FieldName(field);
            if (!snapshot.Values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            {
                omissions.Add(name);
                continue;
            }

            var redacted = Redact(value);
            if (Encoding.UTF8.GetByteCount(redacted) > MaximumFieldBytes)
            {
                omissions.Add(name);
                continue;
            }

            values[name] = redacted;
            provenance[name] = snapshot.Provenance.TryGetValue(field, out var origin) && IsSafeCode(origin)
                ? origin
                : "platform-redacted";
        }

        foreach (var omitted in Enum.GetValues<EvidenceField>().Where(x => !values.ContainsKey(FieldName(x))))
            omissions.Add(FieldName(omitted));

        var canonicalJson = JsonSerializer.Serialize(values);
        var size = Encoding.UTF8.GetByteCount(canonicalJson);
        if (size > MaximumBundleBytes)
            throw new InvalidOperationException("The minimized evidence bundle exceeds the configured byte limit.");

        var createdAt = _timeProvider.GetUtcNow();
        return new EvidenceBundle
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            IncidentId = incidentId,
            Tier = tier,
            CanonicalJson = canonicalJson,
            Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))),
            ProvenanceJson = JsonSerializer.Serialize(provenance),
            OmissionsJson = JsonSerializer.Serialize(omissions),
            SizeBytes = size,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.Add(DefaultLifetime)
        };
    }

    private ValueTask AppendDecisionAsync(
        EvidenceElevationRequest request,
        bool authorized,
        Guid? releasedBundleId,
        string? approvedBy,
        IReadOnlyList<string> reasonCodes,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken) =>
        store.AppendAccessDecisionAsync(
            CreateDecision(request, authorized, releasedBundleId, approvedBy, reasonCodes, decidedAt),
            cancellationToken);

    private static EvidenceAccessDecision CreateDecision(
        EvidenceElevationRequest request,
        bool authorized,
        Guid? releasedBundleId,
        string? approvedBy,
        IReadOnlyList<string> reasonCodes,
        DateTimeOffset decidedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = request.WorkspaceId,
            ApplicationId = request.ApplicationId,
            IncidentId = request.IncidentId,
            ReleasedBundleId = releasedBundleId,
            RequesterId = request.RequesterId,
            RequestedTier = EvidenceTier.Elevated,
            RequestedFieldsJson = JsonSerializer.Serialize(request.RequestedFields.Select(FieldName).Order(StringComparer.Ordinal)),
            Purpose = request.Purpose,
            Authorized = authorized,
            ReasonCodesJson = JsonSerializer.Serialize(reasonCodes),
            ApprovedBy = approvedBy,
            DecidedAt = decidedAt
        };

    private static string Redact(string value) =>
        SecretAssignmentRegex().Replace(BearerRegex().Replace(value, "$1[REDACTED]"), "$1[REDACTED]");

    private static string FieldName(EvidenceField field) => field switch
    {
        EvidenceField.ExceptionType => "exceptionType",
        EvidenceField.OperationName => "operationName",
        EvidenceField.NormalizedStack => "normalizedStack",
        EvidenceField.OccurrenceWindow => "occurrenceWindow",
        EvidenceField.AffectedEnvironments => "affectedEnvironments",
        EvidenceField.ProducingRevisions => "producingRevisions",
        EvidenceField.ComponentAttribution => "componentAttribution",
        EvidenceField.TraceCorrelation => "traceCorrelation",
        EvidenceField.SafeAttributes => "safeAttributes",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static bool IsSafeCode(string value) =>
        value.Length is > 0 and <= 128
        && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.' or '/' or ':');

    private static void ValidateScope(Guid workspaceId, Guid applicationId, Guid incidentId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
        if (applicationId == Guid.Empty) throw new ArgumentException("ApplicationId is required.", nameof(applicationId));
        if (incidentId == Guid.Empty) throw new ArgumentException("IncidentId is required.", nameof(incidentId));
    }

    private static void ValidateElevationRequest(EvidenceElevationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(request.WorkspaceId, request.ApplicationId, request.IncidentId);
        if (request.BaseBundleId == Guid.Empty) throw new ArgumentException("BaseBundleId is required.", nameof(request));
        if (request.TargetAttemptId == Guid.Empty) throw new ArgumentException("TargetAttemptId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RequesterId) || request.RequesterId.Length > 256)
            throw new ArgumentException("RequesterId is required and must not exceed 256 characters.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 512)
            throw new ArgumentException("Purpose is required and must not exceed 512 characters.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.RequestedFields);
    }

    [GeneratedRegex("(?i)(bearer\\s+)[A-Za-z0-9._~+/-]+")]
    private static partial Regex BearerRegex();

    [GeneratedRegex("(?i)((?:password|secret|token|authorization|cookie)\\s*[=:]\\s*)[^,;\\s]+")]
    private static partial Regex SecretAssignmentRegex();
}
