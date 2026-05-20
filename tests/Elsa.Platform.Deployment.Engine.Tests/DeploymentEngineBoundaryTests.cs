using System.Xml.Linq;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Engine.Tests;

public class DeploymentEngineBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string EngineProject = Path.Combine(RepositoryRoot, "src", "Elsa.Platform.Deployment.Engine", "Elsa.Platform.Deployment.Engine.csproj");
    private static readonly string EngineSource = Path.Combine(RepositoryRoot, "src", "Elsa.Platform.Deployment.Engine");

    [Fact]
    public void EngineProjectReferencesOnlyDeploymentAbstractions()
    {
        var document = XDocument.Load(EngineProject);
        var references = document.Descendants("ProjectReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => x is not null)
            .ToArray();

        references.Should().Equal(@"..\Elsa.Platform.Deployment.Abstractions\Elsa.Platform.Deployment.Abstractions.csproj");
    }

    [Fact]
    public void EngineSourceDoesNotImportForbiddenPlatformPackages()
    {
        var forbidden = new[]
        {
            "Elsa.Platform.Deployment.Artifacts",
            "Elsa.Platform.Deployment.Manifest",
            "Elsa.Platform.Deployment.Cli",
            "Elsa.Platform.Deployment.Api",
            "Elsa.Platform.PackageCatalog",
            "Elsa.Platform.RuntimeBuilder",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Kubernetes",
            "OCI"
        };

        var source = string.Join(Environment.NewLine, Directory.GetFiles(EngineSource, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        foreach (var namespaceFragment in forbidden)
            source.Should().NotContain(namespaceFragment);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Platform.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
