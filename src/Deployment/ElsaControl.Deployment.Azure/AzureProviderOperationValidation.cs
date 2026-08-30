using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElsaControl.Deployment.Azure;

public static class AzureProviderOperationValidation
{
    public static void ValidateCheckpoint(AzureProviderCheckpoint checkpoint)
    {
        if (checkpoint is null) throw new ArgumentNullException(nameof(checkpoint));
        if (checkpoint.Resources is null) throw new ArgumentException("Resources are required.", nameof(checkpoint));
        if (!Enum.IsDefined(checkpoint.Phase) || !Enum.IsDefined(checkpoint.Health) || !IsSafeCode(checkpoint.Code))
            throw new ArgumentException("Checkpoint code, phase, and health are required.", nameof(checkpoint));
        if (checkpoint.Message is null || checkpoint.Message.Length > 2000 || checkpoint.Message.Any(char.IsControl) || ContainsSensitiveMarker(checkpoint.Message))
            throw new ArgumentException("Checkpoint message is unsafe.", nameof(checkpoint));
        ValidateEndpoint(checkpoint.Endpoint);
        if (checkpoint.Diagnostics is null || checkpoint.Diagnostics.Count > 20) throw new ArgumentException("Diagnostics are required and bounded.", nameof(checkpoint));
        if (JsonSerializer.Serialize(checkpoint.Diagnostics).Length > 10000) throw new ArgumentException("Diagnostics are too large.", nameof(checkpoint));
        foreach (var diagnostic in checkpoint.Diagnostics)
        {
            if (diagnostic is null || !IsSafeCode(diagnostic.Code) ||
                string.IsNullOrWhiteSpace(diagnostic.Message) || diagnostic.Message.Length > 2000 || diagnostic.Message.Any(char.IsControl) || ContainsSensitiveMarker(diagnostic.Message))
                throw new ArgumentException("Diagnostic is unsafe.", nameof(checkpoint));
        }
        ValidateReferences(checkpoint.Resources);
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

    private static bool IsSafeRepository(string? value) => value is not null && value.Length <= 512 &&
        Regex.IsMatch(value, "^[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?(?:/[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?)*\\z", RegexOptions.IgnoreCase);

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
            IdempotencyKey = request.IdempotencyKey.Trim()
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
            normalized.ReleaseManifestSignatureDigest
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
    private static bool ContainsSensitiveMarker(string value) =>
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("connectionstring", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("stack trace", StringComparison.OrdinalIgnoreCase);
}
