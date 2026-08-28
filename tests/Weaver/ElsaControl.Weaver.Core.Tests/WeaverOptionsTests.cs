using ElsaControl.Weaver.Core.Configuration;

namespace ElsaControl.Weaver.Core.Tests;

public sealed class WeaverOptionsTests
{
    [Fact]
    public void Disabled_weaver_is_unavailable()
    {
        var options = new WeaverOptions { Enabled = false, ProviderMode = WeaverProviderMode.Fake };

        var available = options.IsAvailable(out var reason);

        Assert.False(available);
        Assert.Equal("Weaver is disabled.", reason);
    }

    [Fact]
    public void Byok_requires_api_key_environment_variable_name()
    {
        var options = new WeaverOptions { Enabled = true, ProviderMode = WeaverProviderMode.BringYourOwnKey };

        var available = options.IsAvailable(out var reason);

        Assert.False(available);
        Assert.Equal("Weaver BYOK provider requires an API key environment variable.", reason);
    }

    [Fact]
    public void Fake_provider_is_available_when_enabled()
    {
        var options = new WeaverOptions { Enabled = true, ProviderMode = WeaverProviderMode.Fake };

        var available = options.IsAvailable(out var reason);

        Assert.True(available);
        Assert.Null(reason);
    }
}
