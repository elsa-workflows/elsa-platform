using System.IO.Compression;
using System.Text.Json;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Manifest;

namespace Elsa.Platform.Deployment.Artifacts;

public sealed class DeploymentArtifactReader(
    IManifestReader? manifestReader = null,
    IManifestNormalizer? manifestNormalizer = null) : IDeploymentArtifactReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IManifestReader _manifestReader = manifestReader ?? new ManifestReader();
    private readonly IManifestNormalizer _manifestNormalizer = manifestNormalizer ?? new ManifestNormalizer();

    public async ValueTask<DeploymentArtifactInspectionResult> InspectFolderAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
            return Failed([Error(ArtifactDiagnosticCodes.MetadataRequired, $"Artifact folder '{path}' does not exist.")]);

        return await InspectAsync(new FolderArtifactStore(path), cancellationToken);
    }

    public async ValueTask<DeploymentArtifactInspectionResult> InspectZipAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return Failed([Error(ArtifactDiagnosticCodes.ArchiveInvalid, $"Artifact ZIP '{path}' does not exist.")]);

        var tempPath = Path.Combine(Path.GetTempPath(), $"elsa-artifact-read-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempPath);
            using (var archive = ZipFile.OpenRead(path))
            {
                foreach (var entry in archive.Entries)
                {
                    var normalized = DeploymentArtifactPathValidator.NormalizeRelativePath(entry.FullName);
                    if (normalized is null)
                        return Failed([DeploymentArtifactPathValidator.Invalid(entry.FullName, "Artifact ZIP contains an invalid entry path.")]);

                    var destination = Path.GetFullPath(Path.Combine(tempPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(tempPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) && destination != tempPath)
                        return Failed([DeploymentArtifactPathValidator.Invalid(entry.FullName, "Artifact ZIP entry escapes the extraction root.")]);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (!string.IsNullOrEmpty(entry.Name))
                        entry.ExtractToFile(destination, overwrite: false);
                }
            }

            return await InspectFolderAsync(tempPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Failed([Error(ArtifactDiagnosticCodes.ArchiveInvalid, ex.Message)]);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private async ValueTask<DeploymentArtifactInspectionResult> InspectAsync(
        FolderArtifactStore store,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<DeploymentDiagnostic>();
        var metadata = await ReadJsonAsync<DeploymentArtifactMetadata>(
            store,
            ArtifactLayoutConstants.MetadataPath,
            ArtifactDiagnosticCodes.MetadataRequired,
            diagnostics,
            cancellationToken);
        var checksums = await ReadJsonAsync<DeploymentArtifactChecksumInventory>(
            store,
            ArtifactLayoutConstants.ChecksumInventoryPath,
            ArtifactDiagnosticCodes.ChecksumMissing,
            diagnostics,
            cancellationToken);

        if (metadata is not null && metadata.LayoutVersion != ArtifactLayoutConstants.LayoutVersion)
            diagnostics.Add(Error(ArtifactDiagnosticCodes.LayoutUnsupported, $"Artifact layout '{metadata.LayoutVersion}' is not supported."));

        var manifestPath = FindManifestPath(store);
        EnvironmentManifest? manifest = null;
        NormalizedManifest? normalized = null;
        if (manifestPath is null)
        {
            diagnostics.Add(Error(ArtifactDiagnosticCodes.ManifestRequired, "Artifact manifest snapshot is required."));
        }
        else
        {
            var manifestText = await store.ReadTextAsync(manifestPath, cancellationToken);
            var manifestFormat = manifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ManifestFormat.Json : ManifestFormat.Yaml;
            var parse = _manifestReader.Read(manifestText, manifestFormat);
            diagnostics.AddRange(parse.Diagnostics);
            manifest = parse.Manifest;
            if (manifest is not null)
            {
                normalized = _manifestNormalizer.Normalize(manifest);
                diagnostics.AddRange(normalized.Diagnostics);
            }
        }

        var entries = ListEntries(store).ToArray();
        var verification = new List<DeploymentArtifactChecksumVerification>();
        if (checksums is not null)
            await VerifyChecksumsAsync(store, checksums, entries, verification, diagnostics, cancellationToken);

        var succeeded = diagnostics.All(x => x.Severity < DeploymentDiagnosticSeverity.Error);
        return new DeploymentArtifactInspectionResult(
            succeeded,
            succeeded ? metadata?.ArtifactId : metadata?.ArtifactId,
            metadata,
            manifest,
            normalized,
            entries,
            verification,
            diagnostics);
    }

    private static async ValueTask<T?> ReadJsonAsync<T>(
        FolderArtifactStore store,
        string path,
        string missingCode,
        ICollection<DeploymentDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!store.Exists(path))
        {
            diagnostics.Add(Error(missingCode, $"Artifact file '{path}' is required."));
            return default;
        }

        try
        {
            await using var stream = store.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(Error(missingCode, ex.Message));
            return default;
        }
    }

    private static string? FindManifestPath(FolderArtifactStore store) =>
        store.EnumerateFiles(ArtifactLayoutConstants.ManifestDirectory)
            .Select(store.GetRelativePath)
            .Where(path => path is $"{ArtifactLayoutConstants.ManifestDirectory}/manifest.yaml" or $"{ArtifactLayoutConstants.ManifestDirectory}/manifest.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

    private static IEnumerable<DeploymentArtifactEntry> ListEntries(FolderArtifactStore store)
    {
        foreach (var file in store.EnumerateFiles())
        {
            var relativePath = store.GetRelativePath(file);
            var kind = relativePath switch
            {
                ArtifactLayoutConstants.MetadataPath => DeploymentArtifactEntryKind.Metadata,
                ArtifactLayoutConstants.ChecksumInventoryPath => DeploymentArtifactEntryKind.ChecksumInventory,
                _ when relativePath.StartsWith($"{ArtifactLayoutConstants.ManifestDirectory}/", StringComparison.Ordinal) => DeploymentArtifactEntryKind.Manifest,
                _ => DeploymentArtifactEntryKind.Payload
            };
            yield return new DeploymentArtifactEntry(relativePath, kind, new FileInfo(file).Length);
        }
    }

    private static async ValueTask VerifyChecksumsAsync(
        FolderArtifactStore store,
        DeploymentArtifactChecksumInventory inventory,
        IReadOnlyCollection<DeploymentArtifactEntry> entries,
        ICollection<DeploymentArtifactChecksumVerification> verification,
        ICollection<DeploymentDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var entryPaths = entries.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var checksum in inventory.Entries)
        {
            if (!store.Exists(checksum.Path))
            {
                verification.Add(new DeploymentArtifactChecksumVerification(checksum.Path, checksum.Kind, DeploymentArtifactChecksumStatus.Missing, checksum.Digest));
                diagnostics.Add(Error(ArtifactDiagnosticCodes.ChecksumMissing, $"Artifact entry '{checksum.Path}' is missing."));
                continue;
            }

            var digest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(store.FullPath(checksum.Path), cancellationToken);
            if (!string.Equals(digest.Value, checksum.Digest, StringComparison.OrdinalIgnoreCase))
            {
                verification.Add(new DeploymentArtifactChecksumVerification(checksum.Path, checksum.Kind, DeploymentArtifactChecksumStatus.Mismatched, checksum.Digest, digest.Value));
                diagnostics.Add(Error(ArtifactDiagnosticCodes.ChecksumMismatch, $"Artifact entry '{checksum.Path}' checksum does not match."));
                continue;
            }

            verification.Add(new DeploymentArtifactChecksumVerification(checksum.Path, checksum.Kind, DeploymentArtifactChecksumStatus.Verified, checksum.Digest, digest.Value));
        }

        foreach (var unexpected in entryPaths.Except(inventory.Entries.Select(x => x.Path), StringComparer.Ordinal))
        {
            if (unexpected == ArtifactLayoutConstants.ChecksumInventoryPath)
                continue;
            var kind = entries.First(x => x.Path == unexpected).Kind;
            verification.Add(new DeploymentArtifactChecksumVerification(unexpected, kind, DeploymentArtifactChecksumStatus.Unexpected));
            diagnostics.Add(Error(ArtifactDiagnosticCodes.PayloadUnexpected, $"Artifact entry '{unexpected}' is not listed in the checksum inventory."));
        }
    }

    private static DeploymentArtifactInspectionResult Failed(IReadOnlyCollection<DeploymentDiagnostic> diagnostics) =>
        new(false, null, null, null, null, [], [], diagnostics);

    private static DeploymentDiagnostic Error(string code, string message) =>
        new(code, DeploymentDiagnosticSeverity.Error, message);

    private sealed class FolderArtifactStore(string root)
    {
        private readonly string _root = Path.GetFullPath(root);

        public bool Exists(string path) => File.Exists(FullPath(path));

        public string FullPath(string path) => Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));

        public Stream OpenRead(string path) => File.OpenRead(FullPath(path));

        public async ValueTask<string> ReadTextAsync(string path, CancellationToken cancellationToken) =>
            await File.ReadAllTextAsync(FullPath(path), cancellationToken);

        public IEnumerable<string> EnumerateFiles(string? relativeDirectory = null)
        {
            var directory = relativeDirectory is null ? _root : FullPath(relativeDirectory);
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories) : [];
        }

        public string GetRelativePath(string fullPath) =>
            Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
    }
}
