using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ValenceControl.Deployment.Artifacts.Tests;

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

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Succeeded);
        Assert.StartsWith("sha256:", result.ArtifactId);
        Assert.Equal(ArtifactLayoutConstants.LayoutVersion, result.Metadata!.LayoutVersion);
        Assert.Equal(2, result.NormalizedManifest!.Resources.Count());
        Assert.All(result.Checksums, x => Assert.True(x.Status == DeploymentArtifactChecksumStatus.Verified));
    }

    [Fact]
    public async Task DetectsChecksumMismatch()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, "payload", "workflows", "order-approval.json"), """{"changed":true}""");

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactId);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumMismatch);
    }

    [Fact]
    public async Task DetectsChecksumAlgorithmMismatch()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var checksumPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath);
        var checksums = JsonNode.Parse(await File.ReadAllTextAsync(checksumPath))!.AsObject();
        var payloadChecksum = checksums["entries"]!.AsArray().First(entry => entry!["kind"]!.GetValue<string>() == "Payload")!.AsObject();
        payloadChecksum["algorithm"] = "sha512";
        await File.WriteAllTextAsync(checksumPath, checksums.ToJsonString());

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactId);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumMismatch);
    }

    [Fact]
    public async Task DetectsChecksumInventoryAlgorithmMismatch()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var checksumPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath);
        var checksums = JsonNode.Parse(await File.ReadAllTextAsync(checksumPath))!.AsObject();
        checksums["algorithm"] = "sha512";
        await File.WriteAllTextAsync(checksumPath, checksums.ToJsonString());

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Null(result.ArtifactId);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumInvalid);
    }

    [Fact]
    public async Task MissingArtifactFolderReturnsReadFailedDiagnostic()
    {
        var result = await _reader.InspectFolderAsync(Path.Combine(_workspace.Root, "missing-artifact"));

        Assert.False(result.Succeeded);
        Assert.Single(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ReadFailed);
    }

    [Fact]
    public async Task DetectsMissingPayload()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        File.Delete(Path.Combine(_workspace.OutputFolder, "payload", "recipes", "initialize-sales.yaml"));

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumMissing);
    }

    [Fact]
    public async Task DetectsUnexpectedPayload()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, "payload", "unexpected.txt"), "unexpected");

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.PayloadUnexpected);
    }

    [Fact]
    public async Task RejectsChecksumTraversalPaths()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var outsidePath = Path.Combine(_workspace.Root, "outside.txt");
        await File.WriteAllTextAsync(outsidePath, "outside artifact root");
        var outsideDigest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(outsidePath, CancellationToken.None);
        var checksumPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath);
        var checksums = JsonNode.Parse(await File.ReadAllTextAsync(checksumPath))!.AsObject();
        var payloadChecksum = checksums["entries"]!.AsArray().First(entry => entry!["kind"]!.GetValue<string>() == "Payload")!.AsObject();
        payloadChecksum["path"] = "../outside.txt";
        payloadChecksum["digest"] = outsideDigest.Value;
        payloadChecksum["size"] = new FileInfo(outsidePath).Length;
        await File.WriteAllTextAsync(checksumPath, checksums.ToJsonString());

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.PathInvalid);
    }

    [Theory]
    [InlineData(ArtifactLayoutConstants.MetadataPath, ArtifactDiagnosticCodes.MetadataRequired)]
    [InlineData("manifest/manifest.yaml", ArtifactDiagnosticCodes.ManifestRequired)]
    public async Task DetectsMissingRequiredArtifactFiles(string relativePath, string expectedCode)
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        File.Delete(Path.Combine(_workspace.OutputFolder, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == expectedCode);
    }

    [Fact]
    public async Task DetectsUnsupportedLayoutVersion()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var metadataPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath);
        var metadata = await File.ReadAllTextAsync(metadataPath);
        await File.WriteAllTextAsync(metadataPath, metadata.Replace(ArtifactLayoutConstants.LayoutVersion, "valence-control/deployment-artifact/v9"));

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.LayoutUnsupported);
    }

    [Fact]
    public async Task DetectsArtifactIdentityMismatch()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var metadataPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath);
        var checksumPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!.AsObject();
        metadata["artifactId"] = "sha256:tampered";
        metadata["contentDigest"]!["value"] = "tampered";
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());
        var metadataDigest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(metadataPath, CancellationToken.None);
        var checksums = JsonNode.Parse(await File.ReadAllTextAsync(checksumPath))!.AsObject();
        var metadataChecksum = checksums["entries"]!.AsArray().Single(entry => entry!["path"]!.GetValue<string>() == ArtifactLayoutConstants.MetadataPath)!.AsObject();
        metadataChecksum["digest"] = metadataDigest.Value;
        metadataChecksum["size"] = new FileInfo(metadataPath).Length;
        await File.WriteAllTextAsync(checksumPath, checksums.ToJsonString());

        var result = await _reader.InspectFolderAsync(_workspace.OutputFolder);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.IdentityMismatch);
    }

    [Fact]
    public async Task ZipInspectionMatchesFolderInspection()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await _builder.BuildZipAsync(_workspace.ZipOptions());

        var folder = await _reader.InspectFolderAsync(_workspace.OutputFolder);
        var zip = await _reader.InspectZipAsync(_workspace.OutputZip);

        Assert.True(folder.Succeeded);
        Assert.True(zip.Succeeded);
        Assert.Equal(folder.ArtifactId, zip.ArtifactId);
        Assert.Equal(folder.Metadata!.Resources, zip.Metadata!.Resources);
        Assert.Equivalent(folder.Checksums.Select(x => x.Path), zip.Checksums.Select(x => x.Path));
    }

    [Fact]
    public async Task RejectsArchiveTraversalEntries()
    {
        await using (var stream = File.Create(_workspace.OutputZip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            archive.CreateEntry("../escape.txt");

        var result = await _reader.InspectZipAsync(_workspace.OutputZip);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.PathInvalid);
    }

    [Fact]
    public async Task AllowsZipDirectoryEntries()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await using (var stream = File.Create(_workspace.OutputZip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("manifest/");
            archive.CreateEntry("payload/");
            archive.CreateEntry("payload/workflows/");
            archive.CreateEntry("payload/recipes/");
            foreach (var file in Directory.EnumerateFiles(_workspace.OutputFolder, "*", SearchOption.AllDirectories))
                AddFile(archive, file, Path.GetRelativePath(_workspace.OutputFolder, file).Replace('\\', '/'));
        }

        var result = await _reader.InspectZipAsync(_workspace.OutputZip);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task NullChecksumEntriesReturnsDiagnostic()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath), """
            {
              "algorithm": "sha256",
              "entries": null
            }
            """);

        var inspect = async () => await _reader.InspectFolderAsync(_workspace.OutputFolder);

        var result = await inspect();
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumMissing);
    }

    [Fact]
    public async Task NullChecksumDocumentReturnsDiagnostic()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath), "null");

        var inspect = async () => await _reader.InspectFolderAsync(_workspace.OutputFolder);

        var result = await inspect();
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumInvalid);
    }

    [Fact]
    public async Task NullChecksumEntryFieldsReturnDiagnostic()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath), """
            {
              "algorithm": "sha256",
              "entries": [
                {
                  "path": null,
                  "kind": "Payload",
                  "algorithm": "sha256",
                  "digest": null,
                  "size": 1
                }
              ]
            }
            """);

        var inspect = async () => await _reader.InspectFolderAsync(_workspace.OutputFolder);

        var result = await inspect();
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumMissing);
    }

    [Fact]
    public async Task NullChecksumEntryElementReturnsDiagnostic()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath), """
            {
              "algorithm": "sha256",
              "entries": [
                null
              ]
            }
            """);

        var inspect = async () => await _reader.InspectFolderAsync(_workspace.OutputFolder);

        var result = await inspect();
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.ChecksumMissing);
    }

    [Fact]
    public async Task NullMetadataDigestFieldsReturnDiagnostic()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        await File.WriteAllTextAsync(Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath), """
            {
              "layoutVersion": "valence-control/deployment-artifact/v1alpha1",
              "artifactId": "sha256:invalid",
              "contentDigest": {
                "algorithm": null,
                "value": "invalid"
              },
              "createdAt": "2026-05-20T00:00:00+00:00",
              "resources": []
            }
            """);

        var inspect = async () => await _reader.InspectFolderAsync(_workspace.OutputFolder);

        var result = await inspect();
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.MetadataInvalid);
    }

    [Fact]
    public async Task MissingMetadataRequiredFieldsReturnDiagnostic()
    {
        await _builder.BuildFolderAsync(_workspace.FolderOptions());
        var metadataPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.MetadataPath);
        var checksumPath = Path.Combine(_workspace.OutputFolder, ArtifactLayoutConstants.ChecksumInventoryPath);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!.AsObject();
        metadata.Remove("manifest");
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());
        var metadataDigest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(metadataPath, CancellationToken.None);
        var checksums = JsonNode.Parse(await File.ReadAllTextAsync(checksumPath))!.AsObject();
        var metadataChecksum = checksums["entries"]!.AsArray().Single(entry => entry!["path"]!.GetValue<string>() == ArtifactLayoutConstants.MetadataPath)!.AsObject();
        metadataChecksum["digest"] = metadataDigest.Value;
        metadataChecksum["size"] = new FileInfo(metadataPath).Length;
        await File.WriteAllTextAsync(checksumPath, checksums.ToJsonString());

        var inspect = async () => await _reader.InspectFolderAsync(_workspace.OutputFolder);

        var result = await inspect();
        Assert.False(result.Succeeded);
        Assert.Null(result.Metadata);
        Assert.Contains(result.Diagnostics, x => x.Code == ArtifactDiagnosticCodes.MetadataInvalid);
    }

    public async ValueTask DisposeAsync() => await _workspace.DisposeAsync();

    private static void AddFile(ZipArchive archive, string file, string entryName)
    {
        var entry = archive.CreateEntry(entryName);
        using var entryStream = entry.Open();
        using var fileStream = File.OpenRead(file);
        fileStream.CopyTo(entryStream);
    }
}
