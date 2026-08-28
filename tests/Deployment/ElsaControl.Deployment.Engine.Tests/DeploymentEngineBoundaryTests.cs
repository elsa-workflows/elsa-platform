using System.Xml.Linq;

namespace ElsaControl.Deployment.Engine.Tests;

public class DeploymentEngineBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string EngineProject = Path.Combine(RepositoryRoot, "src", "Deployment", "ElsaControl.Deployment.Engine", "ElsaControl.Deployment.Engine.csproj");
    private static readonly string EngineSource = Path.Combine(RepositoryRoot, "src", "Deployment", "ElsaControl.Deployment.Engine");

    [Fact]
    public void EngineProjectReferencesOnlyDeploymentAbstractions()
    {
        var document = XDocument.Load(EngineProject);
        var references = document.Descendants("ProjectReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => x is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal([@"..\ElsaControl.Deployment.Abstractions\ElsaControl.Deployment.Abstractions.csproj"], references);
    }

    [Fact]
    public void EngineSourceDoesNotImportForbiddenControlPackages()
    {
        var forbidden = new[]
        {
            "ElsaControl.Deployment.Artifacts",
            "ElsaControl.Deployment.Manifest",
            "ElsaControl.Deployment.Cli",
            "ElsaControl.Deployment.Api",
            "ElsaControl.PackageCatalog",
            "ElsaControl.RuntimeBuilder",
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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ElsaControl.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }
}
