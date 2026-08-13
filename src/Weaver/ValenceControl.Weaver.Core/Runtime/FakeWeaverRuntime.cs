using System.Runtime.CompilerServices;
using ValenceControl.Weaver.Core.Sessions;
using ValenceControl.Weaver.Core.Tools;

namespace ValenceControl.Weaver.Core.Runtime;

public sealed class FakeWeaverRuntime(TimeProvider timeProvider, WeaverWorkspaceTools workspaceTools) : IWeaverRuntime
{
    public async IAsyncEnumerable<WeaverRuntimeEvent> SendAsync(
        WeaverRuntimeRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new WeaverToolStartedEvent("get_current_context", timeProvider.GetUtcNow());
        await Task.Yield();

        cancellationToken.ThrowIfCancellationRequested();

        var context = workspaceTools.GetCurrentContext(request.RoutePath, request.Context);

        yield return new WeaverToolCompletedEvent("get_current_context", context.Summary, timeProvider.GetUtcNow());
        yield return new WeaverAssistantDeltaEvent(
            $"Mode: {request.Mode}. I can inspect this workspace from {request.RoutePath ?? "the current page"}. {context.Summary}",
            timeProvider.GetUtcNow());

        yield return new WeaverRuntimeIdleEvent(timeProvider.GetUtcNow());
    }
}
