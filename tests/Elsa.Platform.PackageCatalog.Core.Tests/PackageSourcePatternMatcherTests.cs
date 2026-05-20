using Elsa.Platform.PackageCatalog.Core.Sources;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Core.Tests;

public sealed class PackageSourcePatternMatcherTests
{
    private readonly PackageSourcePatternMatcher _matcher = new();

    [Fact]
    public void Matches_include_patterns_case_insensitively()
    {
        _matcher.IsMatch("elsa.email", ["Elsa.*"], []).Should().BeTrue();
    }

    [Fact]
    public void Exclude_patterns_win_over_includes()
    {
        _matcher.IsMatch("Elsa.Experimental.Email", ["Elsa.*"], ["Elsa.Experimental.*"]).Should().BeFalse();
    }
}
