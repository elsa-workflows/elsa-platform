using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class WorkspaceDeploymentServiceTests
{
    private readonly Guid _workspaceId = WorkspaceDeploymentTestFixtures.WorkspaceId;
    private readonly RecordingDeploymentStore _store = new();

    [Fact]
    public async Task Gets_cockpit_projection_from_workspace_store()
    {
        _store.Cockpit = new DeploymentCockpit(
            [new WorkflowApplication("app-1", "Claims", "Workspace", [])],
            [],
            [],
            [],
            [],
            [],
            []);
        var service = new WorkspaceDeploymentService(_store);

        var cockpit = await service.GetCockpitAsync(_workspaceId);

        cockpit.Applications.Should().ContainSingle(x => x.Id == "app-1");
        _store.LastWorkspaceId.Should().Be(_workspaceId);
    }

    [Fact]
    public void Computes_stable_desired_state_hashes()
    {
        var hash = WorkspaceDeploymentService.ComputeDesiredStateHash("{\"kind\":\"Workflow\"}");

        hash.Should().Be(WorkspaceDeploymentService.ComputeDesiredStateHash("{\"kind\":\"Workflow\"}"));
        hash.Should().NotBe(WorkspaceDeploymentService.ComputeDesiredStateHash("{\"kind\":\"Feature\"}"));
    }

    private sealed class RecordingDeploymentStore : IWorkspaceDeploymentStore
    {
        public Guid LastWorkspaceId { get; private set; }
        public DeploymentCockpit Cockpit { get; set; } = new([], [], [], [], [], [], []);

        public Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default)
        {
            LastWorkspaceId = workspaceId;
            return Task.FromResult(Cockpit);
        }

        public Task<WorkspaceDeploymentApplication> CreateApplicationAsync(Guid workspaceId, CreateWorkflowApplicationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(Guid workspaceId, CreateDeploymentEnvironmentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceWorkflowEngine> RegisterEngineAsync(Guid workspaceId, RegisterWorkflowEngineRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(Guid workspaceId, CreateDesiredStateRevisionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
