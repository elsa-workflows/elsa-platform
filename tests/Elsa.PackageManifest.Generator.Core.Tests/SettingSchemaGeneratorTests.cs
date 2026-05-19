using Elsa.PackageManifest.Generator.Core.SchemaGeneration;
using Elsa.PackageManifest.Generator.Core.AssemblyInspection;
using Elsa.PackageManifest.Generator.Core.Validation;
using FluentAssertions;
using System.Diagnostics;

namespace Elsa.PackageManifest.Generator.Core.Tests;

public sealed class SettingSchemaGeneratorTests
{
    public static TheoryData<Type, string> CommonClrTypes => new()
    {
        { typeof(string), "string" },
        { typeof(bool), "boolean" },
        { typeof(int), "integer" },
        { typeof(decimal), "number" },
        { typeof(Uri), "string" }
    };

    [Theory]
    [MemberData(nameof(CommonClrTypes))]
    public void Generate_maps_common_clr_types(Type type, string expectedJsonType)
    {
        var schema = new SettingSchemaGenerator().Generate(type, true, new Dictionary<string, object?>());

        schema.JsonType.Should().Be(expectedJsonType);
        schema.Diagnostics.Should().BeEmpty();
    }

    public static TheoryData<Type> DelegateShapedTypes => new()
    {
        typeof(Action<string>),
        typeof(Func<IServiceProvider, object>),
        typeof(Action<IServiceProvider, HttpClient>),
        typeof(IDictionary<string, Func<IServiceProvider, ValueTask<object>>>),
        typeof(IReadOnlyDictionary<string, List<Func<IServiceProvider, object>>>),
        typeof(Func<IServiceProvider, object>[])
    };

    [Theory]
    [MemberData(nameof(DelegateShapedTypes))]
    public void IsDelegateOrContainsDelegate_identifies_delegate_shapes(Type type)
    {
        TypeMetadataHelpers.IsDelegateOrContainsDelegate(type).Should().BeTrue();
    }

    public static TheoryData<Type> NonDelegateSettingTypes => new()
    {
        typeof(string),
        typeof(Dictionary<string, int>),
        typeof(List<string>),
        typeof(Uri)
    };

    [Theory]
    [MemberData(nameof(NonDelegateSettingTypes))]
    public void IsDelegateOrContainsDelegate_ignores_non_delegate_shapes(Type type)
    {
        TypeMetadataHelpers.IsDelegateOrContainsDelegate(type).Should().BeFalse();
    }

    [Fact]
    public void IsDelegateOrContainsDelegate_stays_fast_for_representative_modules()
    {
        var types = Enumerable.Range(0, 500)
            .Select(i => i % 2 == 0
                ? typeof(Dictionary<string, Func<IServiceProvider, ValueTask<object>>>)
                : typeof(Dictionary<string, List<string>>))
            .ToArray();

        var stopwatch = Stopwatch.StartNew();
        foreach (var type in types)
            TypeMetadataHelpers.IsDelegateOrContainsDelegate(type);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }
}
