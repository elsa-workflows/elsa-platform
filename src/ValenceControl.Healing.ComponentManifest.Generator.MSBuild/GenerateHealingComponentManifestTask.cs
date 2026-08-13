using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ValenceControl.Healing.ComponentManifest;
using Microsoft.Build.Framework;

namespace ValenceControl.Healing.ComponentManifest.Generator.MSBuild;

public sealed class GenerateHealingComponentManifestTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string ProjectAssetsFile { get; set; } = "";

    [Required]
    public string ProjectDirectory { get; set; } = "";

    [Required]
    public string ApplicationAssemblyPath { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    [Required]
    public string ApplicationName { get; set; } = "";

    [Required]
    public string ApplicationVersion { get; set; } = "";

    [Required]
    public string TargetFramework { get; set; } = "";

    public string? RuntimeIdentifier { get; set; }
    public string? SourceRevision { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? BuildId { get; set; }
    public string? CreatedAt { get; set; }
    public bool RequireSourceRevision { get; set; } = true;

    public override bool Execute()
    {
        try
        {
            if (RequireSourceRevision && string.IsNullOrWhiteSpace(SourceRevision))
                throw new InvalidOperationException("A source revision is required. Set ValenceControlHealingSourceRevision or disable ValenceControlHealingRequireSourceRevision explicitly.");

            var manifest = Generate();
            var json = ComponentManifestSerializer.Serialize(manifest);
            var output = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var temporary = $"{output}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, output, overwrite: true);
            Log.LogMessage(MessageImportance.Low, "Generated healing component manifest '{0}'.", output);
            return true;
        }
        catch (Exception exception)
        {
            TryDeleteOutput();
            Log.LogErrorFromException(exception, showStackTrace: true);
            return false;
        }
    }

    private HealingComponentManifest Generate()
    {
        var assetsPath = Path.GetFullPath(ProjectAssetsFile);
        if (!File.Exists(assetsPath))
            throw new FileNotFoundException("The resolved NuGet assets file was not found.", assetsPath);

        var projectRoot = Path.GetFullPath(ProjectDirectory);
        var applicationPath = EnsureContainedFile(projectRoot, ApplicationAssemblyPath, "application assembly");
        using var assetsStream = File.OpenRead(assetsPath);
        using var assets = JsonDocument.Parse(assetsStream);

        var target = SelectTarget(assets.RootElement, TargetFramework, RuntimeIdentifier);
        var libraries = assets.RootElement.GetProperty("libraries");
        var packageRoot = ResolvePackageRoot(assets.RootElement);
        var directDependencies = ReadDirectDependencies(assets.RootElement, TargetFramework);
        var runtimePackages = target.EnumerateObject()
            .Where(x => IsPackage(x.Value) && IsRuntimeComponent(x.Value))
            .ToArray();
        var packageKeys = runtimePackages
            .Select(x => ParseLibraryKey(x.Name))
            .ToDictionary(x => x.Id, x => ComponentKey(x.Id, x.Version), StringComparer.OrdinalIgnoreCase);

        var packageComponents = new List<ComponentManifestEntry>();
        foreach (var targetLibrary in runtimePackages)
        {
            var identity = ParseLibraryKey(targetLibrary.Name);
            if (!libraries.TryGetProperty(targetLibrary.Name, out var library))
                throw new InvalidDataException($"Resolved library '{targetLibrary.Name}' has no library metadata.");
            var libraryPath = library.GetProperty("path").GetString()
                              ?? throw new InvalidDataException($"Resolved library '{targetLibrary.Name}' has no package path.");
            var packageDirectory = EnsureContainedDirectory(packageRoot, libraryPath, $"package '{targetLibrary.Name}'");
            var assemblies = ReadAssemblyAssets(targetLibrary.Value)
                .Select(path => CreateAssembly(packageDirectory, path, targetLibrary.Name))
                .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
                .ToArray();
            var dependencies = ReadDependencies(targetLibrary.Value, packageKeys);
            var repository = ReadPackageRepository(packageDirectory);

            packageComponents.Add(new ComponentManifestEntry(
                ComponentKey(identity.Id, identity.Version),
                "package",
                identity.Id,
                identity.Version,
                HashPackage(packageDirectory, identity),
                repository.Url,
                repository.Commit,
                directDependencies.Contains(identity.Id),
                assemblies,
                dependencies));
        }

        var applicationAssembly = CreateAssembly(projectRoot, Path.GetRelativePath(projectRoot, applicationPath), "application");
        var applicationDependencies = packageComponents
            .Where(x => x.DirectDependency)
            .Select(x => x.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var application = new ComponentManifestEntry(
            $"application:{ApplicationName.Trim()}:{ApplicationVersion.Trim()}",
            "application",
            ApplicationName.Trim(),
            ApplicationVersion.Trim(),
            HashFile(applicationPath),
            RepositoryUrl,
            IsCommitSha(SourceRevision) ? SourceRevision : null,
            true,
            [applicationAssembly],
            applicationDependencies);

        var createdAt = string.IsNullOrWhiteSpace(CreatedAt)
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new HealingComponentManifest(
            "1.0",
            new ComponentManifestApplication(ApplicationName.Trim(), ApplicationVersion.Trim(), TargetFramework.Trim(), NullIfWhiteSpace(RuntimeIdentifier)),
            new ComponentManifestRevision(SourceRevision?.Trim() ?? "unavailable", NullIfWhiteSpace(RepositoryUrl), NullIfWhiteSpace(BuildId), createdAt),
            [application, .. packageComponents]);
    }

    private static JsonElement SelectTarget(JsonElement root, string targetFramework, string? runtimeIdentifier)
    {
        var targets = root.GetProperty("targets");
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(runtimeIdentifier) ? null : $"{targetFramework}/{runtimeIdentifier}",
            targetFramework
        }.Where(x => x is not null).Cast<string>().ToArray();
        foreach (var candidate in candidates)
        {
            if (targets.TryGetProperty(candidate, out var target))
                return target;
        }

        throw new InvalidDataException($"The assets file has no target for '{targetFramework}' and runtime '{runtimeIdentifier}'.");
    }

    private static string ResolvePackageRoot(JsonElement root)
    {
        if (root.TryGetProperty("project", out var project)
            && project.TryGetProperty("restore", out var restore)
            && restore.TryGetProperty("packagesPath", out var configured)
            && !string.IsNullOrWhiteSpace(configured.GetString()))
            return Path.GetFullPath(configured.GetString()!);

        var packageFolder = root.GetProperty("packageFolders").EnumerateObject().FirstOrDefault();
        if (string.IsNullOrWhiteSpace(packageFolder.Name))
            throw new InvalidDataException("The assets file has no resolved package folder.");
        return Path.GetFullPath(packageFolder.Name);
    }

    private static HashSet<string> ReadDirectDependencies(JsonElement root, string targetFramework)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project)
            || !project.TryGetProperty("frameworks", out var frameworks)
            || !frameworks.TryGetProperty(targetFramework, out var framework)
            || !framework.TryGetProperty("dependencies", out var dependencies))
            return result;
        foreach (var dependency in dependencies.EnumerateObject())
            result.Add(dependency.Name);
        return result;
    }

    private static IReadOnlyList<string> ReadDependencies(JsonElement targetLibrary, IReadOnlyDictionary<string, string> packageKeys)
    {
        if (!targetLibrary.TryGetProperty("dependencies", out var dependencies))
            return [];
        return dependencies.EnumerateObject()
            .Where(x => packageKeys.ContainsKey(x.Name))
            .Select(x => packageKeys[x.Name])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadAssemblyAssets(JsonElement targetLibrary)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        AddAssetGroup(targetLibrary, "runtime", result);
        AddAssetGroup(targetLibrary, "compile", result);
        if (targetLibrary.TryGetProperty("runtimeTargets", out var runtimeTargets))
        {
            foreach (var asset in runtimeTargets.EnumerateObject())
            {
                if (asset.Value.TryGetProperty("assetType", out var assetType)
                    && string.Equals(assetType.GetString(), "runtime", StringComparison.OrdinalIgnoreCase)
                    && IsManagedAssemblyPath(asset.Name))
                    result.Add(asset.Name);
            }
        }
        return result.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static void AddAssetGroup(JsonElement targetLibrary, string groupName, ISet<string> result)
    {
        if (!targetLibrary.TryGetProperty(groupName, out var group))
            return;
        foreach (var asset in group.EnumerateObject())
        {
            if (asset.Name != "_._" && IsManagedAssemblyPath(asset.Name))
                result.Add(asset.Name);
        }
    }

    private static bool IsManagedAssemblyPath(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsRuntimeComponent(JsonElement targetLibrary) =>
        ReadAssemblyAssets(targetLibrary).Count > 0
        || (targetLibrary.TryGetProperty("dependencies", out var dependencies) && dependencies.EnumerateObject().Any());

    private static (string? Url, string? Commit) ReadPackageRepository(string packageDirectory)
    {
        var nuspec = Directory.EnumerateFiles(packageDirectory, "*.nuspec", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault();
        if (nuspec is null)
            return (null, null);
        var safeNuspec = EnsureContainedFile(packageDirectory, Path.GetFileName(nuspec), "package metadata");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_048_576
        };
        using var stream = File.OpenRead(safeNuspec);
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var repository = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "repository");
        return (NullIfWhiteSpace(repository?.Attribute("url")?.Value), NullIfWhiteSpace(repository?.Attribute("commit")?.Value));
    }

    private static ComponentManifestAssembly CreateAssembly(string root, string relativePath, string owner)
    {
        var path = EnsureContainedFile(root, relativePath, $"assembly asset for {owner}");
        var canonicalPath = NormalizeRelativePath(relativePath);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
            throw new InvalidDataException($"Assembly asset '{canonicalPath}' has no managed metadata.");
        var metadata = peReader.GetMetadataReader();
        if (!metadata.IsAssembly)
            throw new InvalidDataException($"Assembly asset '{canonicalPath}' is not a managed assembly.");
        var definition = metadata.GetAssemblyDefinition();
        var publicKey = metadata.GetBlobBytes(definition.PublicKey);
        var publicKeyToken = publicKey.Length == 0 ? null : ComputePublicKeyToken(publicKey);
        return new ComponentManifestAssembly(
            metadata.GetString(definition.Name),
            definition.Version.ToString(),
            publicKeyToken,
            canonicalPath,
            HashFile(path));
    }

    private static string? ComputePublicKeyToken(byte[] publicKey)
    {
        var hash = SHA1.HashData(publicKey);
        return Convert.ToHexString(hash[^8..].Reverse().ToArray()).ToLowerInvariant();
    }

    private static string HashPackage(string packageDirectory, (string Id, string Version) identity)
    {
        var files = Directory.EnumerateFiles(packageDirectory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        })
            .Select(path => (Path: path, RelativePath: NormalizeRelativePath(Path.GetRelativePath(packageDirectory, path))))
            .Where(x => !string.Equals(x.RelativePath, ".nupkg.metadata", StringComparison.OrdinalIgnoreCase)
                        && !x.RelativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                        && !x.RelativePath.EndsWith(".nupkg.sha512", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException($"The resolved package contents for '{identity.Id}/{identity.Version}' cannot be hashed.", packageDirectory);

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var safePath = EnsureContainedFile(packageDirectory, file.RelativePath, $"package artifact for '{identity.Id}/{identity.Version}'");
            aggregate.AppendData(System.Text.Encoding.UTF8.GetBytes(file.RelativePath));
            aggregate.AppendData([0]);
            aggregate.AppendData(HashFileBytes(safePath));
            aggregate.AppendData([(byte)'\n']);
        }
        return $"sha256:{Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant()}";
    }

    private static string EnsureContainedFile(string root, string path, string description)
    {
        var candidate = EnsureContainedPath(root, path, description);
        if (!File.Exists(candidate))
            throw new FileNotFoundException($"The {description} cannot be hashed because it does not exist.", candidate);
        return candidate;
    }

    private static string EnsureContainedDirectory(string root, string path, string description)
    {
        var candidate = EnsureContainedPath(root, path, description);
        if (!Directory.Exists(candidate))
            throw new DirectoryNotFoundException($"The resolved {description} directory does not exist.");
        return candidate;
    }

    private static string EnsureContainedPath(string root, string path, string description)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(fullRoot, path));
        var relative = Path.GetRelativePath(fullRoot, candidate);
        if (!IsSafeRelativePath(relative))
            throw new InvalidDataException($"The {description} path '{path}' is unsafe because it escapes its allowed root.");
        ValidatePhysicalContainment(fullRoot, relative, description);
        return candidate;
    }

    private static void ValidatePhysicalContainment(string root, string relativePath, string description)
    {
        var physicalRoot = ResolveLink(new DirectoryInfo(root)) ?? root;
        var current = physicalRoot;
        foreach (var segment in relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            var resolved = Directory.Exists(next)
                ? ResolveLink(new DirectoryInfo(next))
                : File.Exists(next)
                    ? ResolveLink(new FileInfo(next))
                    : null;
            current = resolved ?? next;
            var physicalRelative = Path.GetRelativePath(physicalRoot, Path.GetFullPath(current));
            if (!IsSafeRelativePath(physicalRelative) && physicalRelative != ".")
                throw new InvalidDataException($"The {description} path is unsafe because a symbolic link escapes its allowed root.");
        }
    }

    private static string? ResolveLink(FileSystemInfo info)
    {
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target is null ? null : Path.GetFullPath(target.FullName);
    }

    private static string NormalizeRelativePath(string path)
    {
        var candidate = path.Replace('\\', '/');
        if (!IsSafeRelativePath(candidate))
            throw new InvalidDataException($"Resolved asset path '{path}' is unsafe.");
        return string.Join('/', candidate.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            return false;
        var candidate = path.Replace('\\', '/');
        if (candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':')
            return false;
        return !candidate.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x is "." or "..");
    }

    private static bool IsPackage(JsonElement targetLibrary) =>
        targetLibrary.TryGetProperty("type", out var type) && string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase);

    private static (string Id, string Version) ParseLibraryKey(string key)
    {
        var separator = key.LastIndexOf('/');
        if (separator <= 0 || separator == key.Length - 1)
            throw new InvalidDataException($"Resolved package identity '{key}' is invalid.");
        return (key[..separator], key[(separator + 1)..]);
    }

    private static string ComponentKey(string id, string version) => $"nuget:{id}:{version}";

    private static bool IsCommitSha(string? value) =>
        value?.Length == 40 && value.All(Uri.IsHexDigit);

    private static string HashFile(string path)
    {
        return $"sha256:{Convert.ToHexString(HashFileBytes(path)).ToLowerInvariant()}";
    }

    private static byte[] HashFileBytes(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void TryDeleteOutput()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(OutputPath) && File.Exists(OutputPath))
                File.Delete(OutputPath);
        }
        catch
        {
            // Preserve the original generation failure.
        }
    }
}
