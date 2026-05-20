using Elsa.Platform.Deployment.Abstractions.History;
using Elsa.Platform.Deployment.Abstractions.Targets;

namespace Elsa.Platform.Deployment.Engine;

public sealed class InMemoryDeploymentHistoryStore : IDeploymentHistoryStore
{
    private readonly Lock _lock = new();
    private readonly List<DeploymentHistoryRecord> _records = [];

    public ValueTask RecordAsync(DeploymentHistoryRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            _records.Add(record);

        return ValueTask.CompletedTask;
    }

    public ValueTask<DeploymentHistoryRecord?> FindAsync(string deploymentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            return ValueTask.FromResult(_records.LastOrDefault(record => record.DeploymentId == deploymentId));
    }

    public async IAsyncEnumerable<DeploymentHistoryRecord> ListAsync(
        DeploymentTargetDescriptor target,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DeploymentHistoryRecord[] snapshot;
        lock (_lock)
            snapshot = _records.Where(record => record.Target.Id == target.Id).ToArray();

        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }
}
