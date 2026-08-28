using ElsaControl.Deployment.Manifest;

namespace ElsaControl.Deployment.Artifacts.Tests;

internal sealed class ArtifactTestWorkspace : IAsyncDisposable
{
    public ArtifactTestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), $"elsa-control-artifact-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "workflows"));
        Directory.CreateDirectory(Path.Combine(Root, "recipes"));
        File.WriteAllText(Path.Combine(Root, "workflows", "order-approval.json"), """{"id":"order-approval"}""");
        File.WriteAllText(Path.Combine(Root, "recipes", "initialize-sales.yaml"), "name: initialize-sales");
    }

    public string Root { get; }

    public string OutputFolder => Path.Combine(Root, "artifact");

    public string OutputZip => Path.Combine(Root, "artifact.zip");

    public string ManifestYaml => """
        apiVersion: elsa-control/v1alpha1
        kind: EnvironmentManifest
        metadata:
          name: sales-staging
          version: 2026.05.20.1
          environment: staging
        resources:
          workflows:
            - id: order-approval
              path: workflows/order-approval.json
          recipes:
            - id: initialize-sales
              path: recipes/initialize-sales.yaml
        """;

    public DeploymentArtifactBuildOptions FolderOptions(bool overwrite = true) =>
        new(ManifestYaml, ManifestFormat.Yaml, Root, OutputFolder, overwrite, BuiltAt, builder: "tests", source: "fixture");

    public DeploymentArtifactBuildOptions ZipOptions(bool overwrite = true) =>
        new(ManifestYaml, ManifestFormat.Yaml, Root, OutputZip, overwrite, BuiltAt, builder: "tests", source: "fixture");

    public static DateTimeOffset BuiltAt { get; } = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
