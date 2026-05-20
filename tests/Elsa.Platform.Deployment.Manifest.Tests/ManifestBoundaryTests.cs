using System.Xml.Linq;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Manifest.Tests;

public class ManifestBoundaryTests
{
    private readonly DirectoryInfo _repoRoot = FindRepoRoot();
    private readonly string[] _forbiddenReferenceFragments =
    [
        "Elsa.Platform.Deployment.Engine",
        "Elsa.Platform.Deployment.Cli",
        "Elsa.Platform.Deployment.Api",
        "Elsa.Platform.Deployment.Artifacts",
        "Elsa.Platform.PackageCatalog.Api",
        "Elsa.Platform.PackageCatalog.Core",
        "Elsa.Platform.PackageCatalog.Persistence",
        "Elsa.Platform.PackageCatalog.AdminUi",
        "Elsa.Platform.RuntimeBuilder.Core",
        "Elsa.Platform.RuntimeBuilder.DeploymentTemplates",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore"
    ];

    private readonly string[] _forbiddenSourcePhrases =
    [
        "Elsa.Platform.Deployment.Engine",
        "Elsa.Platform.Deployment.Cli",
        "Elsa.Platform.Deployment.Api",
        "Elsa.Platform.Deployment.Artifacts",
        "Elsa.Platform.PackageCatalog",
        "Elsa.Platform.RuntimeBuilder",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "File.ReadAllText",
        "File.OpenRead",
        "ZipFile",
        "HttpClient",
        "workflow instance",
        "workflow instances",
        "bookmark",
        "bookmarks",
        "execution state",
        "execution log",
        "runtime queue",
        "transient runtime state"
    ];

    [Fact]
    public void ManifestProjectReferencesOnlyAllowedProjects()
    {
        var projectFile = _repoRoot
            .GetFiles("Elsa.Platform.Deployment.Manifest.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(Path.Combine("src", "Elsa.Platform.Deployment.Manifest"), StringComparison.Ordinal));
        var document = XDocument.Load(projectFile.FullName);
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        references.Should().Contain(reference => reference.Contains("Elsa.Platform.Deployment.Abstractions", StringComparison.Ordinal));
        references.Should().NotContain(reference =>
            _forbiddenReferenceFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ManifestTestProjectReferencesOnlyFocusedTestDependencies()
    {
        var projectFile = _repoRoot
            .GetFiles("Elsa.Platform.Deployment.Manifest.Tests.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(Path.Combine("tests", "Elsa.Platform.Deployment.Manifest.Tests"), StringComparison.Ordinal));
        var references = ReferencesFrom(projectFile);
        var packageReferences = references.Where(reference => !reference.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        var allowedPackageReferences = new[]
        {
            "FluentAssertions",
            "Microsoft.NET.Test.Sdk",
            "xunit",
            "xunit.runner.visualstudio"
        };

        packageReferences.Should().OnlyContain(reference => allowedPackageReferences.Contains(reference));
        references.Should().NotContain(reference =>
            _forbiddenReferenceFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ManifestSourceDoesNotUseDeferredDeploymentBoundaries()
    {
        var sourceRoot = new DirectoryInfo(Path.Combine(_repoRoot.FullName, "src", "Elsa.Platform.Deployment.Manifest"));
        var sourceText = string.Join(
            Environment.NewLine,
            sourceRoot
                .GetFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(file => File.ReadAllText(file.FullName)));

        foreach (var phrase in _forbiddenSourcePhrases)
            sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase).Should().BeFalse($"manifest parsing must not depend on {phrase}");
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

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Platform.sln")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
