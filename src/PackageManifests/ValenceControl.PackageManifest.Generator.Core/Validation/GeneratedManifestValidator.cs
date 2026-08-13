using ValenceControl.PackageManifests.Validation;

namespace ValenceControl.PackageManifest.Generator.Core.Validation;

public sealed class GeneratedManifestValidator
{
    private readonly ManifestValidator _validator = new();

    public ManifestValidationResult Validate(string manifestJson, string? packageId = null, string? version = null) =>
        _validator.Validate(manifestJson, packageId, version);
}
