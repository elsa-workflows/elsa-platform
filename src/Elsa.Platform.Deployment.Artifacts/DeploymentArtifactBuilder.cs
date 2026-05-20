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
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputParent = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var outputName = Path.GetFileName(outputPath);
        var tempPath = Path.Combine(outputParent, $".{outputName}.tmp-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(outputParent, $".{outputName}.bak-{Guid.NewGuid():N}");
        var committed = false;
        try
        {
            Directory.CreateDirectory(outputParent);
            var build = await PrepareBuildAsync(options, diagnostics, cancellationToken);
            if (build is null)
                return Failed(outputPath, diagnostics);

            Directory.CreateDirectory(tempPath);
            var metadata = await WriteFolderContentsAsync(tempPath, options, build, cancellationToken);

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
                committed = true;
            }
            catch
            {
                TryRecoverDirectoryBackup(backupPath, outputPath, diagnostics);
                throw;
            }

            TryDeleteDirectoryBackup(backupPath, diagnostics);
            return new DeploymentArtifactBuildResult(true, metadata.ArtifactId, outputPath, metadata, diagnostics);
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
            if (!committed)
                TryRecoverDirectoryBackup(backupPath, outputPath, diagnostics);
        }
    }

    public async ValueTask<DeploymentArtifactBuildResult> BuildZipAsync(
        DeploymentArtifactBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<DeploymentDiagnostic>();
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputParent = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        var outputName = Path.GetFileName(outputPath);
        var folderPath = Path.Combine(outputParent, $".{outputName}.folder-{Guid.NewGuid():N}");
        var tempZipPath = Path.Combine(outputParent, $".{outputName}.tmp-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(outputParent, $".{outputName}.bak-{Guid.NewGuid():N}");
        var committed = false;
        var folderOptions = options with { OutputPath = folderPath, Overwrite = true };
        var result = await BuildFolderAsync(folderOptions, cancellationToken);
        if (!result.Succeeded)
            return result with { OutputPath = outputPath };
        diagnostics.AddRange(result.Diagnostics);

        try
        {
            Directory.CreateDirectory(outputParent);
            if (Directory.Exists(outputPath))
            {
                diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Output path '{outputPath}' exists and is not a file."));
                return Failed(outputPath, diagnostics);
            }

            if (File.Exists(outputPath))
            {
                if (!options.Overwrite)
                {
                    diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Output ZIP '{outputPath}' already exists."));
                    return Failed(outputPath, diagnostics);
                }
                File.Move(outputPath, backupPath);
            }

            ZipFile.CreateFromDirectory(folderPath, tempZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            try
            {
                File.Move(tempZipPath, outputPath);
                committed = true;
            }
            catch
            {
                TryRecoverFileBackup(backupPath, outputPath, diagnostics);
                throw;
            }

            TryDeleteFileBackup(backupPath, diagnostics);
            return result with { OutputPath = outputPath, Diagnostics = diagnostics };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, ex.Message));
            return Failed(outputPath, diagnostics);
        }
        finally
        {
            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, recursive: true);
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
            if (!committed)
                TryRecoverFileBackup(backupPath, outputPath, diagnostics);
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

        var payloadEntries = payloads
            .Select(payload => new DeploymentArtifactEntry(payload.ArtifactPath, DeploymentArtifactEntryKind.Payload, new FileInfo(payload.SourcePath).Length, payload.SourceRelativePath))
            .ToArray();
        var payloadChecksums = await ComputePayloadChecksumsAsync(payloadEntries, payloads, cancellationToken);
        return new PreparedBuild(normalized, payloads, payloadChecksums);
    }

    private static async ValueTask<DeploymentArtifactMetadata> WriteFolderContentsAsync(
        string root,
        DeploymentArtifactBuildOptions options,
        PreparedBuild build,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(root, ArtifactLayoutConstants.ManifestDirectory, $"manifest.{ManifestExtension(options.ManifestFormat)}");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, options.ManifestText, cancellationToken);
        var manifestDigest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(manifestPath, cancellationToken);
        var manifestEntry = new DeploymentArtifactChecksumEntry(
            Path.GetRelativePath(root, manifestPath).Replace('\\', '/'),
            DeploymentArtifactEntryKind.Manifest,
            manifestDigest.Algorithm,
            manifestDigest.Value,
            new FileInfo(manifestPath).Length);

        foreach (var payload in build.Payloads)
        {
            var destination = Path.Combine(root, payload.ArtifactPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var sourceStream = File.OpenRead(payload.SourcePath);
            await using var destinationStream = File.Create(destination);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        var contentChecksums = new[] { manifestEntry }.Concat(build.PayloadChecksums).ToArray();
        var contentDigest = DeploymentArtifactChecksumService.ComputeContentDigest(contentChecksums);
        var metadata = new DeploymentArtifactMetadata(
            ArtifactLayoutConstants.LayoutVersion,
            contentDigest.ToString(),
            options.BuiltAt ?? DateTimeOffset.UtcNow,
            new DeploymentArtifactManifestMetadata(
                build.Normalized.Manifest.Metadata.Name,
                build.Normalized.Manifest.Metadata.Version,
                build.Normalized.Manifest.Metadata.Environment,
                build.Normalized.Manifest.Metadata.Labels,
                build.Normalized.Manifest.Metadata.Annotations),
            build.Normalized.Resources.Select(DeploymentArtifactResourceSummary.FromResource).ToArray(),
            contentDigest,
            options.Builder,
            options.Source);
        var metadataPath = Path.Combine(root, ArtifactLayoutConstants.MetadataPath);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions), cancellationToken);
        var metadataDigest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(metadataPath, cancellationToken);
        var metadataEntry = new DeploymentArtifactChecksumEntry(
            ArtifactLayoutConstants.MetadataPath,
            DeploymentArtifactEntryKind.Metadata,
            metadataDigest.Algorithm,
            metadataDigest.Value,
            new FileInfo(metadataPath).Length);
        var inventory = new DeploymentArtifactChecksumInventory(
            ArtifactLayoutConstants.ChecksumAlgorithm,
            contentChecksums.Concat([metadataEntry]).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray());
        var checksumPath = Path.Combine(root, ArtifactLayoutConstants.ChecksumInventoryPath);
        await File.WriteAllTextAsync(checksumPath, JsonSerializer.Serialize(inventory, JsonOptions), cancellationToken);
        return metadata;
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

    private static async ValueTask<IReadOnlyCollection<DeploymentArtifactChecksumEntry>> ComputePayloadChecksumsAsync(
        IEnumerable<DeploymentArtifactEntry> entries,
        IEnumerable<PayloadFile> payloads,
        CancellationToken cancellationToken)
    {
        var payloadsByArtifactPath = payloads.ToDictionary(payload => payload.ArtifactPath, StringComparer.Ordinal);
        var result = new List<DeploymentArtifactChecksumEntry>();
        foreach (var entry in entries)
        {
            var digest = await DeploymentArtifactChecksumService.ComputeFileDigestAsync(payloadsByArtifactPath[entry.Path].SourcePath, cancellationToken);
            result.Add(new DeploymentArtifactChecksumEntry(entry.Path, entry.Kind, digest.Algorithm, digest.Value, entry.Size));
        }

        return result;
    }

    private static string ManifestExtension(ManifestFormat format) => format == ManifestFormat.Json ? "json" : "yaml";

    private static DeploymentArtifactBuildResult Failed(
        string outputPath,
        IReadOnlyCollection<DeploymentDiagnostic> diagnostics) =>
        new(false, null, outputPath, null, diagnostics);

    private static DeploymentDiagnostic Error(string code, string message) =>
        new(code, DeploymentDiagnosticSeverity.Error, message);

    private static DeploymentDiagnostic Warning(string code, string message) =>
        new(code, DeploymentDiagnosticSeverity.Warning, message);

    private static void RecoverDirectoryBackup(string backupPath, string outputPath)
    {
        if (!Directory.Exists(backupPath))
            return;

        if (Directory.Exists(outputPath))
            Directory.Delete(outputPath, recursive: true);
        Directory.Move(backupPath, outputPath);
    }

    private static void TryRecoverDirectoryBackup(
        string backupPath,
        string outputPath,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        try
        {
            RecoverDirectoryBackup(backupPath, outputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Failed to restore output backup '{backupPath}': {ex.Message}"));
        }
    }

    private static void DeleteDirectoryBackup(string backupPath)
    {
        if (Directory.Exists(backupPath))
            Directory.Delete(backupPath, recursive: true);
    }

    private static void TryDeleteDirectoryBackup(
        string backupPath,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        try
        {
            DeleteDirectoryBackup(backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Warning(ArtifactDiagnosticCodes.BuildFailed, $"Failed to delete output backup '{backupPath}': {ex.Message}"));
        }
    }

    private static void RecoverFileBackup(string backupPath, string outputPath)
    {
        if (!File.Exists(backupPath))
            return;

        if (File.Exists(outputPath))
            File.Delete(outputPath);
        File.Move(backupPath, outputPath);
    }

    private static void TryRecoverFileBackup(
        string backupPath,
        string outputPath,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        try
        {
            RecoverFileBackup(backupPath, outputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error(ArtifactDiagnosticCodes.BuildFailed, $"Failed to restore output backup '{backupPath}': {ex.Message}"));
        }
    }

    private static void DeleteFileBackup(string backupPath)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }

    private static void TryDeleteFileBackup(
        string backupPath,
        ICollection<DeploymentDiagnostic> diagnostics)
    {
        try
        {
            DeleteFileBackup(backupPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Warning(ArtifactDiagnosticCodes.BuildFailed, $"Failed to delete output backup '{backupPath}': {ex.Message}"));
        }
    }

    private sealed record PreparedBuild(
        NormalizedManifest Normalized,
        IReadOnlyCollection<PayloadFile> Payloads,
        IReadOnlyCollection<DeploymentArtifactChecksumEntry> PayloadChecksums);

    private sealed record PayloadFile(string SourceRelativePath, string ArtifactPath, string SourcePath);
}
