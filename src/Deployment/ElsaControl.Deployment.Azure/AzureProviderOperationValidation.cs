using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace ElsaControl.Deployment.Azure;

public static class AzureProviderOperationValidation
{
    public static void ValidateCheckpoint(AzureProviderCheckpoint checkpoint)
    {
        if (checkpoint is null) throw new ArgumentNullException(nameof(checkpoint));
        if (checkpoint.Resources is null) throw new ArgumentException("Resources are required.", nameof(checkpoint));
        if (!Enum.IsDefined(checkpoint.Phase) || !Enum.IsDefined(checkpoint.Health) || !IsSafeCode(checkpoint.Code))
            throw new ArgumentException("Checkpoint code, phase, and health are required.", nameof(checkpoint));
        if (string.IsNullOrWhiteSpace(checkpoint.Message) || checkpoint.Message.Length > 2000 || checkpoint.Message.Any(char.IsControl) || ContainsSensitiveMarker(checkpoint.Message))
            throw new ArgumentException("Checkpoint message is unsafe.", nameof(checkpoint));
        ValidateEndpoint(checkpoint.Endpoint);
        if (!IsSafeDiagnostics(checkpoint.Diagnostics)) throw new ArgumentException("Diagnostics are required and bounded.", nameof(checkpoint));
        ValidateReferences(checkpoint.Resources);
    }

    /// <summary>
    /// Applies the same bounded, value-free diagnostics contract to persisted values that are
    /// read back from storage. Invalid stored diagnostics must not be returned through status
    /// responses, even when their JSON is syntactically valid.
    /// </summary>
    public static bool IsSafeDiagnostics(IReadOnlyList<AzureProviderDiagnostic>? diagnostics)
    {
        if (diagnostics is null || diagnostics.Count > 20)
            return false;

        try
        {
            if (JsonSerializer.Serialize(diagnostics).Length > 10000)
                return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        return diagnostics.All(diagnostic => diagnostic is not null &&
            IsSafeCode(diagnostic.Code) &&
            !string.IsNullOrWhiteSpace(diagnostic.Message) &&
            diagnostic.Message.Length <= 2000 &&
            !diagnostic.Message.Any(char.IsControl) &&
            !ContainsSensitiveMarker(diagnostic.Message));
    }

    public static void ValidateCode(string code)
    {
        if (!IsSafeCode(code)) throw new ArgumentException("Operation event code is unsafe.", nameof(code));
    }

    public static void ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 2000 || message.Any(char.IsControl) || ContainsSensitiveMarker(message))
            throw new ArgumentException("Operation event message is unsafe.", nameof(message));
    }

    public static void ValidateLeaseToken(string leaseToken)
    {
        if (string.IsNullOrWhiteSpace(leaseToken) || leaseToken.Length > 512 || leaseToken.Any(char.IsControl))
            throw new ArgumentException("Lease token is unsafe.", nameof(leaseToken));
    }

    public static void ValidateEndpoint(string? endpoint)
    {
        if (endpoint is null) return;
        if (endpoint.Length > 2048 || endpoint.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Contains("%2f", StringComparison.OrdinalIgnoreCase) || endpoint.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            Uri.UnescapeDataString(uri.AbsolutePath).Contains("..", StringComparison.Ordinal) || uri.AbsolutePath.Any(char.IsControl))
            throw new ArgumentException("Endpoint must be a safe HTTPS URI.", nameof(endpoint));
    }

    /// <summary>
    /// Validates an immutable evidence locator without resolving or dereferencing it. Evidence
    /// references are metadata only; their digest is validated separately by the admission
    /// contract. OCI and HTTPS are the only supported locator schemes.
    /// </summary>
    public static bool IsSafeImmutableLocator(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 2048 ||
            reference.Any(char.IsWhiteSpace) ||
            reference.Contains("/../", StringComparison.Ordinal) ||
            reference.Contains("/./", StringComparison.Ordinal) ||
            reference.Contains('\\') ||
            !Uri.TryCreate(reference, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "oci" && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.AbsolutePath is "/" or "" ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath.Any(char.IsControl) ||
            uri.AbsolutePath.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Contains("%2f", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            return false;

        var unescapedPath = Uri.UnescapeDataString(uri.AbsolutePath);
        return !unescapedPath.Split('/').Any(segment => segment is "." or "..");
    }

    /// <summary>
    /// Validates an evidence locator and binds an optional digest embedded in the locator to
    /// the separately retained digest. Evidence without either digest form is not immutable.
    /// </summary>
    public static bool IsSafeImmutableEvidenceReference(string? reference, string? digest)
    {
        if (!IsSafeImmutableLocator(reference))
            return false;

        var at = reference!.IndexOf('@');
        var embeddedDigest = at < 0 ? null : reference[(at + 1)..];
        if (at >= 0 && (at != reference.LastIndexOf('@') || !IsSha256Digest(embeddedDigest)))
            return false;
        if (embeddedDigest is not null && IsSha256Digest(digest) &&
            !string.Equals(embeddedDigest, digest, StringComparison.OrdinalIgnoreCase))
            return false;
        return IsSha256Digest(digest) || embeddedDigest is not null;
    }

    public static void ValidateReferences(AzureProviderResourceReferences references)
    {
        if (references is null) throw new ArgumentNullException(nameof(references));
        ValidateAzureName(references.ResourceGroupName, 90, "resourceGroupName");
        ValidateAzureReference(references.FoundationDeploymentId, 512, "foundationDeploymentId");
        ValidateAzureReference(references.WorkloadDeploymentId, 512, "workloadDeploymentId");
        ValidateAzureReference(references.WorkloadResourceId, 1024, "workloadResourceId");
        ValidateAzureName(references.WorkloadRevisionName, 128, "workloadRevisionName");
        ValidateAzureName(references.StableTrafficRevisionName, 128, "stableTrafficRevisionName");
    }

    public static void ValidateWorkerId(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128 || !Regex.IsMatch(workerId, "^[A-Za-z0-9][A-Za-z0-9._-]*\\z"))
            throw new ArgumentException("Worker ID is unsafe.", nameof(workerId));
    }

    private static void ValidateAzureName(string? value, int max, string name)
    {
        if (value is null) return;
        if (value.Length > max || !Regex.IsMatch(value, "^[A-Za-z0-9._()\\-]+\\z") || ContainsSensitiveMarker(value))
            throw new ArgumentException("Azure resource name is unsafe.", name);
    }

    private static void ValidateAzureReference(string? value, int maxLength, string name)
    {
        if (value is null) return;
        if (value.Length > maxLength || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) ||
            value.Contains("?", StringComparison.Ordinal) || value.Contains("#", StringComparison.Ordinal) ||
            value.Contains("@", StringComparison.Ordinal) || value.Contains("://", StringComparison.Ordinal) ||
            ContainsSensitiveMarker(value) || !Regex.IsMatch(value, "^[A-Za-z0-9._:/()\\-]+\\z"))
            throw new ArgumentException("Azure resource reference is unsafe.", name);
    }

    private static bool IsSafeCode(string? value) => value is not null && value.Length <= 128 && Regex.IsMatch(value, "^[a-z0-9]+(?:[._-][a-z0-9]+)*\\z");

    private static bool IsSha256Digest(string? value) => value is not null && value.Length == "sha256:".Length + 64 &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && value["sha256:".Length..].All(Uri.IsHexDigit);

    private static bool IsSafeRepository(string? value) => value is not null && value.Length <= 512 &&
        Regex.IsMatch(value, "^[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?(?:/[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?)*\\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static AzureProviderOperationRequest Normalize(AzureProviderOperationRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("; ", errors), nameof(request));

        return request with
        {
            TargetKey = request.TargetKey.Trim().ToLowerInvariant(),
            PlanFingerprint = NormalizeFingerprint(request.PlanFingerprint),
            TemplateFingerprint = NormalizeFingerprint(request.TemplateFingerprint),
            ImageDigest = NormalizeDigest(request.ImageDigest),
            ReleaseManifestDigest = NormalizeOptionalDigest(request.ReleaseManifestDigest),
            ReleaseManifestSignatureDigest = NormalizeOptionalDigest(request.ReleaseManifestSignatureDigest),
            Location = request.Location.Trim().ToLowerInvariant(),
            Topology = request.Topology.Trim().ToLowerInvariant(),
            Isolation = request.Isolation.Trim().ToLowerInvariant(),
            ReleaseLine = request.ReleaseLine.Trim(),
            ElsaVersion = request.ElsaVersion.Trim(),
            ImageRepository = request.ImageRepository.Trim().ToLowerInvariant(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            ReleaseManifestReference = NormalizeOptionalReference(request.ReleaseManifestReference),
            ReleaseManifestSignatureReference = NormalizeOptionalReference(request.ReleaseManifestSignatureReference),
            SecretReferences = NormalizeSecretReferences(request.SecretReferences)
        };
    }

    public static IReadOnlyList<string> Validate(AzureProviderOperationRequest request)
    {
        var errors = new List<string>();
        if (request.WorkspaceId == Guid.Empty) errors.Add("workspace.required");
        if (!Enum.IsDefined(request.Action)) errors.Add("action.invalid");
        Required(request.TargetKey, "target");
        Required(request.IdempotencyKey, "idempotency");
        Required(request.PlanFingerprint, "planFingerprint");
        Required(request.TemplateFingerprint, "templateFingerprint");
        Required(request.ElsaVersion, "elsaVersion");
        Required(request.ReleaseLine, "releaseLine");
        Required(request.Topology, "topology");
        Required(request.Isolation, "isolation");
        Required(request.Location, "location");
        Required(request.ImageRepository, "imageRepository");
        Required(request.ImageDigest, "imageDigest");

        if (!IsFingerprint(request.PlanFingerprint)) errors.Add("planFingerprint.invalid");
        if (!IsFingerprint(request.TemplateFingerprint)) errors.Add("templateFingerprint.invalid");
        if (!IsDigest(request.ImageDigest)) errors.Add("imageDigest.invalid");
        if (request.ReleaseManifestDigest is not null && !IsDigest(request.ReleaseManifestDigest)) errors.Add("releaseManifestDigest.invalid");
        if (request.ReleaseManifestSignatureDigest is not null && !IsDigest(request.ReleaseManifestSignatureDigest)) errors.Add("releaseManifestSignatureDigest.invalid");
        if (request.ReleaseManifestReference is not null && !IsSafeImmutableEvidenceReference(request.ReleaseManifestReference, request.ReleaseManifestDigest)) errors.Add("releaseManifestReference.invalid");
        if (request.ReleaseManifestSignatureReference is not null && !IsSafeImmutableEvidenceReference(request.ReleaseManifestSignatureReference, request.ReleaseManifestSignatureDigest)) errors.Add("releaseManifestSignatureReference.invalid");
        if ((request.ReleaseManifestReference is null) != (request.ReleaseManifestSignatureReference is null)) errors.Add("releaseManifestReferences.incomplete");
        ValidateSecretReferences(request.SecretReferences, errors);

        BoundedSafe(request.TargetKey, 128, "target", errors);
        BoundedSafe(request.IdempotencyKey, 512, "idempotency", errors);
        BoundedSafe(request.ElsaVersion, 128, "elsaVersion", errors);
        BoundedSafe(request.ReleaseLine, 64, "releaseLine", errors);
        BoundedSafe(request.Topology, 64, "topology", errors);
        BoundedSafe(request.Isolation, 64, "isolation", errors);
        BoundedSafe(request.Location, 64, "location", errors);
        BoundedSafe(request.ImageRepository, 512, "imageRepository", errors);
        if (!IsSafeRepository(request.ImageRepository))
            errors.Add("imageRepository.mustBeRepository");

        return errors;

        void Required(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{name}.required");
        }
    }

    public static string ComputeRequestHash(AzureProviderOperationRequest request)
    {
        var normalized = Normalize(request);
        var canonical = JsonSerializer.Serialize(new
        {
            normalized.WorkspaceId,
            normalized.TargetKey,
            Action = normalized.Action.ToString(),
            normalized.PlanFingerprint,
            normalized.TemplateFingerprint,
            normalized.ElsaVersion,
            normalized.ReleaseLine,
            normalized.Topology,
            normalized.Isolation,
            normalized.Location,
            normalized.ImageRepository,
            normalized.ImageDigest,
            normalized.ReleaseManifestDigest,
            normalized.ReleaseManifestSignatureDigest,
            normalized.ReleaseManifestReference,
            normalized.ReleaseManifestSignatureReference,
            secretReferences = normalized.SecretReferences
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string ComputeOperationIdentity(AzureProviderOperationRequest request)
    {
        var normalized = Normalize(request);
        var value = string.Join('|', normalized.WorkspaceId.ToString("N"), normalized.TargetKey,
            normalized.Action, normalized.PlanFingerprint, normalized.TemplateFingerprint,
            normalized.ImageDigest, normalized.Location, normalized.Topology, normalized.Isolation);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static void BoundedSafe(string? value, int max, string name, ICollection<string> errors)
    {
        if (value is not null && value.Length > max) errors.Add($"{name}.tooLong");
        if (value is not null && value.Any(char.IsControl)) errors.Add($"{name}.unsafe");
    }

    private static bool IsFingerprint(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool IsDigest(string? value) => value is not null && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && IsFingerprint(value[7..]);
    private static string NormalizeFingerprint(string value) => value.Trim().ToLowerInvariant();
    private static string NormalizeDigest(string value) => $"sha256:{value.Trim()[7..].ToLowerInvariant()}";
    private static string? NormalizeOptionalDigest(string? value) => value is null ? null : NormalizeDigest(value);
    private static string? NormalizeOptionalReference(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string> NormalizeSecretReferences(IReadOnlyDictionary<string, string>? values) =>
        new ReadOnlyDictionary<string, string>((values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key.Trim().ToLowerInvariant(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase));

    private static void ValidateSecretReferences(IReadOnlyDictionary<string, string>? values, ICollection<string> errors)
    {
        if (values is null)
            return;
        if (values.Count > 64)
            errors.Add("secretReferences.tooMany");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 256 || pair.Key.Any(char.IsControl) || !keys.Add(pair.Key.Trim()))
                errors.Add("secretReferences.key.invalid");
            if (!IsSafeSecretReference(pair.Value))
                errors.Add("secretReferences.value.invalid");
        }
    }

    public static bool IsSafeSecretReferences(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count > 64)
            return false;

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return values.All(pair =>
            !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 256 &&
            !pair.Key.Any(char.IsControl) &&
            string.Equals(pair.Key, pair.Key.Trim().ToLowerInvariant(), StringComparison.Ordinal) &&
            keys.Add(pair.Key) && IsSafeSecretReference(pair.Value));
    }

    /// <summary>
    /// Secret references are opaque provider locators. They are never dereferenced by the
    /// control plane and intentionally reject URI features that could make a locator behave
    /// like a filesystem path or carry credentials.
    /// </summary>
    public static bool IsSafeSecretReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) ||
            value.Contains('\\') || value.Contains('%') || value.Contains('?') || value.Contains('#') ||
            value.Contains("/../", StringComparison.Ordinal) || value.Contains("/./", StringComparison.Ordinal) ||
            value.EndsWith("/..", StringComparison.Ordinal) || value.EndsWith("/.", StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "secret" ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath.Contains("//", StringComparison.Ordinal))
            return false;

        // A host-only locator (for example secret://database) is a valid opaque provider key.
        // A trailing slash, empty segment or dot segment is rejected because it makes the
        // locator ambiguous if a downstream provider ever maps it to a hierarchical key.
        if (uri.AbsolutePath.Length == 0 ||
            (uri.AbsolutePath == "/" && !value.EndsWith("/", StringComparison.Ordinal)))
            return true;

        if (!uri.AbsolutePath.StartsWith("/", StringComparison.Ordinal))
            return false;

        var segments = uri.AbsolutePath[1..].Split('/', StringSplitOptions.None);
        return segments.All(segment => !string.IsNullOrEmpty(segment) && segment is not "." and not "..");
    }

    private static bool ContainsSensitiveMarker(string value) =>
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("stack trace", StringComparison.OrdinalIgnoreCase);
}
