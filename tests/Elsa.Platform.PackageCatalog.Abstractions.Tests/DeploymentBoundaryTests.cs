using System.Xml.Linq;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Abstractions.Tests;

public sealed class DeploymentBoundaryTests
{
    private static readonly string[] ForbiddenProjectReferences =
    [
        "Elsa.Platform.Api",
        "Elsa.Platform.Console",
        "Elsa.Platform.PackageCatalog.Persistence",
        "Elsa.Platform.PackageCatalog.Sources.NuGet",
        "Elsa.Platform.AppHost"
    ];

    [Fact]
    public void Deployment_projects_do_not_reference_package_catalog_internals()
    {
        var root = FindRepositoryRoot();
        var deploymentProjects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith("Elsa.Platform.Deployment.", StringComparison.Ordinal))
            .ToList();

        var forbiddenReferences = deploymentProjects
            .SelectMany(ReadProjectReferences)
            .Where(reference => ForbiddenProjectReferences.Any(forbidden => reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        forbiddenReferences.Should().BeEmpty();
    }

    private static IEnumerable<string> ReadProjectReferences(string projectFile)
    {
        var document = XDocument.Load(projectFile);
        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
