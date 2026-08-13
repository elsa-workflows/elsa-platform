using System.Collections.Concurrent;

namespace ValenceControl.Workflows.RuntimeApplier;

public sealed class InMemoryWorkflowDefinitionRuntimeStore : IWorkflowDefinitionRuntimeStore
{
    private readonly ConcurrentDictionary<string, WorkflowDefinitionRuntimeStoreRequest> _definitions = new(StringComparer.Ordinal);

    public IReadOnlyCollection<WorkflowDefinitionRuntimeStoreRequest> Definitions => _definitions.Values.ToList();

    public Task<WorkflowDefinitionRuntimeStoreResult> SaveAsync(
        WorkflowDefinitionRuntimeStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        _definitions[request.WorkflowDefinitionId] = request;
        return Task.FromResult(new WorkflowDefinitionRuntimeStoreResult($"elsa://workflows/{request.WorkflowDefinitionId}", []));
    }
}
