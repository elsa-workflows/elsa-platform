using ElsaControl.RuntimeBuilder.Abstractions;

namespace ElsaControl.RuntimeBuilder.Core.Builder.Renderers;

public interface IBundleFileRenderer
{
    int Order { get; }
    BundleFile Render(BundleGenerationContext context, List<BundleFinding> findings);
}
