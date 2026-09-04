using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Proof;

namespace ElsaControl.Deployment.ProofHost;

/// <summary>
/// The only lifecycle modes exposed by the disposable proof host.
/// </summary>
public enum ProofHostMode
{
    Validate,
    Run,
    Cleanup
}

/// <summary>
/// Strict, value-safe configuration for the disposable Azure proof host. This object contains
/// secret locators, never secret values. The host intentionally has no implicit production
/// registration; a caller must explicitly choose <see cref="ProofHostMode.Run"/> or
/// <see cref="ProofHostMode.Cleanup"/> and authorize mutation through the exact environment gate.
/// </summary>
public sealed class ProofHostOptions
{
    public static IReadOnlyList<string> SupportedFeatures => ProofHostFeatureContract.Supported;

    public ProofHostMode Mode { get; init; }

    public bool MutationAuthorized { get; init; }

    public Guid WorkspaceId { get; init; }

    public string ProofName { get; init; } = "";

    public string ResourceGroupName { get; init; } = "";

    public string SubscriptionId { get; init; } = "";

    public string RegistrySubscriptionId { get; init; } = "";

    public string RegistryResourceGroupName { get; init; } = "";

    public string RegistryName { get; init; } = "valenceruntimeimages";

    public string Location { get; init; } = AzureWorkloadPlanTranslator.SupportedLocation;

    public string ElsaVersion { get; init; } = "";

    public string Topology { get; init; } = AzureWorkloadPlanTranslator.SupportedTopology;

    public IReadOnlyList<string> Features { get; init; } = [];

    public string ImageRepository { get; init; } = "valenceruntimeimages.azurecr.io/runtime-combined";

    public string ImageDigest { get; init; } = "";

    public string ReleaseManifestReference { get; init; } = "";

    public string ReleaseManifestDigest { get; init; } = "";

    public string ReleaseManifestSignatureReference { get; init; } = "";

    public string ReleaseManifestSignatureDigest { get; init; } = "";

    public string SourceCommit { get; init; } = "";

    public string StatePath { get; init; } = "";

    public string TemplateRoot { get; init; } = "";

    public string AzureCliPath { get; init; } = "";

    public string SqlCmdPath { get; init; } = "";

    public string CurlPath { get; init; } = "";

    public string SqlBootstrapObjectId { get; init; } = "";

    public string SqlBootstrapLogin { get; init; } = "";

    public string SqlBootstrapIp { get; init; } = "";

    public string Owner { get; init; } = "elsa-control";

    public DateOnly ExpiryUtc { get; init; }

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(15);

    public int MaximumOutputCharacters { get; init; } = 1_048_576;

    public int ObservationAttempts { get; init; } = 60;

    public TimeSpan ObservationDelay { get; init; } = TimeSpan.FromSeconds(5);

    public string WorkflowUsername { get; init; } = "proof-admin";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan WorkflowTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromMinutes(20);

    public IReadOnlyDictionary<string, string> SecretReferences { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string ImageReference => $"{ImageRepository}@{ImageDigest}";

    public AzureProviderTargetScope CreateTargetScope()
    {
        EnsureValid();
        var scope = new AzureProviderTargetScope(
            SubscriptionId,
            ResourceGroupName,
            RegistrySubscriptionId,
            RegistryResourceGroupName,
            RegistryName,
            Location);
        scope.Validate();
        return scope;
    }

    public AzureProviderRunnerOptions CreateRunnerOptions()
    {
        EnsureValid();
        var options = new AzureProviderRunnerOptions
        {
            Enabled = true,
            AzureCliPath = AzureCliPath,
            SqlCmdPath = SqlCmdPath,
            CurlPath = CurlPath,
            TemplateRoot = TemplateRoot,
            SqlBootstrapObjectId = SqlBootstrapObjectId,
            SqlBootstrapLogin = SqlBootstrapLogin,
            SqlBootstrapIp = SqlBootstrapIp,
            RuntimeAdminUsername = "proof-admin",
            Owner = Owner,
            CommandTimeout = CommandTimeout,
            MaximumOutputCharacters = MaximumOutputCharacters,
            ObservationAttempts = ObservationAttempts,
            ObservationDelay = ObservationDelay
        };
        options.Validate();
        return options;
    }

    public DeploymentProofInput CreateProofInput()
    {
        EnsureValid();
        return new(ElsaVersion, Topology, Features, ImageReference, ImageDigest, SourceCommit);
    }

    public DeploymentProofEnvironment CreateProofEnvironment()
    {
        EnsureValid();
        return new(ProofName, Location, "azure", SecretReferences.Keys.ToArray());
    }

    public Elsa38CombinedProofAdmission CreateAdmission()
    {
        EnsureValid();
        return new(
            ElsaVersion,
            ImageReference,
            ImageDigest,
            ReleaseManifestReference,
            ReleaseManifestDigest,
            ReleaseManifestSignatureReference,
            ReleaseManifestSignatureDigest,
            SourceCommit,
            Features,
            SecretReferences);
    }

    /// <summary>
    /// Returns stable error codes without including configuration values. Callers can safely
    /// print these errors in CI logs even when the process was supplied with sensitive-looking
    /// values by mistake.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(Mode))
            errors.Add("mode.invalid");

        if (Mode is ProofHostMode.Run or ProofHostMode.Cleanup && !MutationAuthorized)
            errors.Add("mutationGate.required");

        if (WorkspaceId == Guid.Empty)
            errors.Add("workspaceId.required");

        Require(ProofName, "proofName", errors);
        if (!IsAzureWorkloadName(ProofName))
            errors.Add("proofName.invalid");

        Require(ResourceGroupName, "resourceGroupName", errors);
        if (!IsAzureResourceGroupName(ResourceGroupName))
            errors.Add("resourceGroupName.invalid");

        RequireCanonicalGuid(SubscriptionId, "subscriptionId", errors);
        RequireCanonicalGuid(RegistrySubscriptionId, "registrySubscriptionId", errors);

        Require(RegistryResourceGroupName, "registryResourceGroupName", errors);
        if (!IsAzureResourceGroupName(RegistryResourceGroupName))
            errors.Add("registryResourceGroupName.invalid");

        if (!string.Equals(RegistryName, "valenceruntimeimages", StringComparison.Ordinal))
            errors.Add("registryName.invalid");

        if (!AzureWorkloadPlanTranslator.IsSupportedLocation(Location))
            errors.Add("location.unsupported");

        Require(ElsaVersion, "elsaVersion", errors);
        if (!IsElsaVersion(ElsaVersion))
            errors.Add("elsaVersion.invalid");

        if (!string.Equals(Topology, AzureWorkloadPlanTranslator.SupportedTopology, StringComparison.Ordinal))
            errors.Add("topology.unsupported");

        ValidateFeatures(Features, errors);
        if (Features is null ||
            !Features.Order(StringComparer.Ordinal).SequenceEqual(SupportedFeatures.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            errors.Add("features.unsupported");

        if (!IsSupportedImageRepository(ImageRepository))
            errors.Add("imageRepository.invalid");
        if (!IsCanonicalSha256(ImageDigest))
            errors.Add("imageDigest.invalid");

        ValidateEvidenceReference(ReleaseManifestReference, ReleaseManifestDigest, "releaseManifest", errors);
        ValidateEvidenceReference(ReleaseManifestSignatureReference, ReleaseManifestSignatureDigest, "releaseManifestSignature", errors);
        if (SourceCommit is not { Length: 40 } || !SourceCommit.All(char.IsAsciiHexDigit))
            errors.Add("sourceCommit.invalid");
        ValidateStatePath(StatePath, errors);

        Require(TemplateRoot, "templateRoot", errors);
        if (!IsSafeAuthorityDirectory(TemplateRoot))
            errors.Add("templateRoot.invalid");

        ValidateExecutablePath(AzureCliPath, "azureCliPath", errors);
        ValidateExecutablePath(SqlCmdPath, "sqlCmdPath", errors);
        ValidateExecutablePath(CurlPath, "curlPath", errors);

        RequireCanonicalGuid(SqlBootstrapObjectId, "sqlBootstrapObjectId", errors);
        if (!Regex.IsMatch(SqlBootstrapLogin ?? "", "^[A-Za-z0-9._@#-]{1,128}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            errors.Add("sqlBootstrapLogin.invalid");

        if (!IPAddress.TryParse(SqlBootstrapIp, out var bootstrapIp) ||
            bootstrapIp.AddressFamily != AddressFamily.InterNetwork ||
            string.Equals(SqlBootstrapIp, "0.0.0.0", StringComparison.Ordinal) ||
            !string.Equals(SqlBootstrapIp, bootstrapIp.ToString(), StringComparison.Ordinal))
            errors.Add("sqlBootstrapIp.invalid");

        if (!Regex.IsMatch(Owner ?? "", "^[a-z0-9][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            errors.Add("owner.invalid");
        if (ExpiryUtc == default)
            errors.Add("expiryUtc.required");

        ValidatePositiveBounded(CommandTimeout, TimeSpan.FromHours(1), "commandTimeout", errors);
        if (MaximumOutputCharacters is < 1024 or > 16_777_216)
            errors.Add("maximumOutputCharacters.invalid");
        if (ObservationAttempts is < 1 or > 120)
            errors.Add("observationAttempts.invalid");
        if (ObservationDelay < TimeSpan.Zero || ObservationDelay > TimeSpan.FromMinutes(1))
            errors.Add("observationDelay.invalid");

        if (!string.Equals(WorkflowUsername, "proof-admin", StringComparison.Ordinal))
            errors.Add("workflowUsername.invalid");
        ValidatePositiveBounded(RequestTimeout, TimeSpan.FromMinutes(5), "requestTimeout", errors);
        ValidatePositiveBounded(WorkflowTimeout, TimeSpan.FromMinutes(30), "workflowTimeout", errors);
        ValidatePositiveBounded(PollInterval, TimeSpan.FromMinutes(1), "pollInterval", errors);
        ValidatePositiveBounded(CleanupTimeout, TimeSpan.FromHours(1), "cleanupTimeout", errors);

        ValidateSecretReferences(SecretReferences, errors);

        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public void EnsureValid()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(", ", errors), nameof(ProofHostOptions));
    }

    public string ToSafeJson()
    {
        EnsureValid();
        var summary = new
        {
            mode = Mode.ToString().ToLowerInvariant(),
            mutationAuthorized = MutationAuthorized,
            workspaceId = WorkspaceId.ToString("D"),
            proofName = ProofName,
            resourceGroupName = ResourceGroupName,
            subscriptionId = SubscriptionId,
            registrySubscriptionId = RegistrySubscriptionId,
            registryResourceGroupName = RegistryResourceGroupName,
            registryName = RegistryName,
            location = Location,
            elsaVersion = ElsaVersion,
            topology = Topology,
            features = Features,
            imageRepository = ImageRepository,
            imageDigest = ImageDigest,
            releaseManifestReference = ReleaseManifestReference,
            releaseManifestDigest = ReleaseManifestDigest,
            releaseManifestSignatureReference = ReleaseManifestSignatureReference,
            releaseManifestSignatureDigest = ReleaseManifestSignatureDigest,
            sourceCommit = SourceCommit,
            statePath = StatePath,
            secretReferenceNames = SecretReferences.Keys.Order(StringComparer.Ordinal).ToArray()
        };
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    private static void Require(string? value, string code, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{code}.required");
    }

    private static void RequireCanonicalGuid(string? value, string code, ICollection<string> errors)
    {
        if (!Guid.TryParseExact(value, "D", out _) ||
            !string.Equals(value, value?.ToLowerInvariant(), StringComparison.Ordinal))
            errors.Add($"{code}.invalid");
    }

    private static bool IsAzureWorkloadName(string? value) =>
        value is { Length: >= 3 and <= 16 } &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        char.IsAsciiLetterOrDigit(value[^1]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool IsAzureResourceGroupName(string? value) =>
        value is { Length: > 0 and <= 90 } &&
        Regex.IsMatch(value, "^[A-Za-z0-9._()\\-]+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static bool IsElsaVersion(string? value) =>
        value is { Length: > 0 and <= 128 } &&
        Regex.IsMatch(value, "^[0-9]+(?:\\.[0-9]+)+(?:[-+][A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static void ValidateFeatures(IReadOnlyList<string>? features, ICollection<string> errors)
    {
        if (features is null || features.Count == 0 || features.Count > 64)
        {
            errors.Add("features.invalid");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in features)
        {
            if (string.IsNullOrWhiteSpace(feature) || feature.Length > 64 ||
                !Regex.IsMatch(feature, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking) ||
                !seen.Add(feature))
                errors.Add("features.invalid");
        }
    }

    private static bool IsSupportedImageRepository(string? value) =>
        value is { Length: > 0 and <= 255 } &&
        value.StartsWith($"{AzureWorkloadPlanTranslator.SupportedRegistryHost}/", StringComparison.Ordinal) &&
        Regex.IsMatch(
            value[$"{AzureWorkloadPlanTranslator.SupportedRegistryHost}/".Length..],
            "^[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)*$",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static bool IsCanonicalSha256(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value[7..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateEvidenceReference(
        string? reference,
        string? digest,
        string name,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(reference) || !IsCanonicalSha256(digest) ||
            !AzureProviderOperationValidation.IsSafeImmutableEvidenceReference(reference, digest))
            errors.Add($"{name}Evidence.invalid");
    }

    private static bool IsSafeAuthorityDirectory(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Any(char.IsControl) ||
                !Path.IsPathFullyQualified(path) || !Directory.Exists(path) || IsReparsePoint(path))
                return false;

            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Take(33).ToArray();
            if (directories.Length > 32)
                return false;
            foreach (var directory in directories)
                if (IsReparsePoint(directory))
                    return false;

            foreach (var required in new[] { "main.bicep", "acr-pull-role.bicep", "sql-bootstrap.sql" })
            {
                var file = Path.GetFullPath(Path.Combine(root, required));
                if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    !File.Exists(file) || IsReparsePoint(file))
                    return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateExecutablePath(string? path, string code, ICollection<string> errors)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(char.IsControl) ||
                !Path.IsPathFullyQualified(path) || !File.Exists(path) || IsReparsePoint(path))
                errors.Add($"{code}.invalid");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            errors.Add($"{code}.invalid");
        }
    }

    private static void ValidateStatePath(string? path, ICollection<string> errors)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Any(char.IsControl) ||
                !Path.IsPathFullyQualified(path) || (File.Exists(path) && IsReparsePoint(path)))
            {
                errors.Add("statePath.invalid");
                return;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || IsReparsePoint(directory))
                errors.Add("statePath.invalid");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            errors.Add("statePath.invalid");
        }
    }

    private static bool IsReparsePoint(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static void ValidatePositiveBounded(TimeSpan value, TimeSpan maximum, string code, ICollection<string> errors)
    {
        if (value <= TimeSpan.Zero || value > maximum || value == Timeout.InfiniteTimeSpan)
            errors.Add($"{code}.invalid");
    }

    private static void ValidateSecretReferences(IReadOnlyDictionary<string, string>? references, ICollection<string> errors)
    {
        if (references is null || references.Count != 3)
        {
            errors.Add("secretReferences.invalid");
            return;
        }

        var expected = new[] { "sql-connection", "identity-signing-key", "admin-password" };
        if (references.Keys.Any(key => !expected.Contains(key, StringComparer.OrdinalIgnoreCase)) ||
            expected.Any(key => !references.ContainsKey(key)) ||
            references.Keys.Any(key => !string.Equals(key, key.Trim().ToLowerInvariant(), StringComparison.Ordinal)))
            errors.Add("secretReferences.invalid");

        foreach (var pair in references)
            if (!AzureProviderOperationValidation.IsSafeSecretReference(pair.Value))
                errors.Add("secretReferences.invalid");
    }
}

public sealed record ProofHostParseResult(
    ProofHostOptions? Options,
    IReadOnlyList<string> Errors,
    bool HelpRequested = false)
{
    public bool Succeeded => Options is not null && Errors.Count == 0;

    public bool MutationGateFailed => Errors.Count == 1 && Errors[0] == "mutationGate.required";
}

/// <summary>
/// Parses the small proof-host CLI without accepting arbitrary environment variables or
/// value-bearing diagnostics. CLI values take precedence over the corresponding known
/// <c>DISPOSABLE_PROOF_*</c> environment variable.
/// </summary>
public static class ProofHostOptionsParser
{
    private const string EnvironmentPrefix = "DISPOSABLE_PROOF_";

    private static readonly IReadOnlyDictionary<string, string> OptionEnvironmentNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mode"] = "MODE",
            ["workspace-id"] = "WORKSPACE_ID",
            ["proof-name"] = "PROOF_NAME",
            ["resource-group"] = "RESOURCE_GROUP",
            ["subscription"] = "SUBSCRIPTION",
            ["registry-subscription"] = "REGISTRY_SUBSCRIPTION",
            ["registry-resource-group"] = "REGISTRY_RESOURCE_GROUP",
            ["registry-name"] = "REGISTRY_NAME",
            ["location"] = "LOCATION",
            ["elsa-version"] = "ELSA_VERSION",
            ["topology"] = "TOPOLOGY",
            ["features"] = "FEATURES",
            ["image-repository"] = "IMAGE_REPOSITORY",
            ["image-digest"] = "IMAGE_DIGEST",
            ["release-manifest-reference"] = "RELEASE_MANIFEST_REFERENCE",
            ["release-manifest-digest"] = "RELEASE_MANIFEST_DIGEST",
            ["release-manifest-signature-reference"] = "RELEASE_MANIFEST_SIGNATURE_REFERENCE",
            ["release-manifest-signature-digest"] = "RELEASE_MANIFEST_SIGNATURE_DIGEST",
            ["source-commit"] = "SOURCE_COMMIT",
            ["state-path"] = "STATE_PATH",
            ["template-root"] = "TEMPLATE_ROOT",
            ["azure-cli-path"] = "AZURE_CLI_PATH",
            ["sqlcmd-path"] = "SQLCMD_PATH",
            ["curl-path"] = "CURL_PATH",
            ["sql-bootstrap-object-id"] = "SQL_BOOTSTRAP_OBJECT_ID",
            ["sql-bootstrap-login"] = "SQL_BOOTSTRAP_LOGIN",
            ["sql-bootstrap-ip"] = "SQL_BOOTSTRAP_IP",
            ["owner"] = "OWNER",
            ["expiry-utc"] = "EXPIRY_UTC",
            ["command-timeout"] = "COMMAND_TIMEOUT",
            ["maximum-output-characters"] = "MAXIMUM_OUTPUT_CHARACTERS",
            ["observation-attempts"] = "OBSERVATION_ATTEMPTS",
            ["observation-delay"] = "OBSERVATION_DELAY",
            ["workflow-username"] = "WORKFLOW_USERNAME",
            ["request-timeout"] = "REQUEST_TIMEOUT",
            ["workflow-timeout"] = "WORKFLOW_TIMEOUT",
            ["poll-interval"] = "POLL_INTERVAL",
            ["cleanup-timeout"] = "CLEANUP_TIMEOUT",
            ["sql-connection-reference"] = "SQL_CONNECTION_REFERENCE",
            ["identity-signing-key-reference"] = "IDENTITY_SIGNING_KEY_REFERENCE",
            ["admin-password-reference"] = "ADMIN_PASSWORD_REFERENCE"
        };

    private static readonly IReadOnlySet<string> AllowedEnvironmentNames =
        OptionEnvironmentNames.Values
            .Append("APPLY")
            .Select(value => EnvironmentPrefix + value)
            .ToHashSet(StringComparer.Ordinal);

    public static ProofHostParseResult Parse(
        IEnumerable<string>? arguments,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var errors = new List<string>();
        var args = arguments?.ToArray() ?? [];
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var helpRequested = false;

        if (environment is not null)
        {
            foreach (var key in environment.Keys.Where(key => key.StartsWith(EnvironmentPrefix, StringComparison.Ordinal)))
            {
                if (!AllowedEnvironmentNames.Contains(key))
                    errors.Add("environment.unknown");
            }

            foreach (var option in OptionEnvironmentNames)
            {
                var key = EnvironmentPrefix + option.Value;
                if (environment.TryGetValue(key, out var value))
                    values[option.Key] = value;
            }
        }

        var index = 0;
        if (args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            values["mode"] = args[0];
            seen.Add("mode");
            index = 1;
        }

        while (index < args.Length)
        {
            var argument = args[index++];
            if (argument is "-h" or "--help")
            {
                helpRequested = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal) ||
                !OptionEnvironmentNames.ContainsKey(argument[2..]))
            {
                errors.Add("argument.unknown");
                if (index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal))
                    index++;
                continue;
            }

            var option = argument[2..];
            if (!seen.Add(option))
            {
                errors.Add("argument.duplicate");
                if (index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal))
                    index++;
                continue;
            }

            if (index >= args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
            {
                errors.Add("argument.valueRequired");
                continue;
            }

            values[option] = args[index++];
        }

        var mode = ParseMode(values.GetValueOrDefault("mode"), errors);
        var features = ParseFeatures(values.GetValueOrDefault("features"), errors);
        var secrets = ParseSecrets(values, errors);
        var workspaceIdText = Value(values, "workspace-id");
        var workspaceId = ParseGuid(workspaceIdText);
        if (!string.IsNullOrWhiteSpace(workspaceIdText) && workspaceId == Guid.Empty)
            errors.Add("workspaceId.invalid");

        var options = new ProofHostOptions
        {
            Mode = mode,
            MutationAuthorized = environment?.TryGetValue(EnvironmentPrefix + "APPLY", out var gate) == true && gate == "YES",
            WorkspaceId = workspaceId,
            ProofName = Value(values, "proof-name"),
            ResourceGroupName = Value(values, "resource-group"),
            SubscriptionId = Value(values, "subscription"),
            RegistrySubscriptionId = Value(values, "registry-subscription") is { Length: > 0 } registrySubscription
                ? registrySubscription
                : Value(values, "subscription"),
            RegistryResourceGroupName = Value(values, "registry-resource-group"),
            RegistryName = Value(values, "registry-name", "valenceruntimeimages"),
            Location = Value(values, "location", AzureWorkloadPlanTranslator.SupportedLocation),
            ElsaVersion = Value(values, "elsa-version"),
            Topology = Value(values, "topology", AzureWorkloadPlanTranslator.SupportedTopology),
            Features = features,
            ImageRepository = Value(values, "image-repository", "valenceruntimeimages.azurecr.io/runtime-combined"),
            ImageDigest = Value(values, "image-digest"),
            ReleaseManifestReference = Value(values, "release-manifest-reference"),
            ReleaseManifestDigest = Value(values, "release-manifest-digest"),
            ReleaseManifestSignatureReference = Value(values, "release-manifest-signature-reference"),
            ReleaseManifestSignatureDigest = Value(values, "release-manifest-signature-digest"),
            SourceCommit = Value(values, "source-commit"),
            StatePath = Value(values, "state-path"),
            TemplateRoot = Value(values, "template-root"),
            AzureCliPath = Value(values, "azure-cli-path"),
            SqlCmdPath = Value(values, "sqlcmd-path"),
            CurlPath = Value(values, "curl-path"),
            SqlBootstrapObjectId = Value(values, "sql-bootstrap-object-id"),
            SqlBootstrapLogin = Value(values, "sql-bootstrap-login"),
            SqlBootstrapIp = Value(values, "sql-bootstrap-ip"),
            Owner = Value(values, "owner", "elsa-control"),
            ExpiryUtc = ParseDate(values.GetValueOrDefault("expiry-utc"), errors),
            CommandTimeout = ParseTimeSpan(values.GetValueOrDefault("command-timeout"), TimeSpan.FromMinutes(15), "commandTimeout", errors),
            MaximumOutputCharacters = ParseInt(values.GetValueOrDefault("maximum-output-characters"), 1_048_576, "maximumOutputCharacters", errors),
            ObservationAttempts = ParseInt(values.GetValueOrDefault("observation-attempts"), 60, "observationAttempts", errors),
            ObservationDelay = ParseTimeSpan(values.GetValueOrDefault("observation-delay"), TimeSpan.FromSeconds(5), "observationDelay", errors),
            WorkflowUsername = Value(values, "workflow-username", "proof-admin"),
            RequestTimeout = ParseTimeSpan(values.GetValueOrDefault("request-timeout"), TimeSpan.FromSeconds(30), "requestTimeout", errors),
            WorkflowTimeout = ParseTimeSpan(values.GetValueOrDefault("workflow-timeout"), TimeSpan.FromMinutes(2), "workflowTimeout", errors),
            PollInterval = ParseTimeSpan(values.GetValueOrDefault("poll-interval"), TimeSpan.FromSeconds(2), "pollInterval", errors),
            CleanupTimeout = ParseTimeSpan(values.GetValueOrDefault("cleanup-timeout"), TimeSpan.FromMinutes(20), "cleanupTimeout", errors),
            SecretReferences = secrets
        };

        if (!helpRequested)
            errors.AddRange(options.Validate());

        return new(options, errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), helpRequested);
    }

    public static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value)
                environment[key] = value;
        return environment;
    }

    private static string Value(IReadOnlyDictionary<string, string?> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) ? value ?? "" : fallback;

    private static Guid ParseGuid(string? value) => Guid.TryParseExact(value, "D", out var result) ? result : Guid.Empty;

    private static ProofHostMode ParseMode(string? value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("mode.required");
            return ProofHostMode.Validate;
        }

        if (!Enum.TryParse<ProofHostMode>(value, true, out var mode) || !Enum.IsDefined(mode))
        {
            errors.Add("mode.invalid");
            return ProofHostMode.Validate;
        }

        return mode;
    }

    private static IReadOnlyList<string> ParseFeatures(string? value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var rawFeatures = value.Split(',', StringSplitOptions.None);
        var features = rawFeatures.Select(feature => feature.Trim()).ToArray();
        if (features.Length == 0 || features.Any(string.IsNullOrWhiteSpace))
            errors.Add("features.invalid");
        return features;
    }

    private static IReadOnlyDictionary<string, string> ParseSecrets(
        IReadOnlyDictionary<string, string?> values,
        ICollection<string> errors)
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sql-connection"] = Value(values, "sql-connection-reference"),
            ["identity-signing-key"] = Value(values, "identity-signing-key-reference"),
            ["admin-password"] = Value(values, "admin-password-reference")
        };
        if (secrets.Values.Any(string.IsNullOrWhiteSpace))
            errors.Add("secretReferences.required");
        return secrets;
    }

    private static DateOnly ParseDate(string? value, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("expiryUtc.required");
            return default;
        }

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add("expiryUtc.invalid");
            return default;
        }

        return date;
    }

    private static TimeSpan ParseTimeSpan(string? value, TimeSpan fallback, string code, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"{code}.invalid");
            return fallback;
        }

        return parsed;
    }

    private static int ParseInt(string? value, int fallback, string code, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"{code}.invalid");
            return fallback;
        }

        return parsed;
    }
}
