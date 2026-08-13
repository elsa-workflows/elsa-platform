using ValenceControl.Studio.Submit;

namespace ValenceControl.Studio.Submit.Tests;

public sealed class SubmitToControlCommandTests
{
    private readonly StudioWorkflowSnapshotPackager _packager = new();
    private readonly StudioSubmitOptions _options = new()
    {
        ControlEndpoint = new Uri("https://control.example.test"),
        WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001")
    };

    [Fact]
    public async Task Submits_packaged_snapshot_through_control_client()
    {
        var client = new RecordingSubmitClient(new StudioSubmitResult(StudioSubmitStatus.Submitted, "Submitted.", "artifact-1", "sha256:abc", DateTimeOffset.UtcNow));
        var command = new SubmitToControlCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot());

        Assert.Equal(StudioSubmitStatus.Submitted, result.Status);
        Assert.True(result.Succeeded);
        Assert.NotNull(client.Package);
        Assert.Equal("elsa.loom.recipe", client.Package!.Envelope.ArtifactTypeId);
    }

    [Fact]
    public async Task Treats_duplicate_submission_as_success_state()
    {
        var client = new RecordingSubmitClient(new StudioSubmitResult(StudioSubmitStatus.Duplicate, "Already submitted.", "artifact-1"));
        var command = new SubmitToControlCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot());

        Assert.Equal(StudioSubmitStatus.Duplicate, result.Status);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Default_result_status_is_not_successful()
    {
        var result = new StudioSubmitResult(default, "uninitialized");

        Assert.Equal(StudioSubmitStatus.Unknown, result.Status);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Returns_safe_validation_failure_without_calling_control()
    {
        var client = new RecordingSubmitClient(new StudioSubmitResult(StudioSubmitStatus.Submitted, "Submitted."));
        var command = new SubmitToControlCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot(labels: new Dictionary<string, string> { ["token"] = "abc" }));

        Assert.Equal(StudioSubmitStatus.ValidationFailed, result.Status);
        Assert.Contains("[redacted]", result.Message);
        Assert.Null(client.Package);
    }

    [Fact]
    public async Task Converts_control_unavailability_to_safe_retryable_state()
    {
        var client = new RecordingSubmitClient(new HttpRequestException("Bearer token rejected by upstream"));
        var command = new SubmitToControlCommand(_packager, client, _options);

        var result = await command.ExecuteAsync(Snapshot());

        Assert.Equal(StudioSubmitStatus.Unavailable, result.Status);
        Assert.Contains("[redacted]", result.Message);
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

    private sealed class RecordingSubmitClient : IStudioControlSubmitClient
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
