using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Healing.Abstractions;

namespace Elsa.Platform.Healing.Core.Reporting;

public sealed record HealingOverviewQuery(
    Guid WorkspaceId,
    Guid? ApplicationId = null,
    Guid? EnvironmentId = null,
    HealingIncidentStatus? Status = null,
    IncidentSeverity? Severity = null,
    bool? Repairable = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public sealed record HealingAuditReportQuery(
    Guid WorkspaceId,
    Guid? ApplicationId = null,
    Guid? IncidentId = null,
    string? Cursor = null,
    int Take = 50);

public sealed record HealingUsageQuery(
    Guid WorkspaceId,
    Guid? ApplicationId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

public sealed record HealingOverviewSource(
    IReadOnlyList<HealingConfiguration> Configurations,
    IReadOnlyList<HealingEnvironmentConfiguration> EnvironmentConfigurations,
    long OpenIncidents,
    IReadOnlyList<HealingNamedCount> IncidentStates,
    IReadOnlyList<HealingNamedCount> Severities,
    HealingRepairability Repairability,
    HealingRepairActivity RepairActivity,
    IReadOnlyList<HealingNamedCount> VerificationOutcomes,
    HealingUsageReport Usage,
    IReadOnlyList<HealingOverviewIncident> RecentIncidents);

public sealed record HealingAuditSourcePage(IReadOnlyList<HealingAuditEvent> Items, bool HasMore);
public sealed record HealingAuditCursor(long Sequence, Guid Id);

public interface IHealingReportingStore
{
    ValueTask<HealingOverviewSource> LoadOverviewAsync(
        HealingOverviewQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<HealingAuditSourcePage> LoadAuditAsync(
        Guid workspaceId,
        Guid? applicationId,
        Guid? incidentId,
        HealingAuditCursor? before,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record HealingNamedCount(string Name, long Count);
public sealed record HealingEnabledState(long Total, long Enabled, long Disabled, long Stopped);
public sealed record HealingRepairActivity(long ActiveAttempts, long BlockedAttempts, long OpenPullRequests, long BlockedPullRequests);
public sealed record HealingRepairability(long Repairable, long ObservationOnly);

public sealed record HealingUsageReport(
    DateTimeOffset? From,
    DateTimeOffset? To,
    long Attempts,
    long CompletedAttempts,
    long FailedAttempts,
    long InputUnits,
    long OutputUnits,
    double AgentDurationSeconds,
    double RepositoryRunDurationSeconds,
    long RepositoryRuns,
    long ProviderOperations,
    long FailedProviderOperations,
    long InferenceBudget,
    long RepositoryRunBudget,
    double TimeBudgetSeconds,
    long ConcurrencyBudget);

public sealed record HealingOverviewIncident(
    Guid Id,
    Guid ApplicationId,
    HealingIncidentStatus Status,
    IncidentSeverity Severity,
    IncidentClassification Classification,
    long OccurrenceCount,
    bool Repairable,
    DateTimeOffset LastSeenAt);

public sealed record HealingOverview(
    DateTimeOffset UpdatedAt,
    HealingEnabledState Applications,
    HealingEnabledState Environments,
    long OpenIncidents,
    IReadOnlyList<HealingNamedCount> IncidentStates,
    IReadOnlyList<HealingNamedCount> Severities,
    HealingRepairability Repairability,
    HealingRepairActivity RepairActivity,
    IReadOnlyList<HealingNamedCount> VerificationOutcomes,
    HealingUsageReport Usage,
    IReadOnlyList<HealingOverviewIncident> RecentIncidents,
    IReadOnlyList<string> Permissions);

public sealed record HealingAuditItem(
    Guid Id,
    long Sequence,
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
    IReadOnlyDictionary<string, string?> Details,
    DateTimeOffset OccurredAt);

public sealed record HealingAuditPage(IReadOnlyList<HealingAuditItem> Items, string? NextCursor);

public sealed class HealingReportingService(IHealingReportingStore store, TimeProvider? timeProvider = null)
{
    private const int MaximumAuditPageSize = 100;
    private const int MaximumOverviewWindowDays = 366;
    private static readonly IReadOnlySet<string> SafeAuditDetailKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "attemptCount", "attemptLimit", "environment", "gateReason", "operationType", "outcomeCode",
        "providerOutcome", "pullRequestNumber", "repositoryName", "repositoryOwner", "revision", "status",
        "verificationStatus"
    };
    private static readonly IReadOnlySet<string> NumericAuditDetailKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "attemptCount", "attemptLimit", "pullRequestNumber"
    };
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<HealingOverview> GetOverviewAsync(
        HealingOverviewQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(query.WorkspaceId);
        var window = NormalizeWindow(query.From, query.To);
        query = query with { From = window.From, To = window.To };
        var source = await store.LoadOverviewAsync(query, cancellationToken);

        return new(
            _timeProvider.GetUtcNow(),
            BuildApplicationState(source.Configurations),
            BuildEnvironmentState(source.Configurations, source.EnvironmentConfigurations),
            source.OpenIncidents,
            source.IncidentStates,
            source.Severities,
            source.Repairability,
            source.RepairActivity,
            source.VerificationOutcomes,
            source.Usage,
            source.RecentIncidents,
            []);
    }

    public async ValueTask<HealingAuditPage> GetAuditAsync(
        HealingAuditReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(query.WorkspaceId);
        if (query.Take is < 1 or > MaximumAuditPageSize)
            throw new ArgumentOutOfRangeException(nameof(query), $"Audit page size must be between 1 and {MaximumAuditPageSize}.");
        var before = DecodeCursor(query.Cursor);
        var page = await store.LoadAuditAsync(
            query.WorkspaceId, query.ApplicationId, query.IncidentId, before, query.Take, cancellationToken);
        var items = page.Items.Select(ProjectAuditItem).ToArray();
        var nextCursor = page.HasMore && items.Length > 0 ? EncodeCursor(items[^1].Sequence, items[^1].Id) : null;
        return new(items, nextCursor);
    }

    public async ValueTask<HealingUsageReport> GetUsageAsync(
        HealingUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(query.WorkspaceId);
        var window = NormalizeWindow(query.From, query.To);
        query = query with { From = window.From, To = window.To };
        var source = await store.LoadOverviewAsync(
            new(query.WorkspaceId, query.ApplicationId, From: query.From, To: query.To), cancellationToken);
        return source.Usage;
    }

    private static HealingEnabledState BuildApplicationState(IReadOnlyList<HealingConfiguration> configurations)
    {
        var stopped = configurations.LongCount(x => x.ApplicationKillSwitch);
        var enabled = configurations.LongCount(x => !x.ApplicationKillSwitch && (x.DiscoveryEnabled || x.RepairEnabled));
        return new(configurations.Count, enabled, configurations.Count - enabled - stopped, stopped);
    }

    private static HealingEnabledState BuildEnvironmentState(
        IReadOnlyList<HealingConfiguration> configurations,
        IReadOnlyList<HealingEnvironmentConfiguration> environments)
    {
        var applications = configurations.ToDictionary(x => x.ApplicationId);
        var stopped = environments.LongCount(x => x.EnvironmentKillSwitch ||
                                                    (applications.TryGetValue(x.ApplicationId, out var app) && app.ApplicationKillSwitch));
        var enabled = environments.LongCount(x =>
        {
            if (x.EnvironmentKillSwitch || !applications.TryGetValue(x.ApplicationId, out var app) || app.ApplicationKillSwitch)
                return false;
            return (x.DiscoveryEnabled ?? app.DiscoveryEnabled) || (x.RepairEnabled ?? app.RepairEnabled);
        });
        return new(environments.Count, enabled, environments.Count - enabled - stopped, stopped);
    }

    private static HealingAuditItem ProjectAuditItem(HealingAuditEvent item) => new(
        item.Id,
        item.Sequence,
        SafeCode(item.AggregateType),
        item.AggregateId,
        SafeCode(item.EventType),
        SafeCode(item.ReasonCode),
        SafeCode(item.ActorType),
        SafeActor(item.ActorId),
        item.CorrelationId,
        item.CausationId,
        SafeOptionalCode(item.PolicyVersion),
        SafeHash(item.InputHash),
        SafeHash(item.OutputHash),
        SafeDetails(item.SafeDetailJson),
        item.OccurredAt);

    private static IReadOnlyDictionary<string, string?> SafeDetails(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 3 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return EmptyDetails;
            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!SafeAuditDetailKeys.Contains(property.Name) || result.Count == 32)
                    continue;
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    result[property.Name] = null;
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;
                var value = property.Value.GetString();
                if (value is not null && IsSafeAuditDetailValue(property.Name, value))
                    result[property.Name] = value;
            }
            return result;
        }
        catch (JsonException)
        {
            return EmptyDetails;
        }
    }

    private static void ValidateScope(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("WorkspaceId is required.", nameof(workspaceId));
    }

    private (DateTimeOffset? From, DateTimeOffset? To) NormalizeWindow(DateTimeOffset? from, DateTimeOffset? to)
    {
        var now = _timeProvider.GetUtcNow();
        if (from is null && to is null)
            return (now.AddDays(-MaximumOverviewWindowDays), now);

        var maximumFuture = now.AddDays(1);
        if (from > maximumFuture || to > maximumFuture)
            throw new ArgumentException("The reporting window cannot be in the future.");
        var effectiveTo = to ?? now;
        var effectiveFrom = from ?? effectiveTo.AddDays(-MaximumOverviewWindowDays);
        if (effectiveFrom > effectiveTo)
            throw new ArgumentException("The reporting start must not be later than the end.");
        if (effectiveTo - effectiveFrom > TimeSpan.FromDays(MaximumOverviewWindowDays))
            throw new ArgumentException($"The reporting window cannot exceed {MaximumOverviewWindowDays} days.");
        return (effectiveFrom, effectiveTo);
    }

    private static HealingAuditCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = decoded.Split(':');
            if (parts.Length != 3 || parts[0] != "audit-v2" ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence < 1 ||
                !Guid.TryParseExact(parts[2], "N", out var id) || id == Guid.Empty)
            {
                throw new FormatException();
            }
            return new(sequence, id);
        }
        catch (FormatException)
        {
            throw new ArgumentException("The audit cursor is invalid.", nameof(cursor));
        }
    }

    private static string EncodeCursor(long sequence, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"audit-v2:{sequence.ToString(CultureInfo.InvariantCulture)}:{id:N}"));

    private static string SafeCode(string value) =>
        value.Length is > 0 and <= 256 && value.All(IsSafeCodeCharacter) && !LooksSensitive(value) ? value : "redacted";

    private static string? SafeOptionalCode(string? value) => value is null ? null : SafeCode(value);

    private static string SafeActor(string value) =>
        value.Length is > 0 and <= 256 && value.All(IsSafeActorCharacter) && !LooksSensitive(value) ? value : "redacted";

    private static string? SafeHash(string? value)
    {
        if (value is null)
            return null;
        var candidate = value.StartsWith("sha256:", StringComparison.Ordinal) ? value[7..] : value;
        return candidate.Length == 64 && candidate.All(char.IsAsciiHexDigit) ? value : null;
    }

    private static bool IsSafeCodeCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or '/' or ':';

    private static bool IsSafeActorCharacter(char value) => IsSafeCodeCharacter(value) || value is '@';
    private static bool IsSafeDetailCharacter(char value) => IsSafeActorCharacter(value);

    private static bool IsSafeAuditDetailValue(string key, string value)
    {
        if (NumericAuditDetailKeys.Contains(key))
            return value.Length is > 0 and <= 10 && value.All(char.IsAsciiDigit);
        if (key == "revision")
            return value.Length is >= 7 and <= 64 && value.All(char.IsAsciiHexDigit);
        return value.Length is > 0 and <= 128 && value.All(IsSafeDetailCharacter) && !LooksSensitive(value);
    }

    private static bool LooksSensitive(string value)
    {
        string[] markers = ["Bearer ", "AccountKey=", "Password=", "Secret=", "Token=", "AKIA", "ghp_", "github_pat_", "sk-"];
        if (markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;
        var jwtSegments = value.Split('.');
        if (jwtSegments.Length == 3 && jwtSegments.All(x => x.Length >= 8 && x.All(IsBase64UrlCharacter)))
            return true;
        return value.Length >= 24 && value.All(IsBase64UrlCharacter) && value.Any(char.IsAsciiDigit) &&
               value.Any(char.IsAsciiLetterLower) && value.Any(char.IsAsciiLetterUpper);
    }

    private static bool IsBase64UrlCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value is '-' or '_';

    private static readonly IReadOnlyDictionary<string, string?> EmptyDetails = new Dictionary<string, string?>();
}
