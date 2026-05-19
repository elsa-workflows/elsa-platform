using System;

#nullable enable

namespace Elsa.PackageManifest.Generator.Hints;

/// <summary>
/// References a runtime-owned provider that can supply setting UI options dynamically.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ManifestUIOptionsProviderAttribute(string provider)
    : Attribute
{
    public string Provider { get; } = provider;
    public string[] DependsOn { get; set; } = [];
    public string[] Parameters { get; set; } = [];
}
