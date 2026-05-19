using System;

#nullable enable

namespace Elsa.PackageManifest.Generator.Hints;

/// <summary>
/// Excludes a CShells feature type or feature setting property from manifest generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ManifestIgnoreAttribute : Attribute;
