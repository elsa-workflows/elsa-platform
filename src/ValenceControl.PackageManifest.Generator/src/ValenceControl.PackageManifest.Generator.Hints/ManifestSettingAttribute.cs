using System;

#nullable enable

namespace ValenceControl.PackageManifest.Generator.Hints;

/// <summary>
/// Supplies manifest-only metadata for a CShells feature setting property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ManifestSettingAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Group { get; set; }
    public bool Required { get; set; }
    public bool HasRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? UIHint { get; set; }
    [Obsolete("Use UIHint.")]
    public string? UiHint { get; set; }
    public bool Secret { get; set; }
    public bool Sensitive { get; set; }
    public bool RestartRequired { get; set; }
    public bool Advanced { get; set; }
    public bool Experimental { get; set; }
}
