namespace Elsa.Platform.PackageManifest.Generator.Testing;

public static class CShellsFeatureFixtures
{
    public const string AbstractionsSource = """
#nullable enable
using System;

namespace CShells.Features;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ShellFeatureAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public Type[] DependsOn { get; set; } = [];
    public object? Metadata { get; set; }
}

public interface IShellFeature;
""";

    public const string DelegateHooksFeatureSource = """
#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using CShells.Features;

namespace Sample.Features;

[ShellFeature("DelegateHooks", DisplayName = "Delegate Hooks")]
public sealed class DelegateHooksFeature : IShellFeature
{
    public string? Endpoint { get; set; }

    public Action<DelegateHookOptions>? Configure { get; set; }

    public Func<IServiceProvider, object>? ServiceFactory { get; set; }

    public Action<IServiceProvider, HttpClient>? ConfigureHttpClient { get; set; }

    public IDictionary<string, Func<IServiceProvider, ValueTask<object>>> Factories { get; set; } =
        new Dictionary<string, Func<IServiceProvider, ValueTask<object>>>();
}

public sealed class DelegateHookOptions
{
    public string Value { get; set; } = "";
}
""";

    public const string ManifestHintsSource = """
#nullable enable
using System;

namespace Elsa.Platform.PackageManifest.Generator.Hints;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestFeatureCategoryAttribute(string category) : Attribute
{
    public string Category { get; } = category;
}

[AttributeUsage(AttributeTargets.Property)]
internal sealed class ManifestSettingAttribute : Attribute
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Group { get; set; }
    public bool Required { get; set; }
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

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestUIOptionAttribute(string value) : Attribute
{
    public string Value { get; } = value;
    public string? Label { get; set; }
    public string? Description { get; set; }
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class ManifestUIOptionsProviderAttribute(string provider) : Attribute
{
    public string Provider { get; } = provider;
    public string[] DependsOn { get; set; } = [];
    public string[] Parameters { get; set; } = [];
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
internal sealed class ManifestIgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true)]
internal sealed class ManifestExtensionAttribute(string key, string value) : Attribute
{
    public string Key { get; } = key;
    public string Value { get; } = value;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestInfrastructureAttribute(string id, string kind) : Attribute
{
    public string Id { get; } = id;
    public string Kind { get; } = kind;
    public bool Optional { get; set; }
    public string? Reason { get; set; }
    public string[] Capabilities { get; set; } = [];
    public string[] Providers { get; set; } = [];
    public string[] ConfigurationKeys { get; set; } = [];
    public string[] Extensions { get; set; } = [];
}

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
internal sealed class ManifestRuntimeKindAttribute(string runtimeKind) : Attribute
{
    public string RuntimeKind { get; } = runtimeKind;
}

internal static class ElsaRuntimeKinds
{
    public const string Server = "elsa.server";
    public const string Studio = "elsa.studio";
}
""";
}
