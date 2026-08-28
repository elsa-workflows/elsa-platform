using ElsaControl.Deployment.Abstractions.Diagnostics;

namespace ElsaControl.Deployment.Artifacts.Tests;

public class ArtifactBuilderTests : IAsyncDisposable
{
    private readonly ArtifactTestWorkspace _workspace = new();
    private readonly DeploymentArtifactBuilder _builder = new();

    [Fact]
    public async Task BuildsFolderArtifact()
    {
        var result = await _builder.BuildFolderAsync(_workspace.FolderOptions());

        Assert.True(result.Succeeded);
        Assert.StartsWith("sha256:", result.ArtifactId);
        Assert.True(File.Exists(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath)));
        Assert.True(File.Exists(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath)));
        Assert.True(File.Exists(Path.Combine(_workspace.OutputFolder, "manifest", "manifest.yaml")));
        Assert.True(File.Exists(Path.Combine(_workspace.OutputFolder, "payload", "workflows", "order-approval.json")));
        Assert.True(File.Exists(Path.Combine(_workspace.OutputFolder, "payload", "recipes", "initialize-sales.yaml")));
        var checksumInventory = File.ReadAllText(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath));
        Assert.Contains("\"kind\": \"Manifest\"", checksumInventory);
        Assert.Contains("\"kind\": \"Payload\"", checksumInventory);
        Assert.Equal(2, result.Metadata!.Resources.Count());
    }

    [Fact]
    public async Task FolderArtifactIdentityIsDeterministicForSameInputs()
    {
        var first = await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var secondPath = Path.Combine(_workspace.Root, "artifact-two");
        var second = await _builder.BuildFolderAsync(_workspace.FolderOptions() with { OutputPath = secondPath });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(second.ArtifactId, first.ArtifactId);
    }

    [Fact]
    public async Task MissingPayloadFailsBuild()
    {
        File.Delete(Path.Combine(_workspace.Root, "recipes", "initialize-sales.yaml"));

        var result = await _builder.BuildFolderAsync(_workspace.FolderOptions());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.PayloadMissing);
        Assert.False(Directory.Exists(_workspace.OutputFolder));
    }

    [Fact]
    public async Task DuplicatePayloadPathFailsBuild()
    {
        var manifest = _workspace.ManifestYaml.Replace("path: recipes/initialize-sales.yaml", "path: workflows/order-approval.json");
        var options = _workspace.FolderOptions() with { ManifestText = manifest };

        var result = await _builder.BuildFolderAsync(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.PathDuplicate);
        Assert.False(Directory.Exists(_workspace.OutputFolder));
    }

    [Fact]
    public async Task InvalidManifestPathFailsBeforeOutputIsPublished()
    {
        var manifest = _workspace.ManifestYaml.Replace("path: workflows/order-approval.json", "path: ../order-approval.json");
        var options = _workspace.FolderOptions() with { ManifestText = manifest };

        var result = await _builder.BuildFolderAsync(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Severity == DeploymentDiagnosticSeverity.Error);
        Assert.False(Directory.Exists(_workspace.OutputFolder));
    }

    [Fact]
    public async Task ExistingOutputFailsUnlessOverwriteIsEnabled()
    {
        Directory.CreateDirectory(_workspace.OutputFolder);

        var result = await _builder.BuildFolderAsync(_workspace.FolderOptions(overwrite: false));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.BuildFailed);
        Assert.True(Directory.Exists(_workspace.OutputFolder));
    }

    public async ValueTask DisposeAsync() => await _workspace.DisposeAsync();
}
