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
            location = Location.ToLowerInvariant()
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
        if (!string.Equals(Location, AzureWorkloadPlanTranslator.SupportedLocation, StringComparison.Ordinal))
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
    public bool Enabled { get; init; }
    public string AzureCliPath { get; init; } = "az";
    public string SqlCmdPath { get; init; } = "sqlcmd";
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
            sqlBootstrapObjectId = SqlBootstrapObjectId.ToLowerInvariant(),
            sqlBootstrapLogin = SqlBootstrapLogin,
            sqlBootstrapIp = SqlBootstrapIp,
            owner = Owner.ToLowerInvariant(),
            expiryUtc = ExpiryUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public void Validate()
    {
        if (!Enabled)
            throw new InvalidOperationException("The concrete Azure provider runner is not enabled.");
        ValidateExecutable(AzureCliPath, nameof(AzureCliPath));
        ValidateExecutable(SqlCmdPath, nameof(SqlCmdPath));
        if (string.IsNullOrWhiteSpace(TemplateRoot) || !Path.IsPathFullyQualified(TemplateRoot))
            throw new ArgumentException("The Azure template root must be an absolute path.", nameof(TemplateRoot));
        var normalizedRoot = Path.GetFullPath(TemplateRoot);
        if (!Directory.Exists(normalizedRoot) || IsSymbolicLink(normalizedRoot))
            throw new ArgumentException("The Azure template root must be a regular trusted directory.", nameof(TemplateRoot));
        RequireCheckedInFile(normalizedRoot, "main.bicep");
        RequireCheckedInFile(normalizedRoot, "acr-pull-role.bicep");
        RequireCheckedInFile(normalizedRoot, "sql-bootstrap.sql");
        if (!Guid.TryParseExact(SqlBootstrapObjectId, "D", out _) ||
            !string.Equals(SqlBootstrapObjectId, SqlBootstrapObjectId.ToLowerInvariant(), StringComparison.Ordinal))
            throw new ArgumentException("The SQL bootstrap object ID must be a canonical GUID.", nameof(SqlBootstrapObjectId));
        if (!Regex.IsMatch(SqlBootstrapLogin ?? "", "^[A-Za-z0-9._@-]{1,128}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            throw new ArgumentException("The SQL bootstrap login is unsafe.", nameof(SqlBootstrapLogin));
        if (!System.Net.IPAddress.TryParse(SqlBootstrapIp, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || SqlBootstrapIp == "0.0.0.0")
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
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl))
            throw new ArgumentException("The executable locator is unsafe.", name);
    }

    private static void RequireCheckedInFile(string root, string name)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path) ||
            IsSymbolicLink(path))
            throw new ArgumentException("The checked-in Azure provider authority is incomplete.", nameof(TemplateRoot));
    }

    private static bool IsSymbolicLink(string path) =>
        File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
