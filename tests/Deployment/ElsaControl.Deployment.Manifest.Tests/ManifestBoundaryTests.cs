using System.Xml.Linq;

namespace ElsaControl.Deployment.Manifest.Tests;

public class ManifestBoundaryTests
{
    private readonly DirectoryInfo _repoRoot = FindRepoRoot();
    private readonly string[] _forbiddenReferenceFragments =
    [
        "ElsaControl.Deployment.Engine",
        "ElsaControl.Deployment.Cli",
        "ElsaControl.Deployment.Api",
        "ElsaControl.Deployment.Artifacts",
        "ElsaControl.Api",
        "ElsaControl.PackageCatalog.Core",
        "ElsaControl.PackageCatalog.Persistence",
        "ElsaControl.Console",
        "ElsaControl.RuntimeBuilder.Core",
        "ElsaControl.RuntimeBuilder.DeploymentTemplates",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore"
    ];

    private readonly string[] _forbiddenSourcePhrases =
    [
        "ElsaControl.Deployment.Engine",
        "ElsaControl.Deployment.Cli",
        "ElsaControl.Deployment.Api",
        "ElsaControl.Deployment.Artifacts",
        "ElsaControl.PackageCatalog",
        "ElsaControl.RuntimeBuilder",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "File.ReadAllText",
        "File.OpenRead",
        "ZipFile",
        "HttpClient",
        "using Elsa.Workflows.Runtime",
        "using Elsa.Workflows.Management",
        "Elsa.Workflows.Runtime.",
        "Elsa.Workflows.Management."
    ];

    [Fact]
    public void ManifestProjectReferencesOnlyAllowedProjects()
    {
        var projectFile = _repoRoot
            .GetFiles("ElsaControl.Deployment.Manifest.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(Path.Combine("src", "Deployment", "ElsaControl.Deployment.Manifest"), StringComparison.Ordinal));
        var document = XDocument.Load(projectFile.FullName);
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.Contains(references, reference => reference.Contains("ElsaControl.Deployment.Abstractions", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference =>
            _forbiddenReferenceFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ManifestTestProjectReferencesOnlyFocusedTestDependencies()
    {
        var projectFile = _repoRoot
            .GetFiles("ElsaControl.Deployment.Manifest.Tests.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(Path.Combine("tests", "Deployment", "ElsaControl.Deployment.Manifest.Tests"), StringComparison.Ordinal));
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
    public void ManifestSourceDoesNotUseDeferredDeploymentBoundaries()
    {
        var sourceRoot = new DirectoryInfo(Path.Combine(_repoRoot.FullName, "src", "Deployment", "ElsaControl.Deployment.Manifest"));
        var sourceText = string.Join(
            Environment.NewLine,
            sourceRoot
                .GetFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(file => File.ReadAllText(file.FullName)));

        foreach (var phrase in _forbiddenSourcePhrases)
            Assert.False(sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase), $"manifest parsing must not depend on {phrase}");
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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElsaControl.sln")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
