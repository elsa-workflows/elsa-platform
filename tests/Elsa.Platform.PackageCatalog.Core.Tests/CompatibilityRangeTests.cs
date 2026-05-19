using Elsa.Platform.PackageCatalog.Core.Compatibility;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class CompatibilityRangeTests
{
    private readonly VersionRangeEvaluator _ranges = new();

    [Fact]
    public void Evaluates_inclusive_and_exclusive_ranges()
    {
        _ranges.Includes("[1.0.0,2.0.0)", "1.5.0").Should().BeTrue();
        _ranges.Includes("[1.0.0,2.0.0)", "2.0.0").Should().BeFalse();
    }

    [Fact]
    public void Treats_two_part_and_three_part_versions_as_same_release()
    {
        _ranges.Includes(">=3.0.0", "3.0").Should().BeTrue();
    }

    [Fact]
    public void Rejects_malformed_bracket_bounds()
    {
        _ranges.Includes("[abc,)", "3.0.0").Should().BeFalse();
        _ranges.Includes("[1.0.0,abc)", "3.0.0").Should().BeFalse();
    }
}
