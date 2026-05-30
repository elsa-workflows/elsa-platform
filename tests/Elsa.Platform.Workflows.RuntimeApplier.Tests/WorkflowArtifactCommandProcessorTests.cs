using System.Text;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Artifacts;
using Elsa.Platform.Workflows.RuntimeApplier;
using FluentAssertions;

namespace Elsa.Platform.Workflows.RuntimeApplier.Tests;

public sealed class WorkflowArtifactCommandProcessorTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid EngineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CommandId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid ArtifactRecordId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-29T12:00:00Z");

    private readonly WorkflowArtifactRuntimeOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = WorkspaceId,
        EngineId = EngineId,
        WorkerId = "worker-a",
        RuntimeVersion = "4.0.0",
        ClaimLeaseDuration = TimeSpan.FromMinutes(5),
        HeartbeatInterval = TimeSpan.FromSeconds(30),
        LeaseSafetyMargin = TimeSpan.FromSeconds(15)
    };

    [Fact]
    public async Task Applies_valid_workflow_artifact_and_completes_platform_command()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var commands = new RecordingRuntimeCommandClient();
        var store = new InMemoryWorkflowDefinitionRuntimeStore();
        var processor = Processor(commands, envelope, payload, new JournaledWorkflowDefinitionApplier(new WorkflowDefinitionJsonApplier(store), new InMemoryWorkflowArtifactApplyJournal()));

        var result = await processor.ProcessAsync(Claim(envelope));

        result.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Completed);
        result.RuntimeReference.Should().Be("elsa://workflows/payment-retry");
        result.ObservedDigest.Should().Be(envelope.ContentDigest);
        store.Definitions.Should().ContainSingle(x => x.WorkflowDefinitionId == "payment-retry");
        commands.ProgressReports.Should().Contain(x => x.Status == "validating" && x.PercentComplete == 10);
        commands.ProgressReports.Should().Contain(x => x.Status == "applying" && x.PercentComplete == 60);
        commands.Completed.Should().ContainSingle(x => x.RuntimeReference == "elsa://workflows/payment-retry" && x.ObservedDigest == envelope.ContentDigest);
        commands.Rejected.Should().BeEmpty();
        commands.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task Uses_apply_journal_to_avoid_duplicate_local_side_effects()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var commands = new RecordingRuntimeCommandClient();
        var inner = new CountingWorkflowDefinitionApplier(new WorkflowDefinitionJsonApplier(new InMemoryWorkflowDefinitionRuntimeStore()));
        var processor = Processor(commands, envelope, payload, new JournaledWorkflowDefinitionApplier(inner, new InMemoryWorkflowArtifactApplyJournal()));

        var first = await processor.ProcessAsync(Claim(envelope));
        var second = await processor.ProcessAsync(Claim(envelope));

        first.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Completed);
        second.Status.Should().Be(WorkflowArtifactCommandProcessStatus.AlreadyApplied);
        inner.ApplyCount.Should().Be(1);
        commands.Completed.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rejects_digest_mismatch_before_local_apply()
    {
        var envelope = Envelope(Payload());
        var commands = new RecordingRuntimeCommandClient();
        var inner = new CountingWorkflowDefinitionApplier(new WorkflowDefinitionJsonApplier(new InMemoryWorkflowDefinitionRuntimeStore()));
        var processor = Processor(commands, envelope, Payload("""{"id":"other","version":1}"""), inner);

        var result = await processor.ProcessAsync(Claim(envelope));

        result.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Rejected);
        result.Diagnostics.Should().ContainSingle(x => x.Code == "workflow-artifact.digest-mismatch");
        inner.ApplyCount.Should().Be(0);
        commands.Rejected.Should().ContainSingle();
        commands.Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_local_validation_failure_without_exposing_payload()
    {
        var payload = Payload("""{"name":"Payment Retry","password":"super-secret"}""");
        var envelope = Envelope(payload);
        var commands = new RecordingRuntimeCommandClient();
        var processor = Processor(commands, envelope, payload, new WorkflowDefinitionJsonApplier(new InMemoryWorkflowDefinitionRuntimeStore()));

        var result = await processor.ProcessAsync(Claim(envelope));

        result.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Rejected);
        result.Diagnostics.Should().ContainSingle(x => x.Code == "workflow-artifact.local-validation-failed");
        result.Diagnostics.Single().Message.Should().NotContain("super-secret");
        commands.Rejected.Should().ContainSingle();
        commands.Rejected.Single().Single().Message.Should().NotContain("password");
    }

    [Fact]
    public async Task Reports_local_store_validation_as_safe_rejection()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var commands = new RecordingRuntimeCommandClient();
        var processor = Processor(
            commands,
            envelope,
            payload,
            new WorkflowDefinitionJsonApplier(new RejectingWorkflowDefinitionRuntimeStore("password appeared in runtime validation")));

        var result = await processor.ProcessAsync(Claim(envelope));

        result.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Rejected);
        result.Diagnostics.Single().Message.Should().NotContain("password");
        result.Diagnostics.Single().Message.Should().Contain("[redacted]");
        commands.Rejected.Single().Single().Message.Should().NotContain("password");
    }

    [Fact]
    public async Task Fails_when_apply_throws_unexpected_exception_with_safe_diagnostics()
    {
        var payload = Payload();
        var envelope = Envelope(payload);
        var commands = new RecordingRuntimeCommandClient();
        var processor = Processor(commands, envelope, payload, new ThrowingWorkflowDefinitionApplier("Bearer token leaked"));

        var result = await processor.ProcessAsync(Claim(envelope));

        result.Status.Should().Be(WorkflowArtifactCommandProcessStatus.Failed);
        result.Diagnostics.Single().Message.Should().NotContain("Bearer");
        commands.Failed.Should().ContainSingle();
        commands.Failed.Single().Single().Message.Should().Contain("[redacted]");
    }

    private WorkflowArtifactCommandProcessor Processor(
        RecordingRuntimeCommandClient commands,
        ArtifactEnvelope envelope,
        byte[] payload,
        IWorkflowDefinitionApplier applier) =>
        new(
            commands,
            new StubEnvelopeProvider(envelope),
            new StubPayloadFetcher(new WorkflowArtifactPayload(envelope.PayloadReference, payload, envelope.PayloadReference.MediaType)),
            new WorkflowArtifactRuntimeContractValidator(_options),
            applier,
            _options,
            new FixedTimeProvider(Now));

    private static WorkflowRuntimeCommandClaim Claim(ArtifactEnvelope envelope) =>
        new(
            new WorkflowRuntimeCommand(
                CommandId,
                WorkspaceId,
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000001"),
                EngineId,
                WorkflowRuntimeCommandAction.Deploy,
                WorkflowRuntimeCommandStatus.Claimed,
                new WorkflowRuntimeCommandArtifactReference(ArtifactRecordId, envelope.ArtifactId, envelope.ArtifactTypeId, envelope.ContentDigest),
                new WorkflowRuntimeCommandRevisionReference(Guid.Parse("70000000-0000-0000-0000-000000000001")),
                "deploy-payment-retry",
                "worker-a",
                Now,
                Now.AddMinutes(5),
                Now,
                1,
                null,
                null,
                null,
                null,
                [],
                Now,
                Now,
                null,
                null,
                null),
            "lease-1");

    private static byte[] Payload(string json = """{"id":"payment-retry","version":42}""") =>
        Encoding.UTF8.GetBytes(json);

    private static ArtifactEnvelope Envelope(byte[] payload)
    {
        var digest = WorkflowArtifactRuntimeContractValidator.ComputeDigest(payload);
        return new ArtifactEnvelope(
            $"elsa.workflow-definition:payment-retry:{digest.Value}",
            ArtifactEnvelopeConstants.EnvelopeVersion,
            ArtifactTypeIds.ElsaWorkflowDefinition,
            ArtifactEnvelopeConstants.DefaultArtifactSchemaVersion,
            digest,
            null,
            new ArtifactPayloadReference(
                "producer-managed",
                $"https://payloads.example.test/workflows/payment-retry/{digest.Value}",
                "application/vnd.elsa.workflow-definition+json",
                payload.Length,
                digest),
            new ArtifactProducer("studio", "Elsa Studio", "4.0.0", "workflow:payment-retry:version:v42"),
            new ArtifactDisplayMetadata(
                "Payment Retry",
                "42",
                "Retries payment collection failures.",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                "studio://workflows/payment-retry"),
            [
                new ArtifactCompatibilityHint(
                    ArtifactTypeIds.ElsaWorkflowDefinition,
                    "elsa-workflows",
                    ">=4.0.0",
                    ["workflow-definition.apply"],
                    new Dictionary<string, string>())
            ],
            []);
    }

    private sealed class StubEnvelopeProvider(ArtifactEnvelope envelope) : IWorkflowArtifactEnvelopeProvider
    {
        public Task<ArtifactEnvelope> GetEnvelopeAsync(
            WorkflowRuntimeCommandArtifactReference artifact,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(envelope);
    }

    private sealed class StubPayloadFetcher(WorkflowArtifactPayload payload) : IWorkflowArtifactPayloadFetcher
    {
        public Task<WorkflowArtifactPayload> FetchAsync(ArtifactPayloadReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(payload);
    }

    private sealed class RecordingRuntimeCommandClient : IWorkflowRuntimeCommandClient
    {
        public List<(string Status, int? PercentComplete, string Message)> ProgressReports { get; } = [];
        public List<(ArtifactDigest? ObservedDigest, string? RuntimeReference, IReadOnlyList<WorkflowArtifactDiagnostic> Diagnostics)> Completed { get; } = [];
        public List<IReadOnlyList<WorkflowArtifactDiagnostic>> Rejected { get; } = [];
        public List<IReadOnlyList<WorkflowArtifactDiagnostic>> Failed { get; } = [];

        public Task<IReadOnlyList<WorkflowRuntimeCommand>> PollAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowRuntimeCommand>>([]);

        public Task<WorkflowRuntimeCommandClaimResult> ClaimAsync(Guid commandId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowRuntimeCommandClaimResult(WorkflowRuntimeCommandClientStatus.NotFound, "Not used."));

        public Task<WorkflowRuntimeCommandReportResult> HeartbeatAsync(Guid commandId, string leaseToken, CancellationToken cancellationToken = default) =>
            ReportAsync();

        public Task<WorkflowRuntimeCommandReportResult> ReportProgressAsync(
            Guid commandId,
            string leaseToken,
            string status,
            int? percentComplete,
            string message,
            CancellationToken cancellationToken = default)
        {
            ProgressReports.Add((status, percentComplete, message));
            return ReportAsync();
        }

        public Task<WorkflowRuntimeCommandReportResult> CompleteAsync(
            Guid commandId,
            string leaseToken,
            ArtifactDigest? observedDigest,
            string? runtimeReference,
            IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
            CancellationToken cancellationToken = default)
        {
            Completed.Add((observedDigest, runtimeReference, diagnostics));
            return ReportAsync();
        }

        public Task<WorkflowRuntimeCommandReportResult> FailAsync(
            Guid commandId,
            string leaseToken,
            IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
            CancellationToken cancellationToken = default)
        {
            Failed.Add(diagnostics);
            return ReportAsync();
        }

        public Task<WorkflowRuntimeCommandReportResult> RejectAsync(
            Guid commandId,
            string leaseToken,
            IReadOnlyList<WorkflowArtifactDiagnostic> diagnostics,
            CancellationToken cancellationToken = default)
        {
            Rejected.Add(diagnostics);
            return ReportAsync();
        }

        private static Task<WorkflowRuntimeCommandReportResult> ReportAsync() =>
            Task.FromResult(new WorkflowRuntimeCommandReportResult(WorkflowRuntimeCommandClientStatus.Succeeded, "Accepted."));
    }

    private sealed class CountingWorkflowDefinitionApplier(IWorkflowDefinitionApplier inner) : IWorkflowDefinitionApplier
    {
        public int ApplyCount { get; private set; }

        public async Task<WorkflowArtifactApplyResult> ApplyAsync(
            WorkflowArtifactApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            return await inner.ApplyAsync(request, cancellationToken);
        }
    }

    private sealed class RejectingWorkflowDefinitionRuntimeStore(string message) : IWorkflowDefinitionRuntimeStore
    {
        public Task<WorkflowDefinitionRuntimeStoreResult> SaveAsync(
            WorkflowDefinitionRuntimeStoreRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class ThrowingWorkflowDefinitionApplier(string message) : IWorkflowDefinitionApplier
    {
        public Task<WorkflowArtifactApplyResult> ApplyAsync(
            WorkflowArtifactApplyRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
