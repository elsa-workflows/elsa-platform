using System;

#nullable enable

namespace ValenceControl.PackageManifest.Generator.Hints;

/// <summary>
/// Supplies manifest-only runtime kind compatibility metadata for a package or CShells feature.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestRuntimeKindAttribute(string runtimeKind) : Attribute
{
    /// <summary>
    /// Gets the runtime kind identifier, such as elsa.server or elsa.studio.
    /// </summary>
    public string RuntimeKind { get; } = runtimeKind;
}
