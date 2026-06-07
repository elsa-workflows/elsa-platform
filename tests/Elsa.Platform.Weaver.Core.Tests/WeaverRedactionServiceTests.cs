using Elsa.Platform.Weaver.Core.Safety;
using FluentAssertions;

namespace Elsa.Platform.Weaver.Core.Tests;

public sealed class WeaverRedactionServiceTests
{
    private readonly WeaverRedactionService _redaction = new();

    [Theory]
    [InlineData("apiKey=abc123")]
    [InlineData("token: ghp_123456")]
    [InlineData("ConnectionString=Server=.;Password=secret")]
    [InlineData("Authorization: Bearer abc.def.ghi")]
    public void Redact_replaces_secret_like_values(string input)
    {
        var result = _redaction.Redact(input);

        result.Redacted.Should().BeTrue();
        result.Value.Should().Contain("[REDACTED]");
        result.Value.Should().NotContain("abc123");
        result.Value.Should().NotContain("ghp_123456");
        result.Value.Should().NotContain("abc.def.ghi");
    }

    [Fact]
    public void Redact_preserves_safe_text()
    {
        var result = _redaction.Redact("Environment Dev has one healthy engine.");

        result.Redacted.Should().BeFalse();
        result.Value.Should().Be("Environment Dev has one healthy engine.");
    }
}
