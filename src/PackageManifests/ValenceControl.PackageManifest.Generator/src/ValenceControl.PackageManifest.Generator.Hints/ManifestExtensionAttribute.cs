using System;

#nullable enable

namespace ValenceControl.PackageManifest.Generator.Hints;

/// <summary>
/// Supplies a small string extension metadata value for manifest generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestExtensionAttribute(string key, string value) : Attribute
{
    public string Key { get; } = key;
    public string Value { get; } = value;
}
