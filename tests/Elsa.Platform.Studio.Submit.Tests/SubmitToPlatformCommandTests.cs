using Elsa.Platform.Studio.Submit;
using FluentAssertions;

namespace Elsa.Platform.Studio.Submit.Tests;

public sealed class SubmitToPlatformCommandTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();
    private readonly StudioSubmitOptions _options = new()
    {
        PlatformEndpoint = new Uri("https://platform.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001")
    };

    [Fact]
    public async Task Submits_packaged_snapshot_through_platform_client()
    {
        var client = new RecordingSubmitClient(new StudioSubmitResult(StudioSubmitStatus.Submitted, "Submitted.", "artifact-1", "sha256:abc", DateTimeOffset.UtcNow));
        var command = new SubmitToPlatformCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot());

        result.Status.Should().Be(StudioSubmitStatus.Submitted);
        result.Succeeded.Should().BeTrue();
        client.Package.Should().NotBeNull();
        client.Package!.Envelope.ArtifactTypeId.Should().Be("elsa.loom.recipe");
    }

    [Fact]
    public async Task Treats_duplicate_submission_as_success_state()
    {
        var client = new RecordingSubmitClient(new StudioSubmitResult(StudioSubmitStatus.Duplicate, "Already submitted.", "artifact-1"));
        var command = new SubmitToPlatformCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot());

        result.Status.Should().Be(StudioSubmitStatus.Duplicate);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Default_result_status_is_not_successful()
    {
        var result = new StudioSubmitResult(default, "uninitialized");

        result.Status.Should().Be(StudioSubmitStatus.Unknown);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Returns_safe_validation_failure_without_calling_platform()
    {
        var client = new RecordingSubmitClient(new StudioSubmitResult(StudioSubmitStatus.Submitted, "Submitted."));
        var command = new SubmitToPlatformCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot(labels: new Dictionary<string, string> { ["token"] = "abc" }));

        result.Status.Should().Be(StudioSubmitStatus.ValidationFailed);
        result.Message.Should().Contain("[redacted]");
        client.Package.Should().BeNull();
    }

    [Fact]
    public async Task Converts_platform_unavailability_to_safe_retryable_state()
    {
        var client = new RecordingSubmitClient(new HttpRequestException("Bearer token rejected by upstream"));
        var command = new SubmitToPlatformCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot());

        result.Status.Should().Be(StudioSubmitStatus.Unavailable);
        result.Message.Should().Contain("[redacted]");
    }

    private static WorkflowSubmissionSnapshot Snapshot(IReadOnlyDictionary<string, string>? labels = null) =>
        new(
            "payment-retry",
            "v42",
            "Payment Retry",
            "42",
            "Retries payment collection failures.",
            """{"id":"payment-retry","name":"PaymentRetry","version":42}""",
            "1.0",
            "studio://workflows/payment-retry",
            labels ?? new Dictionary<string, string>(),
            new Dictionary<string, string>());

    private sealed class RecordingSubmitClient : IStudioPlatformSubmitClient
    {
        private readonly StudioSubmitResult? _result;
        private readonly Exception? _exception;

        public RecordingSubmitClient(StudioSubmitResult result)
        {
            _result = result;
        }

        public RecordingSubmitClient(Exception exception)
        {
            _exception = exception;
        }

        public StudioSubmitPackage? Package { get; private set; }

        public Task<StudioSubmitResult> SubmitAsync(StudioSubmitPackage package, StudioSubmitOptions options, CancellationToken cancellationToken = default)
        {
            Package = package;
            if (_exception is not null)
                throw _exception;
            return Task.FromResult(_result!);
        }
    }
}
