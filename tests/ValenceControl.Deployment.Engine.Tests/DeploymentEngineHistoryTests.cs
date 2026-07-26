using ValenceControl.Deployment.Abstractions;
using ValenceControl.Deployment.Abstractions.History;
using ValenceControl.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace ValenceControl.Deployment.Engine.Tests;

public class DeploymentEngineHistoryTests
{
    private readonly TestTarget _target = new();
    private readonly RecordingResourceHandler _handler = new();
    private readonly InMemoryDeploymentHistoryStore _history = new();

    [Fact]
    public async Task InMemoryHistoryStoreRecordsFindsAndListsByTarget()
    {
        var record = new DeploymentHistoryRecord(
            "deploy-1",
            DeploymentEngineTestFixtures.Artifact,
            DeploymentEngineTestFixtures.TargetDescriptor,
            DeploymentStatus.Applied);

        await _history.RecordAsync(record);

        var found = await _history.FindAsync("deploy-1");
        var listed = new List<DeploymentHistoryRecord>();
        await foreach (var item in _history.ListAsync(DeploymentEngineTestFixtures.TargetDescriptor))
            listed.Add(item);

        found.Should().Be(record);
        listed.Should().ContainSingle().Which.Should().Be(record);
    }

    [Fact]
    public async Task ApplyAsyncRecordsAuditFieldsIncludingActor()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var actor = new DeploymentActor("user:alice", "Alice");
        var engine = new DeploymentEngine([_handler], _history, DeploymentEngineTestFixtures.StableOptions());
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.ApplyAsync(plan, _target, new DeploymentExecutionContext(actor));
        var history = await _history.FindAsync(result.DeploymentId);

        history.Should().NotBeNull();
        history!.DeploymentId.Should().Be("deploy-1");
        history.Target.Should().Be(DeploymentEngineTestFixtures.TargetDescriptor);
        history.Actor.Should().Be(actor);
        history.Artifact.Should().Be(DeploymentEngineTestFixtures.Artifact);
        history.Plan.Should().Be(plan);
        history.CompletedAt.Should().NotBeNull();
    }
}
