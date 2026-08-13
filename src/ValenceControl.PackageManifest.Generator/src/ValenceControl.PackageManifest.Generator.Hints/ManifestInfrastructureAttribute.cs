using System;

#nullable enable

namespace ValenceControl.PackageManifest.Generator.Hints;

/// <summary>
/// Supplies manifest-only infrastructure requirement metadata for a CShells feature.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestInfrastructureAttribute(string id, string kind) : Attribute
{
    /// <summary>
    /// Gets the infrastructure requirement identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the infrastructure kind, such as database, message-broker, cache, or blob-storage.
    /// </summary>
    public string Kind { get; } = kind;

    /// <summary>
    /// Gets or sets whether the infrastructure requirement is optional.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Gets or sets why the feature needs this infrastructure requirement.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets capability names required from the infrastructure provider.
    /// </summary>
    public string[] Capabilities { get; set; } = [];

    /// <summary>
    /// Gets or sets known provider names that can satisfy the requirement.
    /// </summary>
    public string[] Providers { get; set; } = [];

    /// <summary>
    /// Gets or sets configuration keys related to this requirement.
    /// </summary>
    public string[] ConfigurationKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets extension metadata as key=value pairs. Entries without a key before '=' are ignored.
    /// </summary>
    public string[] Extensions { get; set; } = [];
}
