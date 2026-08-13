using System.Text;
using ValenceControl.PackageManifests.Validation;

namespace ValenceControl.PackageManifest.Generator.Core.Validation;

public sealed class GeneratedManifestSizeValidator
{
    public void Validate(string manifestJson, GenerationDiagnostics diagnostics)
    {
        if (Encoding.UTF8.GetByteCount(manifestJson) > ManifestValidator.MaxManifestBytes)
            diagnostics.Error("EPMGEN_MANIFEST_TOO_LARGE", "Generated manifest exceeds the 1 MB limit.", "$");
    }
}
