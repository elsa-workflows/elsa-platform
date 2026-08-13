using ValenceControl.Deployment.Abstractions.Diagnostics;
using ValenceControl.Deployment.Abstractions.Resources;

namespace ValenceControl.Deployment.Engine;

public sealed class ResourceHandlerRegistry
{
    private readonly Dictionary<string, IResourceHandler> _handlers = new(StringComparer.Ordinal);
    private readonly List<DeploymentDiagnostic> _diagnostics = [];

    public ResourceHandlerRegistry(IEnumerable<IResourceHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            if (!_handlers.TryAdd(handler.ResourceType, handler))
            {
                _diagnostics.Add(new DeploymentDiagnostic(
                    DeploymentEngineDiagnosticCodes.HandlerDuplicate,
                    DeploymentDiagnosticSeverity.Error,
                    $"Multiple resource handlers are registered for resource type '{handler.ResourceType}'."));
            }
        }
    }

    public IReadOnlyCollection<DeploymentDiagnostic> Diagnostics => _diagnostics;

    public bool TryGetHandler(string resourceType, out IResourceHandler handler) =>
        _handlers.TryGetValue(resourceType, out handler!);
}
