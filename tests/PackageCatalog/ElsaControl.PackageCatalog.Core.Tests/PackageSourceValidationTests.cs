using ElsaControl.PackageCatalog.Core.Packages;
using ElsaControl.PackageCatalog.Core.Sources;

namespace ElsaControl.PackageCatalog.Core.Tests;

public sealed class PackageSourceValidationTests
{
    private readonly PackageSourceValidator _validator = new();

    [Fact]
    public void Requires_absolute_http_or_https_url()
    {
        var source = ValidSource();
        source.Url = "not-a-url";

        var result = _validator.Validate(source);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_urls_with_embedded_credentials()
    {
        var source = ValidSource();
        source.Url = "https://user:pass@example.test/v3/index.json";

        var result = _validator.Validate(source);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Requires_include_patterns()
    {
        var source = ValidSource();
        source.IncludePatterns = [];

        var result = _validator.Validate(source);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Accepts_supported_approval_policies()
    {
        var autoApprove = ValidSource();
        autoApprove.ApprovalPolicy = PackageSourceApprovalPolicy.AutoApprove;
        var manual = ValidSource();
        manual.ApprovalPolicy = PackageSourceApprovalPolicy.Manual;

        Assert.True(_validator.Validate(autoApprove).IsValid);
        Assert.True(_validator.Validate(manual).IsValid);
    }

    [Fact]
    public void Rejects_overflowing_polling_intervals_as_validation_errors()
    {
        var source = ValidSource();
        source.PollingInterval = "P999999999999999999999D";

        var result = _validator.Validate(source);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("Polling interval", StringComparison.Ordinal));
    }

    private static PackageSource ValidSource() => new()
    {
        Name = "NuGet",
        Url = "https://example.test/v3/index.json",
        IncludePatterns = ["Elsa.*"]
    };
}
