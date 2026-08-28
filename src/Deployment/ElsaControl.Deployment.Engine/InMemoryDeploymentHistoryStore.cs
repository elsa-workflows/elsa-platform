using ElsaControl.Deployment.Abstractions.History;
using ElsaControl.Deployment.Abstractions.Targets;

namespace ElsaControl.Deployment.Engine;

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
            return ValueTask.FromResult(_records.FirstOrDefault(record => record.DeploymentId == deploymentId));
    }

    public async IAsyncEnumerable<DeploymentHistoryRecord> ListAsync(
        DeploymentTargetDescriptor target,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DeploymentHistoryRecord[] snapshot;
        lock (_lock)
            snapshot = _records.Where(record => record.Target.Id == target.Id).ToArray();

        await Task.CompletedTask;
        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }
}
