using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.OpenTelemetry;
using ValenceControl.Healing.Core.Verification;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Healing.OpenTelemetry.Tests;

public sealed class HealingOpenTelemetryContributorTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ApplicationId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid EnvironmentId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset AcceptedAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Fact]
    public async Task Eligible_post_redaction_exception_log_is_durably_appended_before_contribution_completes()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);

        await contributor.ContributeAsync(CreateExceptionLogBatch(), TrustedContext());

        Assert.Single(appender.Items);
        var item = appender.Items.Single();
        Assert.Equal(WorkspaceId, item.WorkspaceId);
        Assert.Equal(ApplicationId, item.ApplicationId);
        Assert.Equal(EnvironmentId, item.EnvironmentId);
        Assert.Equal(HealingSignalSource.OpenTelemetry, item.Source);
        Assert.Equal(AcceptedAt, item.AcceptedAt);
        Assert.Equal(HealingInboxStatus.Pending, item.Status);
        Assert.Equal("occurrence-42", item.IdempotencyKey);
        Assert.Matches("^[0-9a-f]{64}$", item.EnvelopeHash);

        var signal = JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson);
        Assert.NotNull(signal);
        Assert.Equal(ApplicationId, signal!.ApplicationId);
        Assert.Equal(EnvironmentId, signal.EnvironmentId);
        Assert.Equal("GET /orders/{id}", signal.OperationName);
        Assert.Equal("System.InvalidOperationException", signal.Exception.Type);
        Assert.Equal("safe redacted message", signal.Exception.Message);
        Assert.Contains("OrderService.Load", signal.Exception.StackTrace);
        Assert.True(signal.Evidence.IsRedacted);
        Assert.Equal(new HealingTraceContext("trace-1", "span-1"), signal.Trace);
        Assert.Equal("orders-api", signal.ServiceName);
        Assert.Equal("resource-1", signal.ResourceIdentity);
        Assert.Equal("Error", signal.Severity);
        Assert.DoesNotContain("must-never-enter-healing", item.RedactedEnvelopeJson);
    }

    [Fact]
    public async Task Retried_log_without_producer_occurrence_id_uses_stable_receiver_independent_identity()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);

        await contributor.ContributeAsync(CreateExceptionLogBatch(occurrenceId: null, receiverLogId: "receiver-id-1"), TrustedContext());
        await contributor.ContributeAsync(CreateExceptionLogBatch(occurrenceId: null, receiverLogId: "receiver-id-2"), TrustedContext());

        Assert.Equal(2, appender.Items.Count());
        Assert.Matches("^otel:v1:[0-9a-f]{64}$", Assert.Single(appender.Items.Select(x => x.IdempotencyKey).Distinct()));
        Assert.Single(appender.Items.Select(x => x.EnvelopeHash).Distinct());
    }

    [Fact]
    public async Task Standard_error_severity_number_without_text_remains_error_after_normalization()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);

        await contributor.ContributeAsync(
            CreateExceptionLogBatch(severityNumber: 17, severityText: string.Empty),
            TrustedContext());

        var item = Assert.Single(appender.Items);
        Assert.Equal("error", JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson)!.Severity);
    }

    [Fact]
    public async Task Contribution_waits_for_durable_append_and_propagates_its_failure()
    {
        var appender = new BlockingInboxAppender();
        var contributor = CreateContributor(appender);

        var contribution = contributor.ContributeAsync(CreateExceptionLogBatch(), TrustedContext()).AsTask();
        await appender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(contribution.IsCompleted);
        appender.Completion.SetException(new InvalidOperationException("database unavailable"));
        var act = () => contribution;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Equal("database unavailable", exception.Message);
    }

    [Fact]
    public async Task Error_span_exception_event_merges_resource_span_and_event_evidence()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);
        var resource = CreateResource(new Dictionary<string, string?>
        {
            [HealingSignalAttributes.ProfileVersion] = HealingContractVersions.SignalProfile,
            [HealingSignalAttributes.ApplicationId] = ApplicationId.ToString(),
            [HealingSignalAttributes.EnvironmentId] = EnvironmentId.ToString()
        });
        var occurredAt = DateTimeOffset.Parse("2026-07-16T11:58:00Z");
        var exceptionEvent = new TelemetrySpanEvent(
            "exception",
            occurredAt,
            new Dictionary<string, string?>
            {
                ["exception.type"] = "System.TimeoutException",
                ["exception.message"] = "redacted timeout",
                ["exception.stacktrace"] = "at Acme.Orders.Client.Send()"
            });
        var span = new TelemetrySpan(
            "span-record-1",
            "trace-2",
            "span-2",
            null,
            resource.Id,
            "orders.send",
            "Internal",
            occurredAt.AddSeconds(-1),
            occurredAt,
            SpanStatus.Error,
            "failed",
            new Dictionary<string, string?>
            {
                [HealingSignalAttributes.OperationName] = "orders.send",
                [HealingSignalAttributes.FailureClass] = HealingFailureClasses.FatalBackground,
                [HealingSignalAttributes.OccurrenceId] = "span-occurrence-1"
            },
            [exceptionEvent],
            []);

        await contributor.ContributeAsync(new([resource], [], [span], [], [], []), TrustedContext());

        var item = Assert.Single(appender.Items);
        Assert.Equal("span-occurrence-1", item.IdempotencyKey);
        Assert.Equal(occurredAt, item.OccurredAt);
        var signal = JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson)!;
        Assert.Equal("System.TimeoutException", signal.Exception.Type);
        Assert.Equal("redacted timeout", signal.Exception.Message);
        Assert.Equal("orders.send", signal.OperationName);
        Assert.Equal(new HealingTraceContext("trace-2", "span-2"), signal.Trace);
        Assert.Equal("orders-api", signal.ServiceName);
        Assert.Equal("Error", signal.Severity);
    }

    [Fact]
    public async Task Authenticated_positive_affected_operation_span_advances_the_matching_active_verification()
    {
        var appender = new RecordingInboxAppender();
        var scope = VerificationScope();
        var store = new RecordingVerificationStore(scope);
        var verification = new HealingVerificationService(store, new FixedTimeProvider(AcceptedAt));
        var contributor = CreateContributorWithResolver(
            appender,
            new StaticScopeResolver(new HealingTelemetryScope(WorkspaceId, ApplicationId, EnvironmentId)),
            verification);
        var resource = CreateResource(new Dictionary<string, string?>
        {
            [HealingSignalAttributes.ApplicationId] = ApplicationId.ToString(),
            [HealingSignalAttributes.EnvironmentId] = EnvironmentId.ToString()
        });
        var span = new TelemetrySpan(
            "positive-span-1", "trace-positive", "span-positive", null, resource.Id,
            "GET /orders/{id}", "Server", AcceptedAt.AddMinutes(-2), AcceptedAt.AddMinutes(-1),
            SpanStatus.Ok, null,
            new Dictionary<string, string?>
            {
                [HealingSignalAttributes.ApplicationId] = ApplicationId.ToString(),
                [HealingSignalAttributes.EnvironmentId] = EnvironmentId.ToString(),
                [HealingSignalAttributes.ProfileVersion] = HealingContractVersions.SignalProfile,
                [HealingSignalAttributes.OperationName] = "GET /orders/{id}",
                [HealingSignalAttributes.SourceRevision] = scope.RepairedRevision,
                [HealingSignalAttributes.VerificationAffectedOperation] = "true"
            }, [], []);

        await contributor.ContributeAsync(new([resource], [], [span], [], [], []), TrustedContext());

        Assert.Equal(1, scope.Verification!.RelevantOperationSuccessCount);
        Assert.Equal(AcceptedAt.AddMinutes(-1), scope.Verification.LastRelevantOperationSuccessAt);
        Assert.Empty(appender.Items);
    }

    [Fact]
    public async Task Incomplete_non_error_untrusted_or_cross_application_evidence_is_not_appended()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);
        var nonError = CreateExceptionLogBatch(severityNumber: null, severityText: "Info");
        var incomplete = CreateExceptionLogBatch();
        incomplete.Logs.Single().Attributes.Remove("exception.stacktrace");
        var mismatched = CreateExceptionLogBatch();
        mismatched.Logs.Single().Attributes[HealingSignalAttributes.ApplicationId] = Guid.NewGuid().ToString();
        var missingService = CreateExceptionLogBatch(serviceName: " ");

        await contributor.ContributeAsync(nonError, TrustedContext());
        await contributor.ContributeAsync(incomplete, TrustedContext());
        await contributor.ContributeAsync(mismatched, TrustedContext());
        await contributor.ContributeAsync(missingService, TrustedContext());
        await CreateContributor(appender, null).ContributeAsync(CreateExceptionLogBatch(), TrustedContext());

        Assert.Empty(appender.Items);
    }

    [Fact]
    public async Task Oversized_redacted_evidence_is_bounded_and_records_truncation()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);
        var batch = CreateExceptionLogBatch(occurrenceId: new string('o', 300));
        batch.Logs.Single().Attributes["exception.message"] = new string('m', 20_000);
        batch.Logs.Single().Attributes["exception.stacktrace"] = new string('s', 200_000);
        batch.Logs.Single().Attributes[HealingSignalAttributes.ComponentKey] = new string('c', 10_000);

        await contributor.ContributeAsync(batch, TrustedContext());

        var item = Assert.Single(appender.Items);
        Assert.Matches("^otel:v1:[0-9a-f]{64}$", item.IdempotencyKey);
        Assert.True(item.RedactedEnvelopeJson.Length < 262_144);
        var signal = JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson)!;
        Assert.Equal(8_192, Assert.IsType<string>(signal.Exception.Message).Length);
        Assert.Equal(131_072, Assert.IsType<string>(signal.Exception.StackTrace).Length);
        Assert.Equal(2_048, Assert.IsType<string>(signal.ComponentKey).Length);
        Assert.True(signal.Evidence.IsTruncated);
        Assert.Equivalent(new[]
        {
            HealingSignalAttributes.OccurrenceId,
            HealingSignalAttributes.ComponentKey,
            "exception.message",
            "exception.stacktrace"
        }, signal.Evidence.OmittedFields);
    }

    [Fact]
    public async Task Authenticated_context_claims_are_the_only_source_of_healing_scope()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributorWithResolver(appender, new AuthenticatedClaimHealingTelemetryScopeResolver());
        var contextApplicationId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var contextEnvironmentId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var context = OpenTelemetryIngestionContext.Authenticated(
            "deployment-credential:orders-api",
            new Dictionary<string, string>
            {
                [HealingTelemetryScopeClaims.WorkspaceId] = WorkspaceId.ToString(),
                [HealingTelemetryScopeClaims.ApplicationId] = contextApplicationId.ToString(),
                [HealingTelemetryScopeClaims.EnvironmentId] = contextEnvironmentId.ToString()
            });
        var batch = CreateExceptionLogBatch();
        batch.Logs.Single().Attributes.Remove(HealingSignalAttributes.ApplicationId);
        batch.Logs.Single().Attributes.Remove(HealingSignalAttributes.EnvironmentId);

        await contributor.ContributeAsync(batch, context);

        var item = Assert.Single(appender.Items);
        Assert.Equal(WorkspaceId, item.WorkspaceId);
        Assert.Equal(contextApplicationId, item.ApplicationId);
        Assert.Equal(contextEnvironmentId, item.EnvironmentId);
        var signal = JsonSerializer.Deserialize<HealingSignal>(item.RedactedEnvelopeJson)!;
        Assert.Equal(contextApplicationId, signal.ApplicationId);
        Assert.Equal(contextEnvironmentId, signal.EnvironmentId);
    }

    [Fact]
    public async Task Untrusted_and_Foundation_global_key_contexts_cannot_append_even_if_a_resolver_returns_scope()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributor(appender);

        await contributor.ContributeAsync(CreateExceptionLogBatch(), OpenTelemetryIngestionContext.Untrusted);
        await contributor.ContributeAsync(
            CreateExceptionLogBatch(),
            OpenTelemetryIngestionContext.Authenticated("elsa:otlp:configured-api-key"));

        Assert.Empty(appender.Items);
    }

    [Fact]
    public async Task Forged_telemetry_scope_attributes_are_rejected_against_authenticated_context_scope()
    {
        var appender = new RecordingInboxAppender();
        var contributor = CreateContributorWithResolver(appender, new AuthenticatedClaimHealingTelemetryScopeResolver());
        var trustedApplicationId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var trustedEnvironmentId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var context = OpenTelemetryIngestionContext.Authenticated(
            "deployment-credential:orders-api",
            new Dictionary<string, string>
            {
                [HealingTelemetryScopeClaims.WorkspaceId] = WorkspaceId.ToString(),
                [HealingTelemetryScopeClaims.ApplicationId] = trustedApplicationId.ToString(),
                [HealingTelemetryScopeClaims.EnvironmentId] = trustedEnvironmentId.ToString()
            });

        await contributor.ContributeAsync(CreateExceptionLogBatch(), context);

        Assert.Empty(appender.Items);
    }

    private static HealingOpenTelemetryIngestionContributor CreateContributor(IHealingSignalInboxAppender appender) =>
        CreateContributor(appender, new HealingTelemetryScope(WorkspaceId, ApplicationId, EnvironmentId));

    private static HealingOpenTelemetryIngestionContributor CreateContributor(
        IHealingSignalInboxAppender appender,
        HealingTelemetryScope? trustedScope)
    {
        var resolver = new StaticScopeResolver(trustedScope);
        return CreateContributorWithResolver(appender, resolver);
    }

    private static HealingOpenTelemetryIngestionContributor CreateContributorWithResolver(
        IHealingSignalInboxAppender appender,
        IHealingTelemetryScopeResolver resolver,
        HealingVerificationService? verification = null)
    {
        var serviceProvider = new TestServiceProvider(resolver, appender, verification);
        return new HealingOpenTelemetryIngestionContributor(
            new TestServiceScopeFactory(serviceProvider),
            new FixedTimeProvider(AcceptedAt));
    }

    private static OpenTelemetryIngestionContext TrustedContext() =>
        OpenTelemetryIngestionContext.Authenticated("deployment-credential:orders-api");

    private static OpenTelemetryBatch CreateExceptionLogBatch(
        string? occurrenceId = "occurrence-42",
        string receiverLogId = "receiver-generated-id",
        int? severityNumber = 17,
        string severityText = "Error",
        string serviceName = "orders-api")
    {
        var resource = CreateResource(serviceName: serviceName);
        var log = new OtlpLogRecord(
            receiverLogId,
            resource.Id,
            DateTimeOffset.Parse("2026-07-16T11:59:00Z"),
            severityText,
            severityNumber,
            "safe redacted message",
            "trace-1",
            "span-1",
            new Dictionary<string, string?>
            {
                [HealingSignalAttributes.ProfileVersion] = HealingContractVersions.SignalProfile,
                [HealingSignalAttributes.ApplicationId] = ApplicationId.ToString(),
                [HealingSignalAttributes.EnvironmentId] = EnvironmentId.ToString(),
                [HealingSignalAttributes.OperationName] = "GET /orders/{id}",
                [HealingSignalAttributes.FailureClass] = HealingFailureClasses.UnhandledRequest,
                [HealingSignalAttributes.RetryState] = HealingRetryStates.None,
                ["password"] = "must-never-enter-healing",
                ["exception.type"] = "System.InvalidOperationException",
                ["exception.message"] = "safe redacted message",
                ["exception.stacktrace"] = "at Acme.Orders.OrderService.Load()"
            });
        if (occurrenceId is not null)
            log.Attributes[HealingSignalAttributes.OccurrenceId] = occurrenceId;
        return new([resource], [], [], [], [], [log]);
    }

    private static TelemetryResource CreateResource(
        IDictionary<string, string?>? attributes = null,
        string serviceName = "orders-api") => new(
        "resource-1",
        serviceName,
        "instance-7",
        "dotnet",
        attributes ?? new Dictionary<string, string?>(),
        AcceptedAt,
        TelemetryResourceStatus.Active);

    private sealed class RecordingInboxAppender : IHealingSignalInboxAppender
    {
        public List<HealingSignalInboxItem> Items { get; } = [];

        public ValueTask AppendAsync(HealingSignalInboxItem item, CancellationToken cancellationToken = default)
        {
            Items.Add(item);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingInboxAppender : IHealingSignalInboxAppender
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask AppendAsync(HealingSignalInboxItem item, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Completion.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class StaticScopeResolver(HealingTelemetryScope? scope) : IHealingTelemetryScopeResolver
    {
        public ValueTask<HealingTelemetryScope?> ResolveAsync(
            OpenTelemetryIngestionContext ingestionContext,
            TelemetryResource resource,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(scope);
    }

    private sealed class TestServiceScopeFactory(IServiceProvider serviceProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestServiceScope(serviceProvider);
    }

    private sealed class TestServiceScope(IServiceProvider serviceProvider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public void Dispose() { }
    }

    private sealed class TestServiceProvider(
        IHealingTelemetryScopeResolver resolver,
        IHealingSignalInboxAppender appender,
        HealingVerificationService? verification = null) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IHealingTelemetryScopeResolver) ? resolver :
            serviceType == typeof(IHealingSignalInboxAppender) ? appender :
            serviceType == typeof(HealingVerificationService) ? verification :
            null;
    }

    private static HealingVerificationScope VerificationScope()
    {
        var incident = new HealingIncident
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, ApplicationId = ApplicationId,
            Status = HealingIncidentStatus.Verifying
        };
        var episode = new IncidentEpisode
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, ApplicationId = ApplicationId,
            IncidentId = incident.Id, Outcome = IncidentEpisodeOutcome.Active
        };
        incident.ActiveEpisodeId = episode.Id;
        var impact = new EnvironmentImpact
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, ApplicationId = ApplicationId,
            EpisodeId = episode.Id, EnvironmentId = EnvironmentId,
            VerificationStatus = VerificationOutcome.DeployedUnverified
        };
        var result = new VerificationResult
        {
            Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, ApplicationId = ApplicationId,
            EpisodeId = episode.Id, EnvironmentId = EnvironmentId, RepairedRevision = "fixed-sha",
            WindowStartedAt = AcceptedAt.AddMinutes(-10), WindowEndsAt = AcceptedAt.AddMinutes(10),
            Outcome = VerificationOutcome.DeployedUnverified
        };
        return new HealingVerificationScope(incident, episode, impact,
            new HealingConfiguration { WorkspaceId = WorkspaceId, ApplicationId = ApplicationId, VerificationWindow = TimeSpan.FromMinutes(20) },
            result.RepairedRevision, result);
    }

    private sealed class RecordingVerificationStore(HealingVerificationScope scope) : IHealingVerificationStore
    {
        public ValueTask<HealingVerificationScope?> FindActiveScopeAsync(Guid workspaceId, Guid applicationId, Guid environmentId, string repairedRevision, string operationName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<HealingVerificationScope?>(scope.RepairedRevision == repairedRevision ? scope : null);
        public ValueTask SaveAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<EnvironmentImpact>> ListEpisodeImpactsAsync(Guid workspaceId, Guid applicationId, Guid episodeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<EnvironmentImpact>>([scope.EnvironmentImpact]);
        public ValueTask<IReadOnlyList<VerificationResult>> ListEpisodeVerificationsAsync(Guid workspaceId, Guid applicationId, Guid episodeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<VerificationResult>>([scope.Verification!]);
        public ValueTask<HealingVerificationScope?> GetEpisodeScopeAsync(Guid workspaceId, Guid applicationId, Guid episodeId, CancellationToken cancellationToken = default) => ValueTask.FromResult<HealingVerificationScope?>(scope);
        public ValueTask<HealingVerificationAppendResult<DeploymentObservation>> AppendDeploymentObservationAsync(DeploymentObservation observation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<VerificationResult> UpsertVerificationAsync(VerificationResult verification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<HealingVerificationScope>> ListDeploymentScopesAsync(Guid workspaceId, Guid applicationId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<HealingVerificationScope?> GetScopeAsync(Guid workspaceId, Guid episodeId, Guid environmentId, string repairedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<HealingVerificationScope?> FindScopeForOccurrenceAsync(IncidentOccurrence occurrence, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<HealingVerificationScope>> ListDueScopesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<HealingVerificationScope>> ListExpiredWaiverScopesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
