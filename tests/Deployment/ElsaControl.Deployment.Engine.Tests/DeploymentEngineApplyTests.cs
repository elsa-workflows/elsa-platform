using ElsaControl.Deployment.Abstractions;
using ElsaControl.Deployment.Abstractions.Diagnostics;
using ElsaControl.Deployment.Abstractions.History;
using ElsaControl.Deployment.Abstractions.Plans;
using ElsaControl.Deployment.Abstractions.Resources;
using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Engine.Tests;

public class DeploymentEngineApplyTests
{
    private readonly TestTarget _target = new();
    private readonly RecordingResourceHandler _handler = new();
    private readonly InMemoryDeploymentHistoryStore _history = new();

    [Fact]
    public async Task ApplyAsyncInvokesHandlersForApplyableChangesAndRecordsHistory()
    {
        var create = DeploymentEngineTestFixtures.Resource(logicalId: "create");
        var update = DeploymentEngineTestFixtures.Resource(logicalId: "update");
        _handler.SetCurrentState(new DeploymentResourceState(update.Id, DeploymentEngineTestFixtures.CurrentHash));
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(create, update), _target);

        var result = await engine.ApplyAsync(plan, _target);
        var history = await _history.FindAsync(result.DeploymentId);

        Assert.Equal(DeploymentStatus.Applied, result.Status);
        Assert.Equal([DeploymentChangeAction.Create, DeploymentChangeAction.Update], _handler.ApplyChanges.Select(x => x.Action));
        Assert.NotNull(history);
        Assert.Equal(2, history!.ResourceResults.Count());
    }

    [Fact]
    public async Task ApplyAsyncSkipsNoOpChanges()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.SetCurrentState(new DeploymentResourceState(resource.Id, DeploymentEngineTestFixtures.DesiredHash));
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.ApplyAsync(plan, _target);

        Assert.Equal(DeploymentStatus.NoOp, result.Status);
        Assert.Empty(_handler.ApplyChanges);
        Assert.Single(result.ResourceResults, x => x.Status == DeploymentChangeStatus.Skipped);
    }

    [Fact]
    public async Task ApplyAsyncReturnsNoOpWhenEveryChangeIsSkipped()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var change = new DeploymentChange(resource.Id, DeploymentChangeAction.Delete, DeploymentChangeStatus.Blocked, resource: resource);
        var plan = new DeploymentPlan("plan-1", DeploymentEngineTestFixtures.Artifact, DeploymentEngineTestFixtures.TargetDescriptor, [change]);
        var engine = CreateEngine();

        var result = await engine.ApplyAsync(plan, _target);

        Assert.Equal(DeploymentStatus.NoOp, result.Status);
        Assert.Equal(DeploymentChangeStatus.Skipped, Assert.Single(result.ResourceResults).Status);
        Assert.Empty(_handler.ApplyChanges);
    }

    [Fact]
    public async Task ApplyAsyncRepresentsPartialFailures()
    {
        var success = DeploymentEngineTestFixtures.Resource(logicalId: "success");
        var failure = DeploymentEngineTestFixtures.Resource(logicalId: "failure");
        var diagnostic = new DeploymentDiagnostic("apply.failed", DeploymentDiagnosticSeverity.Error, "Apply failed.", failure.Id);
        _handler.ApplyFactory = change => change.ResourceId == failure.Id
            ? new DeploymentResourceResult(change.ResourceId, change.Action, DeploymentChangeStatus.Failed, retryable: true, diagnostics: [diagnostic])
            : new DeploymentResourceResult(change.ResourceId, change.Action, DeploymentChangeStatus.Completed);
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(success, failure), _target);

        var result = await engine.ApplyAsync(plan, _target);
        var history = await _history.FindAsync(result.DeploymentId);

        Assert.Equal(DeploymentStatus.PartiallyApplied, result.Status);
        Assert.Contains(result.ResourceResults, x => x.ResourceId == failure.Id && x.Retryable);
        Assert.Contains(diagnostic, history!.Diagnostics);
    }

    [Fact]
    public async Task ApplyAsyncSkipsInvalidPlansAndRecordsFailedHistory()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var diagnostic = new DeploymentDiagnostic("plan.invalid", DeploymentDiagnosticSeverity.Error, "Invalid plan.", resource.Id);
        var change = new DeploymentChange(resource.Id, DeploymentChangeAction.Create, DeploymentChangeStatus.Ready, resource: resource);
        var plan = new DeploymentPlan("plan-1", DeploymentEngineTestFixtures.Artifact, DeploymentEngineTestFixtures.TargetDescriptor, [change], [diagnostic]);
        var engine = CreateEngine();

        var result = await engine.ApplyAsync(plan, _target);
        var history = await _history.FindAsync(result.DeploymentId);

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Equal(DeploymentChangeStatus.Skipped, Assert.Single(result.ResourceResults).Status);
        Assert.Empty(_handler.ApplyChanges);
        Assert.Contains(diagnostic, history!.Diagnostics);
    }

    [Fact]
    public async Task ApplyAsyncReportsHandlerExceptionsWithApplyCode()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.ApplyException = new InvalidOperationException("Apply exploded.");
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.ApplyAsync(plan, _target);

        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Single(result.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.ApplyFailed &&
            x.ResourceId == resource.Id);
    }

    [Fact]
    public async Task ApplyAsyncPreservesApplyStatusWhenHistoryFails()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var history = new FailingDeploymentHistoryStore();
        var engine = new DeploymentEngine([_handler], history, DeploymentEngineTestFixtures.StableOptions());
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.ApplyAsync(plan, _target);

        Assert.Equal(DeploymentStatus.Applied, result.Status);
        Assert.Equal(DeploymentChangeStatus.Completed, Assert.Single(result.ResourceResults).Status);
        Assert.Single(result.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.HistoryFailed &&
            x.Severity == DeploymentDiagnosticSeverity.Warning);
    }

    private DeploymentEngine CreateEngine() =>
        new([_handler], _history, DeploymentEngineTestFixtures.StableOptions());

    private sealed class FailingDeploymentHistoryStore : IDeploymentHistoryStore
    {
        public ValueTask RecordAsync(DeploymentHistoryRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("History failed.");

        public ValueTask<DeploymentHistoryRecord?> FindAsync(string deploymentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DeploymentHistoryRecord?>(null);

        public async IAsyncEnumerable<DeploymentHistoryRecord> ListAsync(
            DeploymentTargetDescriptor target,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
