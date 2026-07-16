using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Elsa.Platform.Healing.ComponentManifest;

public sealed record HealingComponentManifest(
    string SchemaVersion,
    ComponentManifestApplication Application,
    ComponentManifestRevision Revision,
    IReadOnlyList<ComponentManifestEntry> Components,
    string? ManifestDigest = null)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ComponentManifestApplication(
    string Name,
    string Version,
    string TargetFramework,
    string? RuntimeIdentifier)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ComponentManifestRevision(
    string SourceRevision,
    string? RepositoryUrl,
    string? BuildId,
    DateTimeOffset CreatedAt)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ComponentManifestEntry(
    string Key,
    string Kind,
    string Name,
    string? Version,
    string ContentHash,
    string? RepositoryUrl,
    string? RepositoryCommit,
    bool DirectDependency,
    IReadOnlyList<ComponentManifestAssembly> Assemblies,
    IReadOnlyList<string> Dependencies)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ComponentManifestAssembly(
    string Name,
    string? Version,
    string? PublicKeyToken,
    string RelativePath,
    string ContentHash)
{
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record ComponentManifestValidationFinding(string Code, string Message, string? Path = null);

public sealed class ComponentManifestValidationException(IReadOnlyList<ComponentManifestValidationFinding> findings)
    : Exception(string.Join(Environment.NewLine, findings.Select(x => $"{x.Code}: {x.Message}")))
{
    public IReadOnlyList<ComponentManifestValidationFinding> Findings { get; } = findings;
}

public static partial class ComponentManifestValidator
{
    public static IReadOnlyList<ComponentManifestValidationFinding> Validate(HealingComponentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var findings = new List<ComponentManifestValidationFinding>();

        if (!string.Equals(manifest.SchemaVersion, "1.0", StringComparison.Ordinal))
            findings.Add(new("manifest.schema.unsupported", "Schema version must be '1.0'.", "schemaVersion"));

        if (manifest.Application is null)
            findings.Add(new("manifest.shape.invalid", "Application must be an object.", "application"));
        else
        {
            Required(manifest.Application.Name, "application.name", findings);
            Required(manifest.Application.Version, "application.version", findings);
            Required(manifest.Application.TargetFramework, "application.targetFramework", findings);
            ValidateExtensions(manifest.Application.ExtensionData, "application", findings);
        }

        if (manifest.Revision is null)
            findings.Add(new("manifest.shape.invalid", "Revision must be an object.", "revision"));
        else
        {
            Required(manifest.Revision.SourceRevision, "revision.sourceRevision", findings);
            if (!string.IsNullOrWhiteSpace(manifest.Revision.SourceRevision)
                && !string.Equals(manifest.Revision.SourceRevision, "unavailable", StringComparison.Ordinal)
                && !CommitShaPattern().IsMatch(manifest.Revision.SourceRevision))
                findings.Add(new("manifest.revision.source-revision", "Source revision must be a 40-character hexadecimal commit SHA or the explicit 'unavailable' sentinel.", "revision.sourceRevision"));
            if (manifest.Revision.CreatedAt == default)
                findings.Add(new("manifest.revision.created-at", "Revision creation time is required.", "revision.createdAt"));
            ValidateRepositoryUrl(manifest.Revision.RepositoryUrl, "revision.repositoryUrl", findings);
            ValidateExtensions(manifest.Revision.ExtensionData, "revision", findings);
        }

        ValidateExtensions(manifest.ExtensionData, "$", findings);
        if (manifest.ManifestDigest is not null && !Sha256Pattern().IsMatch(manifest.ManifestDigest))
            findings.Add(new("manifest.digest.invalid", "Manifest digest must be a lowercase SHA-256 value.", "manifestDigest"));

        if (manifest.Components is null)
        {
            findings.Add(new("manifest.shape.invalid", "Components must be an array.", "components"));
            return findings;
        }

        var nonNullComponents = manifest.Components.Where(x => x is not null).ToArray();
        if (nonNullComponents.Length != manifest.Components.Count)
            findings.Add(new("manifest.shape.invalid", "Component entries must be objects.", "components"));
        var knownKeys = nonNullComponents
            .Select(x => x.Key)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        for (var componentIndex = 0; componentIndex < manifest.Components.Count; componentIndex++)
        {
            var component = manifest.Components[componentIndex];
            var componentPath = $"components[{componentIndex}]";
            if (component is null)
                continue;
            Required(component.Key, $"{componentPath}.key", findings);
            Required(component.Name, $"{componentPath}.name", findings);
            Required(component.Kind, $"{componentPath}.kind", findings);
            ValidateHash(component.ContentHash, $"{componentPath}.contentHash", findings);
            ValidateRepositoryUrl(component.RepositoryUrl, $"{componentPath}.repositoryUrl", findings);
            if (!string.IsNullOrWhiteSpace(component.RepositoryCommit)
                && !CommitShaPattern().IsMatch(component.RepositoryCommit))
                findings.Add(new("manifest.repository-commit.invalid", "Repository commit must be a 40-character hexadecimal commit SHA.", $"{componentPath}.repositoryCommit"));
            ValidateExtensions(component.ExtensionData, componentPath, findings);

            if (component.Assemblies is null)
                findings.Add(new("manifest.shape.invalid", "Component assemblies must be an array.", $"{componentPath}.assemblies"));
            else
            {
                var assemblyPaths = new HashSet<string>(StringComparer.Ordinal);
                for (var assemblyIndex = 0; assemblyIndex < component.Assemblies.Count; assemblyIndex++)
                {
                    var assembly = component.Assemblies[assemblyIndex];
                    if (assembly is null)
                    {
                        findings.Add(new("manifest.shape.invalid", "Assembly entries must be objects.", $"{componentPath}.assemblies[{assemblyIndex}]"));
                        continue;
                    }
                    Required(assembly.Name, $"{componentPath}.assemblies[{assemblyIndex}].name", findings);
                    ValidateHash(assembly.ContentHash, $"{componentPath}.assemblies[{assemblyIndex}].contentHash", findings);
                    if (!ComponentManifestCanonicalizer.TryNormalizeRelativePath(assembly.RelativePath, out var normalizedPath))
                        findings.Add(new("manifest.path.unsafe", $"Assembly path '{assembly.RelativePath}' must be a canonical relative path that cannot escape its component root.", $"{componentPath}.assemblies[{assemblyIndex}].relativePath"));
                    else if (!assemblyPaths.Add(normalizedPath))
                        findings.Add(new("manifest.assembly.duplicate-path", $"Assembly path '{normalizedPath}' must occur exactly once within component '{component.Key}'.", $"{componentPath}.assemblies[{assemblyIndex}].relativePath"));
                    ValidateExtensions(assembly.ExtensionData, $"{componentPath}.assemblies[{assemblyIndex}]", findings);
                }
            }

            if (component.Dependencies is null)
            {
                findings.Add(new("manifest.shape.invalid", "Component dependencies must be an array.", $"{componentPath}.dependencies"));
                continue;
            }
            foreach (var dependency in component.Dependencies.Where(x => x is not null).Distinct(StringComparer.Ordinal))
            {
                if (!knownKeys.Contains(dependency))
                    findings.Add(new("manifest.dependency.unknown", $"Dependency '{dependency}' does not identify a component in this manifest.", $"{componentPath}.dependencies"));
            }
        }

        foreach (var group in nonNullComponents.GroupBy(x => x.Key, StringComparer.Ordinal).Where(x => x.Count() > 1))
            findings.Add(new("manifest.component.duplicate-key", $"Component key '{group.Key}' must occur exactly once.", "components"));

        return findings;
    }

    public static void ValidateAndThrow(HealingComponentManifest manifest)
    {
        var findings = Validate(manifest);
        if (findings.Count > 0)
            throw new ComponentManifestValidationException(findings);
    }

    private static void Required(string? value, string path, ICollection<ComponentManifestValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value))
            findings.Add(new("manifest.value.required", $"'{path}' is required.", path));
    }

    private static void ValidateHash(string? value, string path, ICollection<ComponentManifestValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value) || !Sha256Pattern().IsMatch(value))
            findings.Add(new("manifest.hash.invalid", $"'{path}' must be a lowercase SHA-256 value.", path));
    }

    private static void ValidateRepositoryUrl(string? value, string path, ICollection<ComponentManifestValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            findings.Add(new("manifest.repository.invalid", $"'{path}' must be an absolute HTTP(S) repository URL.", path));
            return;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
            findings.Add(new("manifest.repository.credentials", $"'{path}' cannot contain user information or credentials.", path));
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            findings.Add(new("manifest.repository.sensitive-suffix", $"'{path}' cannot contain a query string or fragment.", path));
    }

    private static void ValidateExtensions(
        IDictionary<string, JsonElement>? extensions,
        string path,
        ICollection<ComponentManifestValidationFinding> findings)
    {
        if (extensions is null)
            return;
        foreach (var extension in extensions)
        {
            var extensionPath = $"{path}.{extension.Key}";
            if (IsSensitiveExtensionKey(extension.Key) || !IsSafeExtensionValue(extension.Value))
                findings.Add(new("manifest.extension.unsafe", $"Unknown field '{extensionPath}' contains data that is unsafe for a portable component manifest.", extensionPath));
        }
    }

    private static bool IsSafeExtensionValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (IsSensitiveExtensionKey(property.Name) || !IsSafeExtensionValue(property.Value))
                        return false;
                }
                return true;
            case JsonValueKind.Array:
                return value.EnumerateArray().All(IsSafeExtensionValue);
            case JsonValueKind.String:
                return IsSafeExtensionString(value.GetString());
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                return true;
            default:
                return false;
        }
    }

    private static bool IsSafeExtensionString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;
        var candidate = value.Trim().Replace('\\', '/');
        if (candidate.Length == 0)
            return true;
        if (candidate[0] == '/'
            || (candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':')
            || candidate.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(x => x == ".."))
            return false;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return true;
        if (uri.IsFile)
            return false;
        return string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool IsSensitiveExtensionKey(string key)
    {
        var normalized = new string(key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return SensitiveExtensionKeyFragments.Any(normalized.Contains);
    }

    private static readonly string[] SensitiveExtensionKeyFragments =
    [
        "token", "secret", "password", "credential", "authorization", "apikey", "connectionstring",
        "privatekey", "feedauthentication", "environmentvariable", "sourcecontent", "packagecache",
        "localpath", "absolutepath", "username"
    ];

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaPattern();
}

public static class ComponentManifestDigest
{
    public static string Compute(HealingComponentManifest manifest)
    {
        ComponentManifestValidator.ValidateAndThrow(manifest);
        var bytes = ComponentManifestCanonicalizer.CanonicalBytes(manifest, includeDigest: false, digest: null);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }
}

public static class ComponentManifestSerializer
{
    public static string Serialize(HealingComponentManifest manifest)
    {
        ComponentManifestValidator.ValidateAndThrow(manifest);
        var digest = ComponentManifestDigest.Compute(manifest);
        return Encoding.UTF8.GetString(ComponentManifestCanonicalizer.CanonicalBytes(manifest, includeDigest: true, digest));
    }

    public static HealingComponentManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var manifest = JsonSerializer.Deserialize<HealingComponentManifest>(json, SerializerOptions)
                       ?? throw new JsonException("The component manifest document is empty.");
        ComponentManifestValidator.ValidateAndThrow(manifest);
        var expectedDigest = ComponentManifestDigest.Compute(manifest with { ManifestDigest = null });
        if (!string.Equals(manifest.ManifestDigest, expectedDigest, StringComparison.Ordinal))
            throw new ComponentManifestValidationException([new("manifest.digest.mismatch", "Manifest digest does not match the canonical document.", "manifestDigest")]);
        return manifest;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
}

internal static class ComponentManifestCanonicalizer
{
    public static byte[] CanonicalBytes(HealingComponentManifest manifest, bool includeDigest, string? digest)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = manifest.SchemaVersion.Trim(),
            ["application"] = ApplicationNode(manifest.Application),
            ["revision"] = RevisionNode(manifest.Revision),
            ["components"] = new JsonArray(manifest.Components
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(ComponentNode)
                .ToArray())
        };
        AddExtensions(root, manifest.ExtensionData, "manifestDigest");
        if (includeDigest)
            root["manifestDigest"] = digest;
        return Encoding.UTF8.GetBytes(SortObject(root).ToJsonString(CompactOptions));
    }

    public static bool TryNormalizeRelativePath(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
            return false;
        var candidate = path.Replace('\\', '/').Trim();
        if (candidate[0] == '/' || candidate.StartsWith("//", StringComparison.Ordinal) || (candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':'))
            return false;
        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(x => x is "." or ".."))
            return false;
        normalized = string.Join('/', segments);
        return true;
    }

    public static string NormalizeRepositoryUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var uri = new Uri(value.Trim(), UriKind.Absolute);
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Query = "",
            Fragment = ""
        };
        var path = builder.Path.TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        builder.Path = path;
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static JsonObject ApplicationNode(ComponentManifestApplication application)
    {
        var node = new JsonObject
        {
            ["name"] = application.Name.Trim(),
            ["version"] = application.Version.Trim(),
            ["targetFramework"] = application.TargetFramework.Trim(),
            ["runtimeIdentifier"] = NullIfWhiteSpace(application.RuntimeIdentifier)
        };
        AddExtensions(node, application.ExtensionData);
        return node;
    }

    private static JsonObject RevisionNode(ComponentManifestRevision revision)
    {
        var node = new JsonObject
        {
            ["sourceRevision"] = string.Equals(revision.SourceRevision, "unavailable", StringComparison.Ordinal)
                ? "unavailable"
                : revision.SourceRevision.Trim().ToLowerInvariant(),
            ["repositoryUrl"] = string.IsNullOrWhiteSpace(revision.RepositoryUrl) ? null : NormalizeRepositoryUrl(revision.RepositoryUrl),
            ["buildId"] = NullIfWhiteSpace(revision.BuildId),
            ["createdAt"] = revision.CreatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)
        };
        AddExtensions(node, revision.ExtensionData);
        return node;
    }

    private static JsonObject ComponentNode(ComponentManifestEntry component)
    {
        var node = new JsonObject
        {
            ["key"] = component.Key.Trim(),
            ["kind"] = component.Kind.Trim(),
            ["name"] = component.Name.Trim(),
            ["version"] = NullIfWhiteSpace(component.Version),
            ["contentHash"] = component.ContentHash,
            ["repositoryUrl"] = string.IsNullOrWhiteSpace(component.RepositoryUrl) ? null : NormalizeRepositoryUrl(component.RepositoryUrl),
            ["repositoryCommit"] = string.IsNullOrWhiteSpace(component.RepositoryCommit)
                ? null
                : component.RepositoryCommit.Trim().ToLowerInvariant(),
            ["directDependency"] = component.DirectDependency,
            ["assemblies"] = new JsonArray(component.Assemblies
                .OrderBy(x => NormalizePathOrOriginal(x.RelativePath), StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Version, StringComparer.Ordinal)
                .Select(AssemblyNode)
                .ToArray()),
            ["dependencies"] = new JsonArray(component.Dependencies
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(x => JsonValue.Create(x))
                .ToArray())
        };
        AddExtensions(node, component.ExtensionData);
        return node;
    }

    private static JsonObject AssemblyNode(ComponentManifestAssembly assembly)
    {
        var node = new JsonObject
        {
            ["name"] = assembly.Name.Trim(),
            ["version"] = NullIfWhiteSpace(assembly.Version),
            ["publicKeyToken"] = NullIfWhiteSpace(assembly.PublicKeyToken),
            ["relativePath"] = NormalizePathOrOriginal(assembly.RelativePath),
            ["contentHash"] = assembly.ContentHash
        };
        AddExtensions(node, assembly.ExtensionData);
        return node;
    }

    private static string NormalizePathOrOriginal(string path) => TryNormalizeRelativePath(path, out var normalized) ? normalized : path;

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddExtensions(JsonObject target, IDictionary<string, JsonElement>? extensions, params string[] excluded)
    {
        if (extensions is null)
            return;
        var exclusion = excluded.ToHashSet(StringComparer.Ordinal);
        foreach (var extension in extensions.Where(x => !target.ContainsKey(x.Key) && !exclusion.Contains(x.Key)))
            target[extension.Key] = JsonNode.Parse(extension.Value.GetRawText());
    }

    private static JsonObject SortObject(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var property in source.OrderBy(x => x.Key, StringComparer.Ordinal))
            result[property.Key] = SortNode(property.Value);
        return result;
    }

    private static JsonNode? SortNode(JsonNode? source) => source switch
    {
        JsonObject value => SortObject(value),
        JsonArray value => new JsonArray(value.Select(x => SortNode(x)?.DeepClone()).ToArray()),
        null => null,
        _ => source.DeepClone()
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false
    };
}
