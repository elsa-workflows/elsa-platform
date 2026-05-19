using Elsa.Platform.PackageCatalog.Core.DeploymentTemplates;

namespace Elsa.Platform.PackageCatalog.Core.Builder;

public sealed class BundleFilePolicy
{
    public static readonly IReadOnlyList<string> RequiredFilePaths =
    [
        "config.json",
        "packages.lock.json",
        "docker-compose.yml",
        ".env.example",
        "README.md"
    ];

    public IReadOnlyList<BundleFinding> Validate(IReadOnlyList<BundleFile> files, string? target = null)
    {
        var findings = new List<BundleFinding>();
        foreach (var file in files)
        {
            if (!IsSafeRelativePath(file.Path))
                findings.Add(BundleFinding.Error("file.invalidPath", $"Generated file path {file.Path} is not a safe relative bundle path.", $"file:{file.Path}"));

            if (string.IsNullOrEmpty(file.Contents))
                findings.Add(BundleFinding.Error("file.empty", $"{file.Path} was generated empty.", $"file:{file.Path}"));
        }

        foreach (var requiredPath in RequiredPathsFor(target))
        {
            if (files.All(x => !string.Equals(x.Path, requiredPath, StringComparison.Ordinal)))
                findings.Add(BundleFinding.Error("file.requiredMissing", $"{requiredPath} was not generated.", $"file:{requiredPath}"));
        }

        return findings;
    }

    public static IReadOnlyList<string> RequiredPathsFor(string? target) =>
        target switch
        {
            DeploymentTemplateTargets.AzureContainerApps => ["config.json", "packages.lock.json", "azure-container-app.bicep", ".env.example", "README.md"],
            DeploymentTemplateTargets.KubernetesHelm => ["config.json", "packages.lock.json", "helm/Chart.yaml", "helm/values.yaml", "helm/templates/deployment.yaml", "README.md"],
            _ => RequiredFilePaths
        };

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return false;

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.All(segment => segment != "." && segment != "..");
    }
}
