using Elsa.Platform.Deployment.Abstractions;
using Elsa.Platform.Deployment.Abstractions.Artifacts;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Plans;
using Elsa.Platform.Deployment.Abstractions.Resources;
using Elsa.Platform.Deployment.Abstractions.Targets;

namespace Elsa.Platform.Deployment.Engine.Tests;

internal static class DeploymentEngineTestFixtures
{
    public static readonly ArtifactDigest DesiredHash = new("sha256", "desired");
    public static readonly ArtifactDigest CurrentHash = new("sha256", "current");
    public static readonly DeploymentTargetDescriptor TargetDescriptor = new("staging", environment: "staging");
    public static readonly DeploymentArtifactIdentity Artifact = new(
        "sales",
        "v1alpha",
        new ArtifactDigest("sha256", "manifest"),
        new ArtifactDigest("sha256", "content"));

    public static DeploymentResource Resource(string type = "variable", string logicalId = "orderTimeout", ArtifactDigest? hash = null) =>
        new(new DeploymentResourceId(type, logicalId), desiredStateHash: hash ?? DesiredHash);

    public static DeploymentEngineOptions StableOptions() =>
        new()
        {
            DeploymentIdFactory = () => "deploy-1",
            PlanIdFactory = () => "plan-1",
            Clock = () => new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero)
        };
}

internal sealed class TestTarget : IDeploymentTarget
{
    public DeploymentTargetDescriptor Descriptor => DeploymentEngineTestFixtures.TargetDescriptor;
}

internal sealed class TestArtifactReader(params DeploymentResource[] resources) : IArtifactReader
{
    public Exception? ReadException { get; init; }

    public ValueTask<DeploymentArtifactMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (ReadException is not null)
            throw ReadException;

        return ValueTask.FromResult(new DeploymentArtifactMetadata(DeploymentEngineTestFixtures.Artifact, DateTimeOffset.UtcNow));
    }

    public ValueTask<IReadOnlyCollection<DeploymentResource>> ReadResourcesAsync(CancellationToken cancellationToken = default)
    {
        if (ReadException is not null)
            throw ReadException;

        return ValueTask.FromResult<IReadOnlyCollection<DeploymentResource>>(resources);
    }

    public ValueTask<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Stream>(new MemoryStream());
}

internal sealed class RecordingResourceHandler(string resourceType = "variable") : IResourceHandler
{
    private readonly Dictionary<DeploymentResourceId, DeploymentResourceState?> _states = [];

    public string ResourceType { get; } = resourceType;

    public List<DeploymentResource> ValidatedResources { get; } = [];

    public List<DeploymentChange> DryRunChanges { get; } = [];

    public List<DeploymentChange> ApplyChanges { get; } = [];

    public IReadOnlyCollection<DeploymentDiagnostic> ValidationDiagnostics { get; set; } = [];

    public Func<DeploymentResource, DeploymentResourceState?, DeploymentChange>? DiffFactory { get; set; }

    public Func<DeploymentChange, DeploymentResourceResult>? DryRunFactory { get; set; }

    public Func<DeploymentChange, DeploymentResourceResult>? ApplyFactory { get; set; }

    public Exception? ApplyException { get; set; }

    public void SetCurrentState(DeploymentResourceState state) => _states[state.Id] = state;

    public ValueTask<DeploymentResourceState?> ReadAsync(
        DeploymentResource resource,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        _states.TryGetValue(resource.Id, out var state);
        return ValueTask.FromResult(state);
    }

    public ValueTask<IReadOnlyCollection<DeploymentDiagnostic>> ValidateAsync(
        DeploymentResource resource,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        ValidatedResources.Add(resource);
        return ValueTask.FromResult(ValidationDiagnostics);
    }

    public ValueTask<DeploymentChange> DiffAsync(
        DeploymentResource desired,
        DeploymentResourceState? current,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        if (DiffFactory is not null)
            return ValueTask.FromResult(DiffFactory(desired, current));

        var action = current switch
        {
            null => DeploymentChangeAction.Create,
            _ when current.StateHash == desired.DesiredStateHash => DeploymentChangeAction.NoOp,
            _ => DeploymentChangeAction.Update
        };
        return ValueTask.FromResult(new DeploymentChange(desired.Id, action, DeploymentChangeStatus.Ready));
    }

    public ValueTask<DeploymentResourceResult> DryRunAsync(
        DeploymentChange change,
        DeploymentResource desired,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        DryRunChanges.Add(change);
        return ValueTask.FromResult(DryRunFactory?.Invoke(change) ?? Completed(change));
    }

    public ValueTask<DeploymentResourceResult> ApplyAsync(
        DeploymentChange change,
        DeploymentResource desired,
        IDeploymentTarget target,
        CancellationToken cancellationToken = default)
    {
        if (ApplyException is not null)
            throw ApplyException;

        ApplyChanges.Add(change);
        return ValueTask.FromResult(ApplyFactory?.Invoke(change) ?? Completed(change));
    }

    private static DeploymentResourceResult Completed(DeploymentChange change) =>
        new(change.ResourceId, change.Action, DeploymentChangeStatus.Completed);
}
