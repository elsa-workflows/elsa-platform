using ElsaControl.Deployment.Abstractions;
using ElsaControl.Deployment.Abstractions.History;
using ElsaControl.Deployment.Abstractions.Resources;

namespace ElsaControl.Deployment.Engine.Tests;

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

        Assert.Equal(record, found);
        Assert.Equal(record, Assert.Single(listed));
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

        Assert.NotNull(history);
        Assert.Equal("deploy-1", history!.DeploymentId);
        Assert.Equal(DeploymentEngineTestFixtures.TargetDescriptor, history.Target);
        Assert.Equal(actor, history.Actor);
        Assert.Equal(DeploymentEngineTestFixtures.Artifact, history.Artifact);
        Assert.Equal(plan, history.Plan);
        Assert.NotNull(history.CompletedAt);
    }
}
