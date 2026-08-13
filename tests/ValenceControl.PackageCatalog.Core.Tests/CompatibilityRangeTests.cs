using ValenceControl.PackageCatalog.Core.Compatibility;

namespace ValenceControl.PackageCatalog.Core.Tests;

public sealed class CompatibilityRangeTests
{
    private readonly VersionRangeEvaluator _ranges = new();

    [Fact]
    public void Evaluates_inclusive_and_exclusive_ranges()
    {
        Assert.True(_ranges.Includes("[1.0.0,2.0.0)", "1.5.0"));
        Assert.False(_ranges.Includes("[1.0.0,2.0.0)", "2.0.0"));
    }

    [Fact]
    public void Treats_two_part_and_three_part_versions_as_same_release()
    {
        Assert.True(_ranges.Includes(">=3.0.0", "3.0"));
    }

    [Fact]
    public void Rejects_malformed_bracket_bounds()
    {
        Assert.False(_ranges.Includes("[abc,)", "3.0.0"));
        Assert.False(_ranges.Includes("[1.0.0,abc)", "3.0.0"));
    }
}
