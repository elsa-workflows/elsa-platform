using ElsaControl.PackageCatalog.Abstractions.Compatibility;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class RuntimeKindCompatibilityPolicyTests
{
    [Fact]
    public void Feature_without_declared_runtime_kinds_is_offered_for_any_image()
    {
        // Most shipped manifests carry no compatibility block. Treating that as "incompatible" hid
        // every feature from the runtime builder.
        Assert.True(RuntimeKindCompatibilityPolicy.IsCompatible([], ["elsa.server"]));
        Assert.True(RuntimeKindCompatibilityPolicy.IsCompatible(null, ["elsa.server"]));
    }

    [Fact]
    public void Image_without_declared_runtime_kinds_accepts_every_feature()
    {
        Assert.True(RuntimeKindCompatibilityPolicy.IsCompatible(["elsa.studio"], []));
    }

    [Fact]
    public void Declared_runtime_kinds_still_constrain_the_match()
    {
        Assert.True(RuntimeKindCompatibilityPolicy.IsCompatible(["elsa.server"], ["elsa.server"]));
        Assert.True(RuntimeKindCompatibilityPolicy.IsCompatible(["ELSA.SERVER"], ["elsa.server"]));
        Assert.True(RuntimeKindCompatibilityPolicy.IsCompatible(["elsa.studio", "elsa.server"], ["elsa.server"]));
        Assert.False(RuntimeKindCompatibilityPolicy.IsCompatible(["elsa.studio"], ["elsa.server"]));
    }
}
