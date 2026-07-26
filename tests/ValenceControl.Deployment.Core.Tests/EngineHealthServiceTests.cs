using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace ValenceControl.Deployment.Core.Tests;

public sealed class EngineHealthServiceTests
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly Guid _engineId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private readonly Guid _environmentId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-05-26T10:00:00Z");

    [Fact]
    public async Task Verify_engine_marks_reachable_trusted_verified_probe_as_healthy()
    {
        var store = new RecordingStore(Engine());
        var service = new EngineHealthService(
            store,
            new StubProbe(new EngineHealthProbeResult(true, "Elsa 4.1.0", CertificateStatus.Trusted, CredentialVerificationStatus.Verified, "Endpoint responded successfully.")),
            new StaticTimeProvider(_now));

        var result = await service.VerifyEngineAsync(_workspaceId, new EngineHealthVerificationRequest(_engineId, Guid.NewGuid()));

        result.Health.Should().Be(DeploymentHealth.Healthy);
        result.Version.Should().Be("Elsa 4.1.0");
        result.LastHeartbeatAt.Should().Be(_now);
        result.LastVerificationAt.Should().Be(_now);
        result.CredentialLastVerifiedAt.Should().Be(_now);
        result.Message.Should().Be("Endpoint responded successfully.");
    }

    [Fact]
    public async Task Verify_engine_marks_unreachable_probe_as_unreachable_without_refreshing_heartbeat()
    {
        var lastHeartbeatAt = DateTimeOffset.Parse("2026-05-26T09:00:00Z");
        var store = new RecordingStore(Engine(lastHeartbeatAt: lastHeartbeatAt));
        var service = new EngineHealthService(
            store,
            new StubProbe(new EngineHealthProbeResult(false, null, CertificateStatus.Trusted, CredentialVerificationStatus.Unverified, "Endpoint did not respond before verification timed out.")),
            new StaticTimeProvider(_now));

        var result = await service.VerifyEngineAsync(_workspaceId, new EngineHealthVerificationRequest(_engineId, Guid.NewGuid()));

        result.Health.Should().Be(DeploymentHealth.Unreachable);
        result.LastHeartbeatAt.Should().Be(lastHeartbeatAt);
        result.LastVerificationAt.Should().Be(_now);
        result.CredentialVerificationStatus.Should().Be(CredentialVerificationStatus.Unverified);
    }

    [Fact]
    public async Task Apply_heartbeat_rejects_stale_heartbeats()
    {
        var heartbeatAt = DateTimeOffset.Parse("2026-05-26T09:00:00Z");
        var store = new RecordingStore(Engine(lastHeartbeatAt: heartbeatAt));
        var service = new EngineHealthService(
            store,
            new StubProbe(new EngineHealthProbeResult(true, null, CertificateStatus.Trusted, CredentialVerificationStatus.Verified, "")),
            new StaticTimeProvider(_now));

        var act = () => service.ApplyHeartbeatAsync(
            _workspaceId,
            new EngineHeartbeatRequest(
                _engineId,
                _environmentId,
                "Elsa 4.1.0",
                CertificateStatus.Trusted,
                CredentialVerificationStatus.Verified,
                heartbeatAt,
                null,
                "Heartbeat accepted."));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Heartbeat is stale.");
    }

    private WorkspaceWorkflowEngine Engine(DateTimeOffset? lastHeartbeatAt = null) =>
        new(
            _engineId,
            _workspaceId,
            _environmentId,
            "claims-prod-weu-01",
            "https://engine.example.test/elsa",
            "weu",
            "",
            CertificateStatus.Trusted,
            "Azure Key Vault",
            "kv://claims/prod/elsa-api",
            CredentialVerificationStatus.Unverified,
            null,
            DeploymentHealth.Unreachable,
            lastHeartbeatAt,
            null,
            DateTimeOffset.Parse("2026-05-25T10:00:00Z"),
            DateTimeOffset.Parse("2026-05-25T10:00:00Z"));

    private sealed class StubProbe(EngineHealthProbeResult result) : IEngineHealthProbe
    {
        public Task<EngineHealthProbeResult> ProbeAsync(WorkspaceWorkflowEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingStore(WorkspaceWorkflowEngine engine) : IWorkspaceDeploymentStore
    {
        private WorkspaceWorkflowEngine _engine = engine;

        public Task<WorkspaceWorkflowEngine?> GetEngineAsync(Guid workspaceId, Guid engineId, CancellationToken cancellationToken = default) =>
            Task.FromResult(workspaceId == _engine.WorkspaceId && engineId == _engine.Id ? _engine : null);

        public Task<EngineHealthResult> UpdateEngineHealthAsync(Guid workspaceId, EngineHealthUpdate update, CancellationToken cancellationToken = default)
        {
            Apply(update);
            return Task.FromResult(Result(update));
        }

        public Task<EngineHealthResult> ApplyEngineHeartbeatAsync(Guid workspaceId, EngineHealthUpdate update, CancellationToken cancellationToken = default)
        {
            if (_engine.LastHeartbeatAt.HasValue && update.LastHeartbeatAt.HasValue && update.LastHeartbeatAt <= _engine.LastHeartbeatAt)
                throw new InvalidOperationException("Heartbeat is stale.");

            Apply(update);
            return Task.FromResult(Result(update));
        }

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(Guid workspaceId, Guid applicationId, UpdateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(Guid workspaceId, Guid environmentId, UpdateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceWorkflowEngine> UpdateEngineAsync(Guid workspaceId, Guid engineId, UpdateWorkflowEngineRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(Guid workspaceId, Guid revisionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(Guid workspaceId, Guid environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private void Apply(EngineHealthUpdate update)
        {
            _engine = _engine with
            {
                Version = update.Version,
                CertificateStatus = update.CertificateStatus,
                CredentialVerificationStatus = update.CredentialVerificationStatus,
                CredentialLastVerifiedAt = update.CredentialLastVerifiedAt,
                Health = update.Health,
                LastHeartbeatAt = update.LastHeartbeatAt,
                LastVerificationAt = update.LastVerificationAt,
                VerificationMessage = update.VerificationMessage
            };
        }

        private static EngineHealthResult Result(EngineHealthUpdate update) =>
            new(
                update.EngineId,
                update.EnvironmentId,
                update.Health,
                update.Version,
                update.CertificateStatus,
                update.CredentialVerificationStatus,
                update.CredentialLastVerifiedAt,
                update.LastHeartbeatAt,
                update.LastVerificationAt,
                update.VerificationMessage);
    }
}
