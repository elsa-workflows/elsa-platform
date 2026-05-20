using Elsa.Platform.Deployment.Abstractions;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.History;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;
using Elsa.Platform.Deployment.Abstractions.Targets;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Engine.Tests;

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

        result.Status.Should().Be(DeploymentStatus.Applied);
        _handler.ApplyChanges.Select(x => x.Action).Should().Equal(DeploymentChangeAction.Create, DeploymentChangeAction.Update);
        history.Should().NotBeNull();
        history!.ResourceResults.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApplyAsyncSkipsNoOpChanges()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.SetCurrentState(new DeploymentResourceState(resource.Id, DeploymentEngineTestFixtures.DesiredHash));
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.ApplyAsync(plan, _target);

        result.Status.Should().Be(DeploymentStatus.NoOp);
        _handler.ApplyChanges.Should().BeEmpty();
        result.ResourceResults.Should().ContainSingle(x => x.Status == DeploymentChangeStatus.Skipped);
    }

    [Fact]
    public async Task ApplyAsyncReturnsNoOpWhenEveryChangeIsSkipped()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var change = new DeploymentChange(resource.Id, DeploymentChangeAction.Delete, DeploymentChangeStatus.Blocked, resource: resource);
        var plan = new DeploymentPlan("plan-1", DeploymentEngineTestFixtures.Artifact, DeploymentEngineTestFixtures.TargetDescriptor, [change]);
        var engine = CreateEngine();

        var result = await engine.ApplyAsync(plan, _target);

        result.Status.Should().Be(DeploymentStatus.NoOp);
        result.ResourceResults.Should().ContainSingle().Which.Status.Should().Be(DeploymentChangeStatus.Skipped);
        _handler.ApplyChanges.Should().BeEmpty();
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

        result.Status.Should().Be(DeploymentStatus.PartiallyApplied);
        result.ResourceResults.Should().Contain(x => x.ResourceId == failure.Id && x.Retryable);
        history!.Diagnostics.Should().Contain(diagnostic);
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

        result.Status.Should().Be(DeploymentStatus.Failed);
        result.ResourceResults.Should().ContainSingle().Which.Status.Should().Be(DeploymentChangeStatus.Skipped);
        _handler.ApplyChanges.Should().BeEmpty();
        history!.Diagnostics.Should().Contain(diagnostic);
    }

    [Fact]
    public async Task ApplyAsyncReportsHandlerExceptionsWithApplyCode()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.ApplyException = new InvalidOperationException("Apply exploded.");
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.ApplyAsync(plan, _target);

        result.Status.Should().Be(DeploymentStatus.Failed);
        result.Diagnostics.Should().ContainSingle(x =>
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

        result.Status.Should().Be(DeploymentStatus.Applied);
        result.ResourceResults.Should().ContainSingle().Which.Status.Should().Be(DeploymentChangeStatus.Completed);
        result.Diagnostics.Should().ContainSingle(x =>
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
