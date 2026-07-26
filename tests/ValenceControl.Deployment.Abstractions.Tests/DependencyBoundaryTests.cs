using System.Xml.Linq;
using FluentAssertions;

namespace ValenceControl.Deployment.Abstractions.Tests;

public class DependencyBoundaryTests
{
    private readonly DirectoryInfo _repoRoot = FindRepoRoot();
    private readonly string[] _forbiddenReferenceFragments =
    [
        "ValenceControl.Api",
        "ValenceControl.PackageCatalog.Core",
        "ValenceControl.Console",
        "ValenceControl.PackageCatalog.Persistence",
        "ValenceControl.PackageCatalog.Sources",
        "ValenceControl.RuntimeBuilder.Core",
        "ValenceControl.RuntimeBuilder.DeploymentTemplates",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore"
    ];

    private readonly string[] _forbiddenSourcePhrases =
    [
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
        "transient runtime state"
    ];

    [Fact]
    public void DeploymentAbstractionsProjectHasNoForbiddenReferences()
    {
        var projectFile = _repoRoot
            .GetFiles("ValenceControl.Deployment.Abstractions.csproj", SearchOption.AllDirectories)
            .Single(file => file.FullName.Contains(
                Path.Combine("src", "ValenceControl.Deployment.Abstractions"),
                StringComparison.Ordinal));
        var document = XDocument.Load(projectFile.FullName);
        var references = document
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        references.Should().NotContain(reference =>
            _forbiddenReferenceFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void PublicDeploymentSourcesDoNotModelRuntimeStateVocabulary()
    {
        var sourceRoot = new DirectoryInfo(Path.Combine(_repoRoot.FullName, "src", "ValenceControl.Deployment.Abstractions"));
        var sourceText = string.Join(
            Environment.NewLine,
            sourceRoot
                .GetFiles("*.cs", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(file => File.ReadAllText(file.FullName)));

        foreach (var phrase in _forbiddenSourcePhrases)
            sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ValenceControl.sln")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
