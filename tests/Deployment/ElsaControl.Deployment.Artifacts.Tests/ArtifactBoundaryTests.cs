using System.Reflection;
using System.Xml.Linq;

namespace ElsaControl.Deployment.Artifacts.Tests;

public class ArtifactBoundaryTests
{
    private readonly DirectoryInfo _repoRoot = FindRepoRoot();
    private readonly string[] _forbiddenReferenceFragments =
    [
        "ElsaControl.Deployment.Engine",
        "ElsaControl.Deployment.Cli",
        "ElsaControl.Deployment.Api",
        "ElsaControl.Api",
        "ElsaControl.PackageCatalog.Core",
        "ElsaControl.PackageCatalog.Persistence",
        "ElsaControl.Console",
        "ElsaControl.PackageCatalog.Sources",
        "ElsaControl.RuntimeBuilder",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Kubernetes",
        "k8s",
        "OCI",
        "OpenContainer",
        "ORAS",
        "Sigstore",
        "Cosign",
        "ElsaControl.Deployment.Signing",
        "ElsaControl.Deployment.Policy",
        "ElsaControl.Deployment.Approval",
        "ElsaControl.Deployment.Attestation"
    ];

    private readonly string[] _forbiddenSourcePhrases =
    [
        "ElsaControl.Deployment.Engine",
        "ElsaControl.Deployment.Cli",
        "ElsaControl.Deployment.Api",
        "ElsaControl.PackageCatalog",
        "ElsaControl.RuntimeBuilder",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "using Kubernetes",
        "using k8s",
        "using Oci",
        "using ORAS",
        "using Sigstore",
        "using Cosign",
        "using ElsaControl.Deployment.Signing",
        "using ElsaControl.Deployment.Policy",
        "using ElsaControl.Deployment.Approval",
        "using ElsaControl.Deployment.Attestation",
        "workflow instance",
        "workflow instances",
        "bookmark",
        "bookmarks",
        "execution state",
        "execution log",
        "execution logs",
        "distributed lock",
        "distributed locks",
        "runtime queue",
        "runtime queues",
        "transient runtime state",
        "live state",
        "deployment history",
        "execute deployment plan",
        "executes deployment plan",
        "executing deployment plan",
        "apply resources",
        "applies resources",
        "applying resources",
        "resolve secrets",
        "resolves secrets",
        "resolving secrets",
        "Assembly.Load"
    ];

    private readonly string[] _forbiddenPublicContractFragments =
    [
        "ElsaControl.Deployment.Engine",
        "ElsaControl.Deployment.Cli",
        "ElsaControl.Deployment.Api",
        "ElsaControl.Api",
        "ElsaControl.PackageCatalog.Core",
        "ElsaControl.PackageCatalog.Persistence",
        "ElsaControl.PackageCatalog.Sources",
        "ElsaControl.RuntimeBuilder",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.Hosting",
        "Microsoft.EntityFrameworkCore",
        "Kubernetes",
        "k8s",
        "OCI",
        "OpenContainer",
        "ORAS",
        "Sigstore",
        "Cosign",
        "ElsaControl.Deployment.Signing",
        "ElsaControl.Deployment.Policy",
        "ElsaControl.Deployment.Approval",
        "ElsaControl.Deployment.Attestation"
    ];

    [Fact]
    public void ArtifactProjectReferencesOnlyAllowedProjects()
    {
        var projectFile = _repoRoot
            .GetFiles("ElsaControl.Deployment.Artifacts.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(Path.Combine("src", "Deployment", "ElsaControl.Deployment.Artifacts"), StringComparison.Ordinal));
        var references = ReferencesFrom(projectFile);

        Assert.Contains(references, reference => reference.Contains("ElsaControl.Deployment.Abstractions", StringComparison.Ordinal));
        Assert.Contains(references, reference => reference.Contains("ElsaControl.Deployment.Manifest", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference =>
            _forbiddenReferenceFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ArtifactTestProjectReferencesOnlyFocusedTestDependencies()
    {
        var projectFile = _repoRoot
            .GetFiles("ElsaControl.Deployment.Artifacts.Tests.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(Path.Combine("tests", "Deployment", "ElsaControl.Deployment.Artifacts.Tests"), StringComparison.Ordinal));
        var references = ReferencesFrom(projectFile);
        var packageReferences = references.Where(reference => !reference.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        var allowedPackageReferences = new[]
        {
            "Microsoft.NET.Test.Sdk",
            "xunit",
            "xunit.runner.visualstudio"
        };

        Assert.All(packageReferences, reference => Assert.True(allowedPackageReferences.Contains(reference)));
        Assert.DoesNotContain(references, reference =>
            _forbiddenReferenceFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ArtifactSourceDoesNotUseDeferredDeploymentBoundaries()
    {
        var sourceRoot = new DirectoryInfo(Path.Combine(_repoRoot.FullName, "src", "Deployment", "ElsaControl.Deployment.Artifacts"));
        var sourceText = string.Join(
            Environment.NewLine,
            sourceRoot
                .GetFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(file => File.ReadAllText(file.FullName)));

        foreach (var phrase in _forbiddenSourcePhrases)
            Assert.False(sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase), $"deployment artifacts must not depend on {phrase}");
    }

    [Fact]
    public void PublicArtifactContractsDoNotExposeDeferredImplementationTypes()
    {
        var assembly = Assembly.Load("ElsaControl.Deployment.Artifacts");
        var exposedContractFragments = assembly
            .GetExportedTypes()
            .SelectMany(ExposedContractFragmentsFrom)
            .Where(fragment => _forbiddenPublicContractFragments.Any(forbidden =>
                fragment.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(exposedContractFragments);
    }

    private static IReadOnlyCollection<string> ReferencesFrom(FileInfo projectFile)
    {
        var document = XDocument.Load(projectFile.FullName);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
    }

    private static IEnumerable<string> ExposedContractFragmentsFrom(Type type)
    {
        yield return ContractNameOf(type);

        if (type.BaseType is not null && type.BaseType != typeof(object))
            yield return ContractNameOf(type.BaseType);

        foreach (var interfaceType in type.GetInterfaces())
            yield return ContractNameOf(interfaceType);

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return ContractNameOf(parameter.ParameterType);
        }

        foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            yield return ContractNameOf(eventInfo.EventHandlerType!);

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            yield return ContractNameOf(field.FieldType);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            yield return ContractNameOf(property.PropertyType);

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.IsSpecialName)
                continue;

            yield return ContractNameOf(method.ReturnType);

            foreach (var parameter in method.GetParameters())
                yield return ContractNameOf(parameter.ParameterType);
        }
    }

    private static string ContractNameOf(Type type)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
            return ContractNameOf(type.GetElementType()!);

        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var genericArguments = string.Join(", ", type.GetGenericArguments().Select(ContractNameOf));
        return $"{type.FullName}<{genericArguments}>";
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElsaControl.sln")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
