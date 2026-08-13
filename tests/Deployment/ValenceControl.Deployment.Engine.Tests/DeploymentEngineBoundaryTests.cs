using System.Xml.Linq;

namespace ValenceControl.Deployment.Engine.Tests;

public class DeploymentEngineBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string EngineProject = Path.Combine(RepositoryRoot, "src", "Deployment", "ValenceControl.Deployment.Engine", "ValenceControl.Deployment.Engine.csproj");
    private static readonly string EngineSource = Path.Combine(RepositoryRoot, "src", "Deployment", "ValenceControl.Deployment.Engine");

    [Fact]
    public void EngineProjectReferencesOnlyDeploymentAbstractions()
    {
        var document = XDocument.Load(EngineProject);
        var references = document.Descendants("ProjectReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => x is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal([@"..\ValenceControl.Deployment.Abstractions\ValenceControl.Deployment.Abstractions.csproj"], references);
    }

    [Fact]
    public void EngineSourceDoesNotImportForbiddenControlPackages()
    {
        var forbidden = new[]
        {
            "ValenceControl.Deployment.Artifacts",
            "ValenceControl.Deployment.Manifest",
            "ValenceControl.Deployment.Cli",
            "ValenceControl.Deployment.Api",
            "ValenceControl.PackageCatalog",
            "ValenceControl.RuntimeBuilder",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Kubernetes",
            "OCI"
        };

        var source = string.Join(Environment.NewLine, Directory.GetFiles(EngineSource, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        foreach (var namespaceFragment in forbidden)
            Assert.DoesNotContain(namespaceFragment, source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ValenceControl.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
