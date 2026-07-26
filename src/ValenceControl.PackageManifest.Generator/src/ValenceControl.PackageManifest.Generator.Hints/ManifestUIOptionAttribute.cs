using System;

#nullable enable

namespace ValenceControl.PackageManifest.Generator.Hints;

/// <summary>
/// Declares one static option for a setting UI hint such as a select list.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestUIOptionAttribute(string value) : Attribute
{
    public string Value { get; } = value;
    public string? Label { get; set; }
    public string? Description { get; set; }
}
