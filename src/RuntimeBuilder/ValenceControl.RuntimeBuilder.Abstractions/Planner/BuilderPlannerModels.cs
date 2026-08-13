using ValenceControl.RuntimeBuilder.Abstractions;

namespace ValenceControl.RuntimeBuilder.Abstractions.Planner;

public sealed record BuilderPlanRequest(RuntimeBuilderIntent Intent);

public sealed record BuilderPlanResult(
    RuntimeBuilderIntent Resolved,
    BuilderPlanAutoAdded AutoAdded,
    IReadOnlyList<BundleFinding> Findings);

public sealed record BuilderPlanAutoAdded(
    IReadOnlyList<BundlePackageSelection> Packages,
    IReadOnlyList<string> Features,
    IReadOnlyList<InfrastructureSelection> Infrastructure);
