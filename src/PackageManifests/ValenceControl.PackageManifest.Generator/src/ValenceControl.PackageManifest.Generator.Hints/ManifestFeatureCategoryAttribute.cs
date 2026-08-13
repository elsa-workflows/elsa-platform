using System;

#nullable enable

namespace ValenceControl.PackageManifest.Generator.Hints;

/// <summary>
/// Adds one catalog category to a generated feature manifest.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestFeatureCategoryAttribute(string category) : Attribute
{
    public string Category { get; } = category;
}
