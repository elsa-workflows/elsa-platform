namespace Elsa.Platform.PackageManifest.Generator.Testing;

public static class TripwireFeatureFixtures
{
    public const string Source = """
using System;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("Tripwire", DisplayName = "Tripwire Feature")]
public sealed class TripwireFeature : IShellFeature
{
    public TripwireFeature() => throw new InvalidOperationException("The generator must not invoke feature constructors.");

    public string ExplodingGetter => throw new InvalidOperationException("The generator must not read property getters.");

    public string SafeSetting { get; set; } = "";
}
""";
}
