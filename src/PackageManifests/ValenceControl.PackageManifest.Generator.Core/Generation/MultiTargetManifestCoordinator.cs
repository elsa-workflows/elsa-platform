using ValenceControl.PackageManifest.Generator.Core.Validation;

namespace ValenceControl.PackageManifest.Generator.Core.Generation;

public sealed class MultiTargetManifestCoordinator
{
    private readonly ManifestSurfaceComparer _comparer = new();

    public string SelectCanonical(IReadOnlyDictionary<string, string> manifestsByTargetFramework, bool allowDifferences, GenerationDiagnostics diagnostics)
    {
        if (manifestsByTargetFramework.Count == 0)
            return "";

        var first = manifestsByTargetFramework.First();
        var expectedSurface = _comparer.Normalize(first.Value);

        foreach (var item in manifestsByTargetFramework.Skip(1))
        {
            if (_comparer.Normalize(item.Value) == expectedSurface)
                continue;

            var diagnostic = MultiTargetingDiagnostics.FeatureSurfaceDifference(item.Key);
            if (allowDifferences)
                diagnostics.Warning(diagnostic.Code, diagnostic.Message, diagnostic.Target);
            else
                diagnostics.Add(diagnostic);
        }

        return first.Value;
    }
}
