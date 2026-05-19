using Elsa.Platform.PackageCatalog.Core.Builder;

namespace Elsa.Platform.PackageCatalog.Core.Builder.Planner;

public sealed record BuilderPlanRequest(RuntimeBuilderIntent Intent);

public sealed record BuilderPlanResult(
    RuntimeBuilderIntent Resolved,
    BuilderPlanAutoAdded AutoAdded,
    IReadOnlyList<BundleFinding> Findings);

public sealed record BuilderPlanAutoAdded(
    IReadOnlyList<BundlePackageSelection> Packages,
    IReadOnlyList<string> Features,
    IReadOnlyList<InfrastructureSelection> Infrastructure);
