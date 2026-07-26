using ValenceControl.Weaver.Core.Configuration;
using FluentAssertions;

namespace ValenceControl.Weaver.Core.Tests;

public sealed class WeaverOptionsTests
{
    [Fact]
    public void Disabled_weaver_is_unavailable()
    {
        var options = new WeaverOptions { Enabled = false, ProviderMode = WeaverProviderMode.Fake };

        var available = options.IsAvailable(out var reason);

        available.Should().BeFalse();
        reason.Should().Be("Weaver is disabled.");
    }

    [Fact]
    public void Byok_requires_api_key_environment_variable_name()
    {
        var options = new WeaverOptions { Enabled = true, ProviderMode = WeaverProviderMode.BringYourOwnKey };

        var available = options.IsAvailable(out var reason);

        available.Should().BeFalse();
        reason.Should().Be("Weaver BYOK provider requires an API key environment variable.");
    }

    [Fact]
    public void Fake_provider_is_available_when_enabled()
    {
        var options = new WeaverOptions { Enabled = true, ProviderMode = WeaverProviderMode.Fake };

        var available = options.IsAvailable(out var reason);

        available.Should().BeTrue();
        reason.Should().BeNull();
    }
}
