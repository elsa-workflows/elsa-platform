using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElsaControl.Deployment.Azure;

/// <summary>
/// Explicit immutable Azure placement authority for one provider operation. Its fingerprint is
/// persisted with the operation so a restarted worker cannot silently move the target or registry.
/// </summary>
public sealed record AzureProviderTargetScope(
    string SubscriptionId,
    string ResourceGroupName,
    string RegistrySubscriptionId,
    string RegistryResourceGroupName,
    string RegistryName,
    string Location)
{
    public const string ConfigurationSection = "Deployment:AzureProvider:Runner:TargetScope";
    public string ComputeFingerprint()
    {
        Validate();
        var canonical = JsonSerializer.Serialize(new
        {
            subscriptionId = SubscriptionId.ToLowerInvariant(),
            resourceGroupName = ResourceGroupName.ToLowerInvariant(),
            registrySubscriptionId = RegistrySubscriptionId.ToLowerInvariant(),
            registryResourceGroupName = RegistryResourceGroupName.ToLowerInvariant(),
            registryName = RegistryName.ToLowerInvariant(),
            location = Location.Trim().ToLowerInvariant()
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public void Validate()
    {
        ValidateGuid(SubscriptionId, nameof(SubscriptionId));
        ValidateGuid(RegistrySubscriptionId, nameof(RegistrySubscriptionId));
        ValidateResourceGroup(ResourceGroupName, nameof(ResourceGroupName));
        ValidateResourceGroup(RegistryResourceGroupName, nameof(RegistryResourceGroupName));
        if (!Regex.IsMatch(RegistryName ?? "", "^[a-z0-9]{5,50}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The Azure registry name is unsafe.", nameof(RegistryName));
        if (!AzureWorkloadPlanTranslator.IsSupportedLocation(Location))
            throw new ArgumentException("The Azure location is outside the governed provider profile.", nameof(Location));
    }

    private static void ValidateGuid(string? value, string name)
    {
        if (!Guid.TryParseExact(value, "D", out _) || !string.Equals(value, value?.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("The Azure subscription ID must be a canonical GUID.", name);
    }

    private static void ValidateResourceGroup(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 90 ||
            !Regex.IsMatch(value, "^[A-Za-z0-9._()\\-]+\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The Azure resource group name is unsafe.", name);
    }
}

/// <summary>
/// Hardened local tooling and policy options for the checked-in Azure Bicep lifecycle.
/// Construction does not enable the provider; hosts must validate these options explicitly.
/// </summary>
public sealed record AzureProviderRunnerOptions
{
    public const string ConfigurationSection = "Deployment:AzureProvider:Runner";
    public bool Enabled { get; init; }
    /// <summary>Absolute Azure CLI executable bound into provider authority.</summary>
    public string AzureCliPath { get; init; } = "";
    /// <summary>Required user-assigned managed identity client ID used by Azure CLI login.</summary>
    public string? AzureCliClientId { get; init; }
    /// <summary>Absolute sqlcmd executable bound into provider authority.</summary>
    public string SqlCmdPath { get; init; } = "";
    /// <summary>Absolute curl executable used for the post-promotion health probe.</summary>
    public string CurlPath { get; init; } = "";
    public string TemplateRoot { get; init; } = "";
    public string SqlBootstrapObjectId { get; init; } = "";
    public string SqlBootstrapLogin { get; init; } = "";
    public string SqlBootstrapIp { get; init; } = "";
    public string Owner { get; init; } = "elsa-control";
    public DateOnly ExpiryUtc { get; init; }
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public int MaximumOutputCharacters { get; init; } = 1_048_576;
    public int ObservationAttempts { get; init; } = 60;
    public TimeSpan ObservationDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Binds the durable operation to every safe configuration value that selects or authorizes
    /// remote Azure mutation. Template contents are bound separately by TemplateFingerprint.
    /// </summary>
    public string ComputeProviderScopeFingerprint(AzureProviderTargetScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Validate();
        scope.Validate();
        var canonical = JsonSerializer.Serialize(new
        {
            targetScopeFingerprint = scope.ComputeFingerprint(),
            azureCliPath = Path.GetFullPath(AzureCliPath),
            azureCliDigest = ComputeFileDigest(AzureCliPath),
            azureCliClientId = AzureCliClientId?.ToLowerInvariant(),
            sqlCmdPath = Path.GetFullPath(SqlCmdPath),
            sqlCmdDigest = ComputeFileDigest(SqlCmdPath),
            curlPath = Path.GetFullPath(CurlPath),
            curlDigest = ComputeFileDigest(CurlPath),
            templateRoot = NormalizeRoot(TemplateRoot),
            templateAuthorityFingerprint = ComputeTemplateAuthorityFingerprint(),
            sqlBootstrapObjectId = SqlBootstrapObjectId.ToLowerInvariant(),
            sqlBootstrapLogin = SqlBootstrapLogin,
            sqlBootstrapIp = SqlBootstrapIp,
            owner = Owner.ToLowerInvariant(),
            expiryUtc = ExpiryUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>
    /// Recomputes every local and remote mutation authority immediately before execution.
    /// Legacy durable rows may omit a scope fingerprint; the concrete runner never may.
    /// </summary>
    public void ValidateExecutionAuthority(
        AzureProviderExecutionContext context,
        AzureProviderTargetScope scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        var actual = context.ProviderScopeFingerprint;
        var expected = ComputeProviderScopeFingerprint(scope);
        if (actual is null || actual.Length != expected.Length ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(expected)))
            throw new InvalidOperationException("The Azure runner authority does not match the durable operation.");
    }

    public void Validate()
    {
        if (!Enabled)
            throw new InvalidOperationException("The concrete Azure provider runner is not enabled.");
        ValidateExecutable(AzureCliPath, nameof(AzureCliPath));
        if (!Guid.TryParseExact(AzureCliClientId, "D", out _) ||
            !string.Equals(AzureCliClientId, AzureCliClientId?.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("The Azure CLI managed identity client ID must be a canonical GUID.", nameof(AzureCliClientId));
        ValidateExecutable(SqlCmdPath, nameof(SqlCmdPath));
        ValidateExecutable(CurlPath, nameof(CurlPath));
        if (string.IsNullOrWhiteSpace(TemplateRoot) || !Path.IsPathFullyQualified(TemplateRoot))
            throw new ArgumentException("The Azure template root must be an absolute path.", nameof(TemplateRoot));
        var normalizedRoot = NormalizeRoot(TemplateRoot);
        if (!Directory.Exists(normalizedRoot) || IsSymbolicLink(normalizedRoot))
            throw new ArgumentException("The Azure template root must be a regular trusted directory.", nameof(TemplateRoot));
        RequireCheckedInFile(normalizedRoot, "main.bicep");
        RequireCheckedInFile(normalizedRoot, "acr-pull-role.bicep");
        RequireCheckedInFile(normalizedRoot, "sql-bootstrap.sql");
        _ = ComputeTemplateAuthorityFingerprint(normalizedRoot);
        if (!Guid.TryParseExact(SqlBootstrapObjectId, "D", out _) ||
            !string.Equals(SqlBootstrapObjectId, SqlBootstrapObjectId.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("The SQL bootstrap object ID must be a canonical GUID.", nameof(SqlBootstrapObjectId));
        if (!Regex.IsMatch(SqlBootstrapLogin ?? "", "^[A-Za-z0-9._@#-]{1,128}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The SQL bootstrap login is unsafe.", nameof(SqlBootstrapLogin));
        if (!System.Net.IPAddress.TryParse(SqlBootstrapIp, out var ip) ||
            ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            SqlBootstrapIp == "0.0.0.0" ||
            !string.Equals(SqlBootstrapIp, ip.ToString(), StringComparison.Ordinal))
            throw new ArgumentException("The SQL bootstrap address must be one exact non-zero IPv4 address.", nameof(SqlBootstrapIp));
        if (!Regex.IsMatch(Owner ?? "", "^[a-z0-9][a-z0-9-]{0,62}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The Azure owner tag is unsafe.", nameof(Owner));
        if (ExpiryUtc == default)
            throw new ArgumentException("A disposable Azure expiry date is required.", nameof(ExpiryUtc));
        if (CommandTimeout <= TimeSpan.Zero || CommandTimeout > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(CommandTimeout), "The command timeout must be positive and no longer than one hour.");
        if (MaximumOutputCharacters is < 1024 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumOutputCharacters), "The command output cap is outside the governed range.");
        if (ObservationAttempts is < 1 or > 120)
            throw new ArgumentOutOfRangeException(nameof(ObservationAttempts));
        if (ObservationDelay < TimeSpan.Zero || ObservationDelay > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(ObservationDelay));
    }

    private static void ValidateExecutable(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(value) || !File.Exists(value) || IsSymbolicLink(value))
            throw new ArgumentException("The executable locator is unsafe.", name);
    }

    public string ComputeTemplateAuthorityFingerprint()
    {
        if (string.IsNullOrWhiteSpace(TemplateRoot) || !Path.IsPathFullyQualified(TemplateRoot))
            throw new ArgumentException("The Azure template root must be an absolute path.", nameof(TemplateRoot));
        return ComputeTemplateAuthorityFingerprint(NormalizeRoot(TemplateRoot));
    }

    private static string ComputeTemplateAuthorityFingerprint(string root)
    {
        var directories = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Take(33).ToArray()
            : [];
        if (!Directory.Exists(root) || IsSymbolicLink(root) || directories.Length > 32 || directories.Any(IsSymbolicLink))
            throw new ArgumentException("The Azure template root must be a regular trusted directory.", nameof(TemplateRoot));

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(257).ToArray();
        if (files.Length > 256)
            throw new ArgumentException("The checked-in Azure provider authority is outside the governed bounds.", nameof(TemplateRoot));
        var authorityFiles = files
            .Where(path => string.Equals(Path.GetExtension(path), ".bicep", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (authorityFiles.Length is 0 or > 64 || authorityFiles.Any(IsSymbolicLink))
            throw new ArgumentException("The checked-in Azure provider authority is incomplete.", nameof(TemplateRoot));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in authorityFiles)
        {
            var relativePath = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relativePath.StartsWith("../", StringComparison.Ordinal))
                throw new ArgumentException("The checked-in Azure provider authority is incomplete.", nameof(TemplateRoot));
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData([0]);
            AppendFileContents(hash, path);
            hash.AppendData([0]);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void RequireCheckedInFile(string root, string name)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path) ||
            IsSymbolicLink(path))
            throw new ArgumentException("The checked-in Azure provider authority is incomplete.", nameof(TemplateRoot));
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Attribute races, access failures and disappearing authority files are all unsafe.
            return true;
        }
    }

    private static string NormalizeRoot(string root) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    private static string ComputeFileDigest(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void AppendFileContents(IncrementalHash hash, string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer, 0, read);
    }
}
