using ElsaControl.Weaver.Core.Safety;

namespace ElsaControl.Weaver.Core.Tests;

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

        Assert.True(result.Redacted);
        Assert.Contains("[REDACTED]", result.Value);
        Assert.DoesNotContain("abc123", result.Value);
        Assert.DoesNotContain("ghp_123456", result.Value);
        Assert.DoesNotContain("abc.def.ghi", result.Value);
    }

    [Fact]
    public void Redact_preserves_safe_text()
    {
        var result = _redaction.Redact("Environment Dev has one healthy engine.");

        Assert.False(result.Redacted);
        Assert.Equal("Environment Dev has one healthy engine.", result.Value);
    }
}
