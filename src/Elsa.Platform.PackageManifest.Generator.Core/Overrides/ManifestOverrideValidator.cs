using Elsa.Platform.PackageManifest.Generator.Core.Generation;
using Elsa.Platform.PackageManifest.Generator.Core.Validation;

namespace Elsa.Platform.PackageManifest.Generator.Core.Overrides;

public sealed class ManifestOverrideValidator
{
    public void Validate(ManifestOverride? manifestOverride, ProjectPackageMetadata metadata, GenerationDiagnostics diagnostics)
    {
        if (manifestOverride is null)
            return;

        if (manifestOverride.Features.Any(x => string.IsNullOrWhiteSpace(x.Id) && string.IsNullOrWhiteSpace(x.ClrTypeName)))
            diagnostics.Error("EPMGEN_OVERRIDE_FEATURE_ID", "Feature override entries must provide id or clrTypeName.", "elsa-package.overrides.json");
    }
}
