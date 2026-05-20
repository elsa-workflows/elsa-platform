using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Artifacts.Tests;

public class ArtifactBuilderTests : IAsyncDisposable
{
    private readonly ArtifactTestWorkspace _workspace = new();
    private readonly DeploymentArtifactBuilder _builder = new();

    [Fact]
    public async Task BuildsFolderArtifact()
    {
        var result = await _builder.BuildFolderAsync(_workspace.FolderOptions());

        result.Succeeded.Should().BeTrue();
        result.ArtifactId.Should().StartWith("sha256:");
        File.Exists(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath)).Should().BeTrue();
        File.Exists(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath)).Should().BeTrue();
        File.Exists(Path.Combine(_workspace.OutputFolder, "manifest", "manifest.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_workspace.OutputFolder, "payload", "workflows", "order-approval.json")).Should().BeTrue();
        File.Exists(Path.Combine(_workspace.OutputFolder, "payload", "recipes", "initialize-sales.yaml")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath))
            .Should().Contain("\"kind\": \"Manifest\"")
            .And.Contain("\"kind\": \"Payload\"");
        result.Metadata!.Resources.Should().HaveCount(2);
    }

    [Fact]
    public async Task FolderArtifactIdentityIsDeterministicForSameInputs()
    {
        var first = await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var secondPath = Path.Combine(_workspace.Root, "artifact-two");
        var second = await _builder.BuildFolderAsync(_workspace.FolderOptions() with { OutputPath = secondPath });

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        first.ArtifactId.Should().Be(second.ArtifactId);
    }

    [Fact]
    public async Task MissingPayloadFailsBuild()
    {
        File.Delete(Path.Combine(_workspace.Root, "recipes", "initialize-sales.yaml"));

        var result = await _builder.BuildFolderAsync(_workspace.FolderOptions());

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.PayloadMissing);
        Directory.Exists(_workspace.OutputFolder).Should().BeFalse();
    }

    [Fact]
    public async Task DuplicatePayloadPathFailsBuild()
    {
        var manifest = _workspace.ManifestYaml.Replace("path: recipes/initialize-sales.yaml", "path: workflows/order-approval.json");
        var options = _workspace.FolderOptions() with { ManifestText = manifest };

        var result = await _builder.BuildFolderAsync(options);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.PathDuplicate);
        Directory.Exists(_workspace.OutputFolder).Should().BeFalse();
    }

    [Fact]
    public async Task InvalidManifestPathFailsBeforeOutputIsPublished()
    {
        var manifest = _workspace.ManifestYaml.Replace("path: workflows/order-approval.json", "path: ../order-approval.json");
        var options = _workspace.FolderOptions() with { ManifestText = manifest };

        var result = await _builder.BuildFolderAsync(options);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Severity == DeploymentDiagnosticSeverity.Error);
        Directory.Exists(_workspace.OutputFolder).Should().BeFalse();
    }

    [Fact]
    public async Task ExistingOutputFailsUnlessOverwriteIsEnabled()
    {
        Directory.CreateDirectory(_workspace.OutputFolder);

        var result = await _builder.BuildFolderAsync(_workspace.FolderOptions(overwrite: false));

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.BuildFailed);
        Directory.Exists(_workspace.OutputFolder).Should().BeTrue();
    }

    public async ValueTask DisposeAsync() => await _workspace.DisposeAsync();
}
