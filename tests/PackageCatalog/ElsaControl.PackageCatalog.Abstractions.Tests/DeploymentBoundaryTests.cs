using System.Xml.Linq;

namespace ElsaControl.PackageCatalog.Abstractions.Tests;

public sealed class DeploymentBoundaryTests
{
    private static readonly string[] ForbiddenProjectReferences =
    [
        "ElsaControl.Api",
        "ElsaControl.Console",
        "ElsaControl.PackageCatalog.Persistence",
        "ElsaControl.PackageCatalog.Sources.NuGet",
        "ElsaControl.AppHost"
    ];

    [Fact]
    public void Deployment_projects_do_not_reference_package_catalog_internals()
    {
        var root = FindRepositoryRoot();
        var deploymentProjects = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith("ElsaControl.Deployment.", StringComparison.Ordinal))
            .ToList();

        var forbiddenReferences = deploymentProjects
            .SelectMany(ReadProjectReferences)
            .Where(reference => ForbiddenProjectReferences.Any(forbidden => reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(forbiddenReferences);
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
