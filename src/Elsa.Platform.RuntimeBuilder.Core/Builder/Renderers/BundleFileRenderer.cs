using Elsa.Platform.RuntimeBuilder.Abstractions;

namespace Elsa.Platform.RuntimeBuilder.Core.Builder.Renderers;

public interface IBundleFileRenderer
{
    int Order { get; }
    BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings);
}
