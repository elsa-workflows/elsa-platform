using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ValenceControl.Healing.Abstractions;

namespace ValenceControl.Healing.Core.Incidents;

public static class HealingSignalNormalizationReasonCodes
{
    public const string UnsupportedProfileVersion = "unsupported-profile-version";
    public const string ApplicationIdRequired = "application-id-required";
    public const string EnvironmentIdRequired = "environment-id-required";
    public const string TimestampRequired = "timestamp-required";
    public const string ServiceNameRequired = "service-name-required";
    public const string OperationNameRequired = "operation-name-required";
    public const string ExceptionTypeRequired = "exception-type-required";
    public const string ExceptionFramesRequired = "exception-frames-required";
    public const string SeverityRequired = "severity-required";
    public const string InvalidRetryState = "invalid-retry-state";
    public const string EvidenceRequired = "evidence-required";
    public const string EvidenceNotRedacted = "evidence-not-redacted";
}

public sealed record NormalizedHealingFrame(string? AssemblyName, string TypeName, string MethodName);

public sealed record NormalizedHealingSignal(
    HealingSignal Source,
    string ServiceName,
    string ResourceIdentity,
    string OperationName,
    string FailureClass,
    IncidentRetryState RetryState,
    IncidentSeverity Severity,
    string ExceptionType,
    IReadOnlyList<NormalizedHealingFrame> Frames,
    string OccurrenceKey)
{
    public Guid ApplicationId => Source.ApplicationId;
    public Guid EnvironmentId => Source.EnvironmentId;
    public Guid? RevisionId => Source.RevisionId;
    public DateTimeOffset OccurredAt => Source.OccurredAt;
    public HealingTraceContext? Trace => Source.Trace;
}

public sealed record HealingSignalNormalizationResult(
    bool Succeeded,
    NormalizedHealingSignal? Signal,
    IReadOnlyList<string> ReasonCodes)
{
    public static HealingSignalNormalizationResult Accepted(NormalizedHealingSignal signal) =>
        new(true, signal, []);

    public static HealingSignalNormalizationResult Rejected(IEnumerable<string> reasonCodes) =>
        new(false, null, reasonCodes.Distinct(StringComparer.Ordinal).ToArray());
}

public sealed class HealingSignalNormalizer
{
    private const int MaxServiceNameLength = 256;
    private const int MaxResourceIdentityLength = 512;
    private const int MaxOperationNameLength = 512;
    private const int MaxExceptionTypeLength = 1024;
    private const int MaxExceptionMessageLength = 4096;
    private const int MaxStackTraceLength = 65_536;
    private const int MaxFrameValueLength = 1024;
    private const int MaxFrames = 64;
    private const int MaxCorrelationIdLength = 128;
    private const int MaxProfileValueLength = 512;

    private static readonly Regex DotNetFrame = new(
        @"^\s*at\s+(?<member>[^\s(]+)\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public HealingSignalNormalizationResult Normalize(HealingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var reasons = Validate(signal);
        if (reasons.Count > 0)
            return HealingSignalNormalizationResult.Rejected(reasons);

        var serviceName = Bound(signal.ServiceName!, MaxServiceNameLength)!;
        var resourceIdentity = Bound(signal.ResourceIdentity, MaxResourceIdentityLength) ?? $"service.name:{serviceName}";
        var operationName = Bound(signal.OperationName, MaxOperationNameLength)!;
        var exceptionType = Bound(signal.Exception.Type, MaxExceptionTypeLength)!;
        var frames = NormalizeFrames(signal.Exception);
        if (frames.Count == 0)
            return HealingSignalNormalizationResult.Rejected([HealingSignalNormalizationReasonCodes.ExceptionFramesRequired]);

        var retryState = ParseRetryState(signal.RetryState);
        var severity = ParseSeverity(signal.Severity!);
        var failureClass = Bound(signal.FailureClass, MaxProfileValueLength)?.ToLowerInvariant() ?? HealingFailureClasses.Unknown;
        var truncations = GetTruncations(signal);
        var evidence = truncations.Count == 0
            ? signal.Evidence
            : signal.Evidence with
            {
                IsTruncated = true,
                OmittedFields = signal.Evidence.OmittedFields
                    .Concat(truncations)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            };
        var source = signal with
        {
            OccurredAt = signal.OccurredAt.ToUniversalTime(),
            OperationName = operationName,
            FailureClass = failureClass,
            RetryState = ToProfileValue(retryState),
            Exception = signal.Exception with
            {
                Type = exceptionType,
                Message = Bound(signal.Exception.Message, MaxExceptionMessageLength),
                StackTrace = Bound(signal.Exception.StackTrace, MaxStackTraceLength),
                Frames = frames.Select(x => new HealingExceptionFrame(x.AssemblyName, x.TypeName, x.MethodName, null, null)).ToArray()
            },
            Evidence = evidence,
            OccurrenceId = Bound(signal.OccurrenceId, MaxProfileValueLength),
            SourceRevision = Bound(signal.SourceRevision, MaxProfileValueLength),
            ComponentManifestDigest = Bound(signal.ComponentManifestDigest, MaxProfileValueLength),
            ComponentKey = Bound(signal.ComponentKey, MaxProfileValueLength),
            WorkflowDefinitionId = Bound(signal.WorkflowDefinitionId, MaxProfileValueLength),
            WorkflowActivityType = Bound(signal.WorkflowActivityType, MaxProfileValueLength),
            Trace = signal.Trace is null
                ? null
                : new HealingTraceContext(
                    Bound(signal.Trace.TraceId, MaxCorrelationIdLength)?.ToLowerInvariant(),
                    Bound(signal.Trace.SpanId, MaxCorrelationIdLength)?.ToLowerInvariant()),
            ServiceName = serviceName,
            ResourceIdentity = resourceIdentity,
            Severity = ToProfileValue(severity)
        };
        var normalized = new NormalizedHealingSignal(
            source,
            serviceName,
            resourceIdentity,
            operationName,
            failureClass,
            retryState,
            severity,
            exceptionType,
            frames,
            ComputeOccurrenceKey(source, resourceIdentity, frames));
        return HealingSignalNormalizationResult.Accepted(normalized);
    }

    private static IReadOnlyList<string> GetTruncations(HealingSignal signal)
    {
        var result = new List<string>();
        AddIfTooLong(result, signal.ServiceName, MaxServiceNameLength, "service.name:truncated");
        AddIfTooLong(result, signal.ResourceIdentity, MaxResourceIdentityLength, "resource.identity:truncated");
        AddIfTooLong(result, signal.OperationName, MaxOperationNameLength, "valence.control.healing.operation.name:truncated");
        AddIfTooLong(result, signal.Exception.Type, MaxExceptionTypeLength, "exception.type:truncated");
        AddIfTooLong(result, signal.Exception.Message, MaxExceptionMessageLength, "exception.message:truncated");
        AddIfTooLong(result, signal.Exception.StackTrace, MaxStackTraceLength, "exception.stacktrace:truncated");
        if (signal.Exception.Frames.Count > MaxFrames)
            result.Add("exception.frames:truncated");
        return result;
    }

    private static void AddIfTooLong(List<string> result, string? value, int maxLength, string field)
    {
        if (value?.Trim().Length > maxLength)
            result.Add(field);
    }

    public static IReadOnlyList<NormalizedHealingFrame> NormalizeFrames(HealingExceptionEvidence exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var structured = (exception.Frames ?? [])
            .Take(MaxFrames)
            .Select(NormalizeFrame)
            .Where(x => x is not null)
            .Cast<NormalizedHealingFrame>()
            .ToArray();
        if (structured.Length > 0)
            return structured;

        if (string.IsNullOrWhiteSpace(exception.StackTrace))
            return [];

        var frames = new List<NormalizedHealingFrame>();
        foreach (var line in exception.StackTrace.Split('\n'))
        {
            if (frames.Count == MaxFrames)
                break;

            var match = DotNetFrame.Match(line);
            if (!match.Success)
                continue;

            var member = match.Groups["member"].Value;
            var separator = member.LastIndexOf('.');
            if (separator <= 0 || separator == member.Length - 1)
                continue;

            var typeName = Bound(member[..separator], MaxFrameValueLength);
            var methodName = Bound(member[(separator + 1)..], MaxFrameValueLength);
            if (typeName is not null && methodName is not null)
                frames.Add(new NormalizedHealingFrame(null, typeName, methodName));
        }

        return frames;
    }

    private static List<string> Validate(HealingSignal signal)
    {
        var reasons = new List<string>();
        if (!HealingContractVersion.IsCompatible(HealingContractVersions.SignalProfile, signal.ProfileVersion))
            reasons.Add(HealingSignalNormalizationReasonCodes.UnsupportedProfileVersion);
        if (signal.ApplicationId == Guid.Empty)
            reasons.Add(HealingSignalNormalizationReasonCodes.ApplicationIdRequired);
        if (signal.EnvironmentId == Guid.Empty)
            reasons.Add(HealingSignalNormalizationReasonCodes.EnvironmentIdRequired);
        if (signal.OccurredAt == default)
            reasons.Add(HealingSignalNormalizationReasonCodes.TimestampRequired);
        if (string.IsNullOrWhiteSpace(signal.ServiceName))
            reasons.Add(HealingSignalNormalizationReasonCodes.ServiceNameRequired);
        if (string.IsNullOrWhiteSpace(signal.OperationName))
            reasons.Add(HealingSignalNormalizationReasonCodes.OperationNameRequired);
        if (signal.Exception is null || string.IsNullOrWhiteSpace(signal.Exception.Type))
            reasons.Add(HealingSignalNormalizationReasonCodes.ExceptionTypeRequired);
        if (string.IsNullOrWhiteSpace(signal.Severity))
            reasons.Add(HealingSignalNormalizationReasonCodes.SeverityRequired);
        if (!TryParseRetryState(signal.RetryState, out _))
            reasons.Add(HealingSignalNormalizationReasonCodes.InvalidRetryState);
        if (signal.Evidence is null)
            reasons.Add(HealingSignalNormalizationReasonCodes.EvidenceRequired);
        else if (!signal.Evidence.IsRedacted)
            reasons.Add(HealingSignalNormalizationReasonCodes.EvidenceNotRedacted);
        return reasons;
    }

    private static NormalizedHealingFrame? NormalizeFrame(HealingExceptionFrame frame)
    {
        var assemblyName = Bound(frame.AssemblyName, MaxFrameValueLength);
        if (assemblyName is not null && assemblyName.IndexOf(',') is var separator and > 0)
            assemblyName = assemblyName[..separator].Trim();
        var typeName = Bound(frame.TypeName, MaxFrameValueLength);
        var methodName = Bound(frame.MethodName, MaxFrameValueLength);
        if (typeName is null || methodName is null)
            return null;

        var parameterStart = methodName.IndexOf('(');
        if (parameterStart > 0)
            methodName = methodName[..parameterStart].Trim();
        return methodName.Length == 0 ? null : new NormalizedHealingFrame(assemblyName, typeName, methodName);
    }

    private static string ComputeOccurrenceKey(
        HealingSignal signal,
        string resourceIdentity,
        IReadOnlyList<NormalizedHealingFrame> frames)
    {
        var material = new StringBuilder("occurrence-v1");
        Append(material, signal.ApplicationId.ToString("N"));
        if (!string.IsNullOrWhiteSpace(signal.OccurrenceId))
        {
            Append(material, signal.OccurrenceId!);
            return Hash(material.ToString());
        }

        Append(material, signal.Trace?.TraceId ?? string.Empty);
        Append(material, signal.Trace?.SpanId ?? string.Empty);
        Append(material, signal.OccurredAt.ToUniversalTime().ToString("O"));
        Append(material, resourceIdentity);
        Append(material, signal.OperationName);
        Append(material, signal.Exception.Type);
        foreach (var frame in frames)
        {
            Append(material, frame.AssemblyName ?? string.Empty);
            Append(material, frame.TypeName);
            Append(material, frame.MethodName);
        }

        return Hash(material.ToString());
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append('|').Append(value.Length).Append(':').Append(value);

    private static string Hash(string value) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static string? Bound(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static IncidentRetryState ParseRetryState(string value) =>
        TryParseRetryState(value, out var parsed) ? parsed : IncidentRetryState.None;

    private static bool TryParseRetryState(string? value, out IncidentRetryState retryState)
    {
        retryState = value?.Trim().ToLowerInvariant() switch
        {
            HealingRetryStates.None => IncidentRetryState.None,
            HealingRetryStates.Retrying => IncidentRetryState.Retrying,
            HealingRetryStates.Exhausted => IncidentRetryState.Exhausted,
            _ => (IncidentRetryState)(-1)
        };
        return Enum.IsDefined(retryState);
    }

    private static IncidentSeverity ParseSeverity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "fatal" or "critical" => IncidentSeverity.Fatal,
        "error" => IncidentSeverity.Error,
        "warning" or "warn" => IncidentSeverity.Warning,
        _ => IncidentSeverity.Informational
    };

    private static string ToProfileValue(IncidentRetryState value) => value switch
    {
        IncidentRetryState.Retrying => HealingRetryStates.Retrying,
        IncidentRetryState.Exhausted => HealingRetryStates.Exhausted,
        _ => HealingRetryStates.None
    };

    private static string ToProfileValue(IncidentSeverity value) => value switch
    {
        IncidentSeverity.Fatal => "fatal",
        IncidentSeverity.Error => "error",
        IncidentSeverity.Warning => "warning",
        _ => "informational"
    };
}
