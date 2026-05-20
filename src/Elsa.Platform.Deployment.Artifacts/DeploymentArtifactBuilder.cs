using System.IO.Compression;
using System.Text.Json;
using Elsa.Platform.Deployment.Abstractions.Diagnostics;
using Elsa.Platform.Deployment.Abstractions.Resources;
using Elsa.Platform.Deployment.Manifest;

namespace Elsa.Platform.Deployment.Artifacts;

public sealed class DeploymentArtifactBuilder(
    IManifestReader? manifestReader = null,
    IManifestNormalizer? manifestNormalizer = null) : IDeploymentArtifactBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IManifestReader _manifestReader = manifestReader ?? new ManifestReader();
    private readonly IManifestNormalizer _manifestNormalizer = manifestNormalizer ?? new ManifestNormalizer();

    public async ValueTask<DeploymentArtifactBuildResult> BuildFolderAsync(
        DeploymentArtifactBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<DeploymentDiagnostic>();
        var build = await PrepareBuildAsync(options, diagnostics, cancellationToken);
        if (build is null)
            return Failed(options.OutputPath, diagnostics);

        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputParent = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputParent);
        var outputName = Path.GetFileName(outputPath);
        var tempPath = Path.Combine(outputParent, $".{outputName}.tmp-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(outputParent, $".{outputName}.bak-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempPath);
            await WriteFolderContentsAsync(tempPath, options, build, cancellationToken);

            if (File.Exists(outputPath))
            {
                diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Output path '{outputPath}' exists and is not a folder."));
                return Failed(outputPath, diagnostics);
            }

            if (Directory.Exists(outputPath))
            {
                if (!options.Overwrite)
                {
                    diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Output folder '{outputPath}' already exists."));
                    return Failed(outputPath, diagnostics);
                }

                Directory.Move(outputPath, backupPath);
            }

            try
            {
                Directory.Move(tempPath, outputPath);
            }
            catch
            {
                RestoreDirectoryBackup(backupPath, outputPath);
                throw;
            }

            DeleteDirectoryBackup(backupPath);
            return new DeploymentArtifactBuildResult(true, build.Metadata.ArtifactId, outputPath, build.Metadata, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, ex.Message));
            return Failed(outputPath, diagnostics);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    public async ValueTask<DeploymentArtifactBuildResult> BuildZipAsync(
        DeploymentArtifactBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputParent = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputParent);
        var outputName = Path.GetFileName(outputPath);
        var folderPath = Path.Combine(outputParent, $".{outputName}.folder-{Guid.NewGuid():N}");
        var tempZipPath = Path.Combine(outputParent, $".{outputName}.tmp-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(outputParent, $".{outputName}.bak-{Guid.NewGuid():N}");
        var folderOptions = options with { OutputPath = folderPath, Overwrite = true };
        var result = await BuildFolderAsync(folderOptions, cancellationToken);
        if (!result.Succeeded)
            return result with { OutputPath = outputPath };

        try
        {
            if (Directory.Exists(outputPath))
                return Failed(outputPath, [Error(ArtifactDiagnosticCodes.BuildFailed, $"Output path '{outputPath}' exists and is not a file.")]);

            ZipFile.CreateFromDirectory(folderPath, tempZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            if (File.Exists(outputPath))
            {
                if (!options.Overwrite)
                    return Failed(outputPath, [Error(ArtifactDiagnosticCodes.BuildFailed, $"Output ZIP '{outputPath}' already exists.")]);
                File.Move(outputPath, backupPath);
            }

            try
            {
                File.Move(tempZipPath, outputPath);
            }
            catch
            {
                RestoreFileBackup(backupPath, outputPath);
                throw;
            }

            DeleteFileBackup(backupPath);
            return result with { OutputPath = outputPath };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(outputPath, [Error(ArtifactDiagnosticCodes.BuildFailed, ex.Message)]);
        }
        finally
        {
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
        }
    }

    private async ValueTask<PreparedBuild?> PrepareBuildAsync(
        DeploymentArtifactBuildOptions options,
        List<DeploymentDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(options.WorkspaceRoot))
        {
            diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Workspace root '{options.WorkspaceRoot}' does not exist."));
            return null;
        }

        var parse = _manifestReader.Read(options.ManifestText, options.ManifestFormat);
        diagnostics.AddRange(parse.Diagnostics);
        if (parse.Manifest is null)
            return null;

        var normalized = _manifestNormalizer.Normalize(parse.Manifest);
        diagnostics.AddRange(normalized.Diagnostics);
        if (diagnostics.Any(x => x.Severity == DeploymentDiagnosticSeverity.Error))
            return null;

        var payloads = CollectPayloads(options.WorkspaceRoot, normalized.Resources, diagnostics);
        if (diagnostics.Any(x => x.Severity == DeploymentDiagnosticSeverity.Error))
            return null;

        var manifestPath = $"{ArtifactLayoutConstants.ManifestDirectory}/manifest.{ManifestExtension(options.ManifestFormat)}";
        var manifestEntry = new DeploymentArtifactEntry(manifestPath, DeploymentArtifactEntryKind.Manifest, System.Text.Encoding.UTF8.GetByteCount(options.ManifestText));
        var payloadEntries = payloads
            .Select(payload => new DeploymentArtifactEntry(payload.ArtifactPath, DeploymentArtifactEntryKind.Payload, new FileInfo(payload.SourcePath).Length, payload.SourceRelativePath))
            .ToArray();
        var initialEntries = new[] { manifestEntry }.Concat(payloadEntries).ToArray();
        var initialChecksums = await ComputeChecksumsAsync(initialEntries, payloads, options, cancellationToken);
        var contentDigest = DeploymentArtifactChecksumService.ComputeContentDigest(initialChecksums);
        var artifactId = contentDigest.ToString();
        var metadata = new DeploymentArtifactMetadata(
            ArtifactLayoutConstants.LayoutVersion,
            artifactId,
            options.BuiltAt ?? DateTimeOffset.UtcNow,
            new DeploymentArtifactManifestMetadata(
                normalized.Manifest.Metadata.Name,
                normalized.Manifest.Metadata.Version,
                normalized.Manifest.Metadata.Environment,
                normalized.Manifest.Metadata.Labels,
                normalized.Manifest.Metadata.Annotations),
            normalized.Resources.Select(DeploymentArtifactResourceSummary.FromResource).ToArray(),
            contentDigest,
            options.Builder,
            options.Source);

        return new PreparedBuild(normalized, payloads, metadata, initialChecksums);
    }

    private static async ValueTask WriteFolderContentsAsync(
        string root,
        DeploymentArtifactBuildOptions options,
        PreparedBuild build,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(root, ArtifactLayoutConstants.ManifestDirectory, $"manifest.{ManifestExtension(options.ManifestFormat)}");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, options.ManifestText, cancellationToken);

        foreach (var payload in build.Payloads)
        {
            var destination = Path.Combine(root, payload.ArtifactPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(payload.SourcePath, destination, overwrite: false);
        }

        var metadataPath = Path.Combine(root, ArtifactLayoutConstants.MetadataPath);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(build.Metadata, JsonOptions), cancellationToken);
        var metadataDigest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(metadataPath, cancellationToken);
        var metadataEntry = new DeploymentArtifactChecksumEntry(
            ArtifactLayoutConstants.MetadataPath,
            DeploymentArtifactEntryKind.Metadata,
            metadataDigest.Algorithm,
            metadataDigest.Value,
            new FileInfo(metadataPath).Length);
        var inventory = new DeploymentArtifactChecksumInventory(
            ArtifactLayoutConstants.ChecksumAlgorithm,
            build.Checksums.Concat([metadataEntry]).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray());
        var checksumPath = Path.Combine(root, ArtifactLayoutConstants.ChecksumInventoryPath);
        await File.WriteAllTextAsync(checksumPath, JsonSerializer.Serialize(inventory, JsonOptions), cancellationToken);
    }

    private static IReadOnlyCollection<PayloadFile> CollectPayloads(
        string workspaceRoot,
        IEnumerable<DeploymentResource> resources,
        List<DeploymentDiagnostic> diagnostics)
    {
        var payloads = new List<PayloadFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);

        foreach (var resource in resources)
        {
            if (!resource.Metadata.TryGetValue("path", out var resourcePath))
                continue;

            var normalized = DeploymentArtifactPathValidator.NormalizeRelativePath(resourcePath);
            if (normalized is null)
            {
                diagnostics.Add(DeploymentArtifactPathValidator.Invalid(resourcePath, $"Resource '{resource.Id}' has an invalid payload path."));
                continue;
            }

            var sourcePath = Path.GetFullPath(Path.Combine(fullWorkspaceRoot, normalized));
            if (!sourcePath.StartsWith(fullWorkspaceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) && sourcePath != fullWorkspaceRoot)
            {
                diagnostics.Add(DeploymentArtifactPathValidator.Invalid(resourcePath, $"Resource '{resource.Id}' escapes the workspace root."));
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                diagnostics.Add(new DeploymentDiagnostic(
                    ArtifactDiagnosticCodes.PayloadMissing,
                    DeploymentDiagnosticSeverity.Error,
                    $"Payload file '{normalized}' was not found.",
                    resource.Id,
                    new Dictionary<string, string> { ["path"] = normalized }));
                continue;
            }

            var artifactPath = $"{ArtifactLayoutConstants.PayloadDirectory}/{normalized}";
            if (!seen.Add(artifactPath))
            {
                diagnostics.Add(new DeploymentDiagnostic(
                    ArtifactDiagnosticCodes.PathDuplicate,
                    DeploymentDiagnosticSeverity.Error,
                    $"Payload path '{artifactPath}' is declared more than once.",
                    resource.Id,
                    new Dictionary<string, string> { ["path"] = artifactPath }));
                continue;
            }

            payloads.Add(new PayloadFile(normalized, artifactPath, sourcePath));
        }

        return payloads;
    }

    private static async ValueTask<IReadOnlyCollection<DeploymentArtifactChecksumEntry>> ComputeChecksumsAsync(
        IEnumerable<DeploymentArtifactEntry> entries,
        IEnumerable<PayloadFile> payloads,
        DeploymentArtifactBuildOptions options,
        CancellationToken cancellationToken)
    {
        var result = new List<DeploymentArtifactChecksumEntry>();
        foreach (var entry in entries)
        {
            var digest = entry.Kind == DeploymentArtifactEntryKind.Manifest
                ? ComputeTextDigest(options.ManifestText)
                : await DeploymentArtifactChecksumService.ComputeFileDigestAsync(payloads.Single(x => x.ArtifactPath == entry.Path).SourcePath, cancellationToken);
            result.Add(new DeploymentArtifactChecksumEntry(entry.Path, entry.Kind, digest.Algorithm, digest.Value, entry.Size));
        }

        return result;
    }

    private static Elsa.Platform.Deployment.Abstractions.Artifacts.ArtifactDigest ComputeTextDigest(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return new(ArtifactLayoutConstants.ChecksumAlgorithm, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static string ManifestExtension(ManifestFormat format) => format == ManifestFormat.Json ? "json" : "yaml";

    private static DeploymentArtifactBuildResult Failed(
        string outputPath,
        IReadOnlyCollection<DeploymentDiagnostic> diagnostics) =>
        new(false, null, outputPath, null, diagnostics);

    private static DeploymentDiagnostic Error(string code, string message) =>
        new(code, DeploymentDiagnosticSeverity.Error, message);

    private static void RestoreDirectoryBackup(string backupPath, string outputPath)
    {
        if (Directory.Exists(backupPath) && !Directory.Exists(outputPath))
            Directory.Move(backupPath, outputPath);
    }

    private static void DeleteDirectoryBackup(string backupPath)
    {
        if (Directory.Exists(backupPath))
            Directory.Delete(backupPath, recursive: true);
    }

    private static void RestoreFileBackup(string backupPath, string outputPath)
    {
        if (File.Exists(backupPath) && !File.Exists(outputPath))
            File.Move(backupPath, outputPath);
    }

    private static void DeleteFileBackup(string backupPath)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }

    private sealed record PreparedBuild(
        NormalizedManifest Normalized,
        IReadOnlyCollection<PayloadFile> Payloads,
        DeploymentArtifactMetadata Metadata,
        IReadOnlyCollection<DeploymentArtifactChecksumEntry> Checksums);

    private sealed record PayloadFile(string SourceRelativePath, string ArtifactPath, string SourcePath);
}
