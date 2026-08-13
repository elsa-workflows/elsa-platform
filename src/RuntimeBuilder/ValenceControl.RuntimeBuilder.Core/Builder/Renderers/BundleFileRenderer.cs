using ValenceControl.RuntimeBuilder.Abstractions;

namespace ValenceControl.RuntimeBuilder.Core.Builder.Renderers;

public interface IBundleFileRenderer
{
    int Order { get; }
    BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings);
}
