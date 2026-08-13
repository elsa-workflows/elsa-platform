using ValenceControl.Deployment.Abstractions.Artifacts;
using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.History;
using ValenceControl.Deployment.Abstractions.Plans;
using ValenceControl.Deployment.Abstractions.Resources;
using ValenceControl.Deployment.Abstractions.Targets;

namespace ValenceControl.Deployment.Abstractions.Tests;

public class ExtensionContractTests
{
    private readonly DeploymentResource _resource = new(new DeploymentResourceId("variable", "orderTimeout"));
    private readonly DeploymentTargetDescriptor _targetDescriptor = new("staging");
    private readonly DeploymentArtifactIdentity _artifact = new(
        "sales",
        "v1alpha",
        new ArtifactDigest("sha256", "manifest"),
        new ArtifactDigest("sha256", "content"));

    [Fact]
    public async Task ResourceHandlerCanReadValidateDiffDryRunAndApply()
    {
        var target = new SampleTarget(_targetDescriptor);
        var handler = new SampleResourceHandler();
        var current = await handler.ReadAsync(_resource, target);
        var diagnostics = await handler.ValidateAsync(_resource, target);
        var change = await handler.DiffAsync(_resource, current, target);
        var dryRun = await handler.DryRunAsync(change, _resource, target);
        var apply = await handler.ApplyAsync(change, _resource, target);

        Assert.Equal("variable", handler.ResourceType);
        Assert.Empty(diagnostics);
        Assert.Equal(DeploymentChangeAction.NoOp, change.Action);
        Assert.Equal(DeploymentChangeStatus.Completed, dryRun.Status);
        Assert.Equal(DeploymentChangeStatus.Completed, apply.Status);
    }

    [Fact]
    public async Task ArtifactReaderAndWriterCanOperateOnMetadataAndContent()
    {
        var metadata = new DeploymentArtifactMetadata(_artifact, DateTimeOffset.UtcNow);
        var artifact = new SampleArtifact(metadata);

        await artifact.WriteMetadataAsync(metadata);
        await artifact.WriteResourcesAsync([_resource]);
        await artifact.WriteAsync("manifest.yaml", new MemoryStream("apiVersion: v1alpha"u8.ToArray()));
        var readMetadata = await artifact.ReadMetadataAsync();
        var resources = await artifact.ReadResourcesAsync();
        await using var content = await artifact.OpenReadAsync("manifest.yaml");

        Assert.Equal(metadata, readMetadata);
        Assert.Equal(_resource, Assert.Single(resources));
        Assert.True(content.Length > 0);
    }

    [Fact]
    public async Task EngineAndHistoryStoreContractsComposeAroundSamePlanAndResultTypes()
    {
        var target = new SampleTarget(_targetDescriptor);
        var artifact = new SampleArtifact(new DeploymentArtifactMetadata(_artifact, DateTimeOffset.UtcNow));
        var engine = new SampleEngine();
        var history = new SampleHistoryStore();

        var plan = await engine.DiffAsync(artifact, target);
        var result = await engine.ApplyAsync(plan, target);
        var record = new DeploymentHistoryRecord(result.DeploymentId, result.Artifact, result.Target, result.Status, resourceResults: result.ResourceResults);
        await history.RecordAsync(record);

        var stored = await history.FindAsync(result.DeploymentId);
        Assert.Equal(record, stored);
    }

    private sealed class SampleTarget(DeploymentTargetDescriptor descriptor) : IDeploymentTarget
    {
        public DeploymentTargetDescriptor Descriptor { get; } = descriptor;
    }

    private sealed class SampleResourceHandler : IResourceHandler
    {
        public string ResourceType => "variable";

        public ValueTask<DeploymentResourceState?> ReadAsync(
            DeploymentResource resource,
            IDeploymentTarget target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DeploymentResourceState?>(new DeploymentResourceState(resource.Id, resource.DesiredStateHash));

        public ValueTask<IReadOnlyCollection<DeploymentDiagnostic>> ValidateAsync(
            DeploymentResource resource,
            IDeploymentTarget target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<DeploymentDiagnostic>>([]);

        public ValueTask<DeploymentChange> DiffAsync(
            DeploymentResource desired,
            DeploymentResourceState? current,
            IDeploymentTarget target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeploymentChange(desired.Id, DeploymentChangeAction.NoOp, DeploymentChangeStatus.Ready));

        public ValueTask<DeploymentResourceResult> DryRunAsync(
            DeploymentChange change,
            DeploymentResource desired,
            IDeploymentTarget target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeploymentResourceResult(desired.Id, change.Action, DeploymentChangeStatus.Completed));

        public ValueTask<DeploymentResourceResult> ApplyAsync(
            DeploymentChange change,
            DeploymentResource desired,
            IDeploymentTarget target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DeploymentResourceResult(desired.Id, change.Action, DeploymentChangeStatus.Completed));
    }

    private sealed class SampleArtifact(DeploymentArtifactMetadata metadata) : IArtifactReader, IArtifactWriter
    {
        private readonly Dictionary<string, byte[]> _content = new();
        private IReadOnlyCollection<DeploymentResource> _resources = [];
        private DeploymentArtifactMetadata _metadata = metadata;

        public ValueTask<DeploymentArtifactMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_metadata);

        public ValueTask<IReadOnlyCollection<DeploymentResource>> ReadResourcesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_resources);

        public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream(_content[path], writable: false));

        public ValueTask WriteMetadataAsync(DeploymentArtifactMetadata metadata, CancellationToken cancellationToken = default)
        {
            _metadata = metadata;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteResourcesAsync(IReadOnlyCollection<DeploymentResource> resources)
        {
            _resources = resources;
            return ValueTask.CompletedTask;
        }

        public async ValueTask WriteAsync(string path, Stream content, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            _content[path] = memory.ToArray();
        }
    }

    private sealed class SampleEngine : IDeploymentEngine
    {
        public async ValueTask<DeploymentResult> ValidateAsync(
            IArtifactReader artifact,
            IDeploymentTarget target,
            DeploymentExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var metadata = await artifact.ReadMetadataAsync(cancellationToken);
            return new DeploymentResult("deploy-1", DeploymentOperationMode.Validate, DeploymentStatus.Validated, metadata.Identity, target.Descriptor);
        }

        public async ValueTask<DeploymentPlan> DiffAsync(
            IArtifactReader artifact,
            IDeploymentTarget target,
            DeploymentExecutionContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var metadata = await artifact.ReadMetadataAsync(cancellationToken);
            var resourceId = new DeploymentResourceId("variable", "orderTimeout");
            return new DeploymentPlan("plan-1", metadata.Identity, target.Descriptor, [new DeploymentChange(resourceId, DeploymentChangeAction.NoOp)]);
        }

        public ValueTask<DeploymentResult> DryRunAsync(
            DeploymentPlan plan,
            IDeploymentTarget target,
            DeploymentExecutionContext? context = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateResult(plan, target, DeploymentOperationMode.DryRun, DeploymentStatus.DryRunCompleted));

        public ValueTask<DeploymentResult> ApplyAsync(
            DeploymentPlan plan,
            IDeploymentTarget target,
            DeploymentExecutionContext? context = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateResult(plan, target, DeploymentOperationMode.Apply, DeploymentStatus.NoOp));

        private static DeploymentResult CreateResult(
            DeploymentPlan plan,
            IDeploymentTarget target,
            DeploymentOperationMode mode,
            DeploymentStatus status)
        {
            var resourceResults = plan.Changes.Select(change =>
                new DeploymentResourceResult(change.ResourceId, change.Action, DeploymentChangeStatus.Completed));
            return new DeploymentResult("deploy-1", mode, status, plan.Artifact, target.Descriptor, plan, resourceResults);
        }
    }

    private sealed class SampleHistoryStore : IDeploymentHistoryStore
    {
        private readonly List<DeploymentHistoryRecord> _records = [];

        public ValueTask RecordAsync(DeploymentHistoryRecord record, CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<DeploymentHistoryRecord?> FindAsync(string deploymentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_records.SingleOrDefault(record => record.DeploymentId == deploymentId));

        public async IAsyncEnumerable<DeploymentHistoryRecord> ListAsync(
            DeploymentTargetDescriptor target,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var record in _records.Where(record => record.Target.Id == target.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return record;
                await Task.Yield();
            }
        }
    }
}
