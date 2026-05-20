using System.IO.Compression;
using FluentAssertions;

namespace Elsa.Platform.Deployment.Artifacts.Tests;

public class ArtifactReaderTests : IAsyncDisposable
{
    private readonly ArtifactTestWorkspace _workspace = new();
    private readonly DeploymentArtifactBuilder _builder = new();
    private readonly DeploymentArtifactReader _reader = new();

    [Fact]
    public async Task InspectsFolderArtifact()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        result.Succeeded.Should().BeTrue();
        result.ArtifactId.Should().StartWith("sha256:");
        result.Metadata!.LayoutVersion.Should().Be(ArtifactLayoutConstants.LayoutVersion);
        result.NormalizedManifest!.Resources.Should().HaveCount(2);
        result.Checksums.Should().OnlyContain(x => x.Status == DeploymentArtifactChecksumStatus.Verified);
    }

    [Fact]
    public async Task DetectsChecksumMismatch()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, "payload", "workflows", "order-approval.json"), """{"changed":true}""");

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.ChecksumMismatch);
    }

    [Fact]
    public async Task DetectsMissingPayload()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        File.Delete(Path.Combine(_workspace.OutputFolder, "payload", "recipes", "initialize-sales.yaml"));

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.ChecksumMissing);
    }

    [Fact]
    public async Task DetectsUnexpectedPayload()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, "payload", "unexpected.txt"), "unexpected");

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.PayloadUnexpected);
    }

    [Theory]
    [InlineData(ArtifactLayoutConstants.MetadataPath, ArtifactDiagnosticCodes.MetadataRequired)]
    [InlineData("manifest/manifest.yaml", ArtifactDiagnosticCodes.ManifestRequired)]
    public async Task DetectsMissingRequiredArtifactFiles(string relativePath, string expectedCode)
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        File.Delete(Path.Combine(_workspace.OutputFolder, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == expectedCode);
    }

    [Fact]
    public async Task DetectsUnsupportedLayoutVersion()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var metadataPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath);
        var metadata = await File.ReadAllTextAsync(metadataPath);
        await File.WriteAllTextAsync(metadataPath, metadata.Replace(ArtifactLayoutConstants.LayoutVersion, "platform.elsa.io/deployment-artifact/v9"));

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.LayoutUnsupported);
    }

    [Fact]
    public async Task ZipInspectionMatchesFolderInspection()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await _builder.BuildZipAsync(_workspace.ZipOptions());

        var folder = await _reader.InspectFolderAsync(_workspace.OutputFolder);
        var zip = await _reader.InspectZipAsync(_workspace.OutputZip);

        folder.Succeeded.Should().BeTrue();
        zip.Succeeded.Should().BeTrue();
        zip.ArtifactId.Should().Be(folder.ArtifactId);
        zip.Metadata!.Resources.Should().Equal(folder.Metadata!.Resources);
        zip.Checksums.Select(x => x.Path).Should().BeEquivalentTo(folder.Checksums.Select(x => x.Path));
    }

    [Fact]
    public async Task RejectsArchiveTraversalEntries()
    {
        await using (var stream = File.Create(_workspace.OutputZip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            archive.CreateEntry("../escape.txt");

        var result = await _reader.InspectZipAsync(_workspace.OutputZip);

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(x => x.Code == ArtifactDiagnosticCodes.PathInvalid);
    }

    public async ValueTask DisposeAsync() => await _workspace.DisposeAsync();
}
