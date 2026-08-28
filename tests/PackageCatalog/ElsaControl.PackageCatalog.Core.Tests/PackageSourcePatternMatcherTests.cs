using ElsaControl.PackageCatalog.Core.Sources;

namespace ElsaControl.PackageCatalog.Core.Tests;

public sealed class PackageSourcePatternMatcherTests
{
    private readonly PackageSourcePatternMatcher _matcher = new();

    [Fact]
    public void Matches_include_patterns_case_insensitively()
    {
        Assert.True(_matcher.IsMatch("elsa.email", ["Elsa.*"], []));
    }

    [Fact]
    public void Matches_dotted_prefix_patterns_case_insensitively()
    {
        Assert.True(_matcher.IsMatch("elsa.email", ["Elsa."], []));
    }

    [Fact]
    public void Exclude_patterns_win_over_includes()
    {
        Assert.False(_matcher.IsMatch("Elsa.Experimental.Email", ["Elsa."], ["Elsa.Experimental.*"]));
    }
}
