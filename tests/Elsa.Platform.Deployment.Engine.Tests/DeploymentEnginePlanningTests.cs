using Elsa.Platform.Deployment.Abstractions;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.History;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Engine.Tests;

public class DeploymentEnginePlanningTests
{
    private readonly TestTarget _target = new();
    private readonly RecordingResourceHandler _handler = new();
    private readonly InMemoryDeploymentHistoryStore _history = new();

    [Fact]
    public async Task DiffAsyncPlansCreateUpdateAndNoOpChanges()
    {
        var create = DeploymentEngineTestFixtures.Resource(logicalId: "create");
        var update = DeploymentEngineTestFixtures.Resource(logicalId: "update");
        var noOp = DeploymentEngineTestFixtures.Resource(logicalId: "noop");
        _handler.SetCurrentState(new DeploymentResourceState(update.Id, DeploymentEngineTestFixtures.CurrentHash));
        _handler.SetCurrentState(new DeploymentResourceState(noOp.Id, DeploymentEngineTestFixtures.DesiredHash));
        var engine = CreateEngine();

        var plan = await engine.DiffAsync(new TestArtifactReader(create, update, noOp), _target);

        plan.Changes.Should().Contain(change => change.ResourceId == create.Id && change.Action == DeploymentChangeAction.Create);
        plan.Changes.Should().Contain(change => change.ResourceId == update.Id && change.Action == DeploymentChangeAction.Update);
        plan.Changes.Should().Contain(change => change.ResourceId == noOp.Id && change.Action == DeploymentChangeAction.NoOp);
    }

    [Fact]
    public async Task DiffAsyncBlocksDeleteWhenPruneIsNotEnabledAndAllowsItWhenEnabled()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.DiffFactory = (desired, _) => new DeploymentChange(desired.Id, DeploymentChangeAction.Delete, DeploymentChangeStatus.Ready);
        var engine = CreateEngine();

        var blocked = await engine.DiffAsync(new TestArtifactReader(resource), _target);
        var allowed = await engine.DiffAsync(new TestArtifactReader(resource), _target, new DeploymentExecutionContext(prune: true));

        blocked.Changes.Should().ContainSingle().Which.Status.Should().Be(DeploymentChangeStatus.Blocked);
        blocked.Changes.Single().Diagnostics.Should().ContainSingle(x => x.Code == DeploymentEngineDiagnosticCodes.PruneDisabled);
        allowed.Changes.Should().ContainSingle().Which.Status.Should().Be(DeploymentChangeStatus.Ready);
    }

    [Fact]
    public async Task DiffAsyncOrdersChangesDeterministicallyByResourceIdentity()
    {
        var second = DeploymentEngineTestFixtures.Resource(logicalId: "b");
        var first = DeploymentEngineTestFixtures.Resource(logicalId: "a");
        var engine = CreateEngine();

        var plan = await engine.DiffAsync(new TestArtifactReader(second, first), _target);

        plan.Changes.Select(x => x.ResourceId.LogicalId).Should().Equal("a", "b");
    }

    [Fact]
    public async Task DiffAsyncReportsReadAndDiffExceptionsWithOperationSpecificCodes()
    {
        var readFailure = DeploymentEngineTestFixtures.Resource(logicalId: "read");
        var diffFailure = DeploymentEngineTestFixtures.Resource(logicalId: "diff");
        _handler.ReadException = new InvalidOperationException("Read exploded.");
        var readEngine = CreateEngine();

        var readPlan = await readEngine.DiffAsync(new TestArtifactReader(readFailure), _target);

        _handler.ReadException = null;
        _handler.DiffException = new InvalidOperationException("Diff exploded.");
        var diffPlan = await readEngine.DiffAsync(new TestArtifactReader(diffFailure), _target);

        readPlan.Diagnostics.Should().ContainSingle(x =>
            x.Code == DeploymentEngineDiagnosticCodes.ReadFailed &&
            x.ResourceId == readFailure.Id);
        diffPlan.Diagnostics.Should().ContainSingle(x =>
            x.Code == DeploymentEngineDiagnosticCodes.DiffFailed &&
            x.ResourceId == diffFailure.Id);
    }

    [Fact]
    public async Task DryRunAsyncDoesNotApplyResourcesOrRecordHistory()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.DryRunAsync(plan, _target);
        var history = await _history.FindAsync(result.DeploymentId);

        result.Status.Should().Be(DeploymentStatus.DryRunCompleted);
        _handler.DryRunChanges.Should().ContainSingle();
        _handler.ApplyChanges.Should().BeEmpty();
        history.Should().BeNull();
    }

    [Fact]
    public async Task DryRunAsyncSkipsInvalidPlansWithoutHandlerInvocation()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var diagnostic = new DeploymentDiagnostic("plan.invalid", DeploymentDiagnosticSeverity.Error, "Invalid plan.", resource.Id);
        var change = new DeploymentChange(resource.Id, DeploymentChangeAction.Create, DeploymentChangeStatus.Ready, resource: resource);
        var plan = new DeploymentPlan("plan-1", DeploymentEngineTestFixtures.Artifact, DeploymentEngineTestFixtures.TargetDescriptor, [change], [diagnostic]);
        var engine = CreateEngine();

        var result = await engine.DryRunAsync(plan, _target);

        result.Status.Should().Be(DeploymentStatus.ValidationFailed);
        result.ResourceResults.Should().ContainSingle().Which.Status.Should().Be(DeploymentChangeStatus.Skipped);
        _handler.DryRunChanges.Should().BeEmpty();
        _handler.ApplyChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task DryRunAsyncReportsHandlerExceptionsWithDryRunCode()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.DryRunException = new InvalidOperationException("Dry-run exploded.");
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.DryRunAsync(plan, _target);

        result.Status.Should().Be(DeploymentStatus.ValidationFailed);
        result.Diagnostics.Should().ContainSingle(x =>
            x.Code == DeploymentEngineDiagnosticCodes.DryRunFailed &&
            x.ResourceId == resource.Id);
    }

    private DeploymentEngine CreateEngine() =>
        new([_handler], _history, DeploymentEngineTestFixtures.StableOptions());
}
