using ValenceControl.Deployment.Abstractions;
using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.History;
using ValenceControl.Deployment.Abstractions.Plans;
using ValenceControl.Deployment.Abstractions.Resources;

namespace ValenceControl.Deployment.Engine.Tests;

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

        Assert.Contains(plan.Changes, change => change.ResourceId == create.Id && change.Action == DeploymentChangeAction.Create);
        Assert.Contains(plan.Changes, change => change.ResourceId == update.Id && change.Action == DeploymentChangeAction.Update);
        Assert.Contains(plan.Changes, change => change.ResourceId == noOp.Id && change.Action == DeploymentChangeAction.NoOp);
    }

    [Fact]
    public async Task DiffAsyncBlocksDeleteWhenPruneIsNotEnabledAndAllowsItWhenEnabled()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.DiffFactory = (desired, _) => new DeploymentChange(desired.Id, DeploymentChangeAction.Delete, DeploymentChangeStatus.Ready);
        var engine = CreateEngine();

        var blocked = await engine.DiffAsync(new TestArtifactReader(resource), _target);
        var allowed = await engine.DiffAsync(new TestArtifactReader(resource), _target, new DeploymentExecutionContext(prune: true));

        Assert.Equal(DeploymentChangeStatus.Blocked, Assert.Single(blocked.Changes).Status);
        Assert.Single(blocked.Changes.Single().Diagnostics, x => x.Code == DeploymentEngineDiagnosticCodes.PruneDisabled);
        Assert.Equal(DeploymentChangeStatus.Ready, Assert.Single(allowed.Changes).Status);
    }

    [Fact]
    public async Task DiffAsyncPreservesHandlerDiagnosticsWhenPruneBlocksDelete()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var diagnostic = new DeploymentDiagnostic("handler.warning", DeploymentDiagnosticSeverity.Warning, "Dependent resources exist.", resource.Id);
        _handler.DiffFactory = (desired, _) => new DeploymentChange(
            desired.Id,
            DeploymentChangeAction.Delete,
            DeploymentChangeStatus.Ready,
            diagnostics: [diagnostic]);
        var engine = CreateEngine();

        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        Assert.Contains(diagnostic, Assert.Single(plan.Changes).Diagnostics);
    }

    [Fact]
    public async Task DiffAsyncOrdersChangesDeterministicallyByResourceIdentity()
    {
        var second = DeploymentEngineTestFixtures.Resource(logicalId: "b");
        var first = DeploymentEngineTestFixtures.Resource(logicalId: "a");
        var engine = CreateEngine();

        var plan = await engine.DiffAsync(new TestArtifactReader(second, first), _target);

        Assert.Equal(["a", "b"], plan.Changes.Select(x => x.ResourceId.LogicalId));
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

        Assert.Single(readPlan.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.ReadFailed &&
            x.ResourceId == readFailure.Id);
        Assert.Single(diffPlan.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.DiffFailed &&
            x.ResourceId == diffFailure.Id);
    }

    [Fact]
    public async Task DiffAsyncAddsMissingHandlersToPlanDiagnostics()
    {
        var resource = DeploymentEngineTestFixtures.Resource("recipe", "seed");
        var engine = CreateEngine();

        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);
        var result = await engine.ApplyAsync(plan, _target);

        Assert.Single(plan.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.HandlerMissing &&
            x.ResourceId == resource.Id);
        Assert.Equal(DeploymentChangeStatus.Blocked, Assert.Single(plan.Changes).Status);
        Assert.Equal(DeploymentStatus.Failed, result.Status);
        Assert.Empty(_handler.ApplyChanges);
    }

    [Fact]
    public async Task DiffAsyncPreservesArtifactIdentityWhenResourceReadFails()
    {
        var artifact = new TestArtifactReader
        {
            ResourcesReadException = new InvalidOperationException("Resource read failed.")
        };
        var engine = CreateEngine();

        var plan = await engine.DiffAsync(artifact, _target);

        Assert.Equal(DeploymentEngineTestFixtures.Artifact, plan.Artifact);
        Assert.Single(plan.Diagnostics, x => x.Code == DeploymentEngineDiagnosticCodes.ArtifactInvalid);
    }

    [Fact]
    public async Task DryRunAsyncDoesNotApplyResourcesOrRecordHistory()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.DryRunAsync(plan, _target);
        var history = await _history.FindAsync(result.DeploymentId);

        Assert.Equal(DeploymentStatus.DryRunCompleted, result.Status);
        Assert.Single(_handler.DryRunChanges);
        Assert.Empty(_handler.ApplyChanges);
        Assert.Null(history);
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

        Assert.Equal(DeploymentStatus.ValidationFailed, result.Status);
        Assert.Equal(DeploymentChangeStatus.Skipped, Assert.Single(result.ResourceResults).Status);
        Assert.Empty(_handler.DryRunChanges);
        Assert.Empty(_handler.ApplyChanges);
    }

    [Fact]
    public async Task DryRunAsyncReturnsNoOpWhenEveryChangeIsSkipped()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        var change = new DeploymentChange(resource.Id, DeploymentChangeAction.Delete, DeploymentChangeStatus.Blocked, resource: resource);
        var plan = new DeploymentPlan("plan-1", DeploymentEngineTestFixtures.Artifact, DeploymentEngineTestFixtures.TargetDescriptor, [change]);
        var engine = CreateEngine();

        var result = await engine.DryRunAsync(plan, _target);

        Assert.Equal(DeploymentStatus.NoOp, result.Status);
        Assert.Equal(DeploymentChangeStatus.Skipped, Assert.Single(result.ResourceResults).Status);
        Assert.Empty(_handler.DryRunChanges);
    }

    [Fact]
    public async Task DryRunAsyncReportsHandlerExceptionsWithDryRunCode()
    {
        var resource = DeploymentEngineTestFixtures.Resource();
        _handler.DryRunException = new InvalidOperationException("Dry-run exploded.");
        var engine = CreateEngine();
        var plan = await engine.DiffAsync(new TestArtifactReader(resource), _target);

        var result = await engine.DryRunAsync(plan, _target);

        Assert.Equal(DeploymentStatus.ValidationFailed, result.Status);
        Assert.Single(result.Diagnostics, x =>
            x.Code == DeploymentEngineDiagnosticCodes.DryRunFailed &&
            x.ResourceId == resource.Id);
    }

    private DeploymentEngine CreateEngine() =>
        new([_handler], _history, DeploymentEngineTestFixtures.StableOptions());
}
