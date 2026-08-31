using System.Text.Json;
using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Azure.Tests;

public sealed class AzureProviderRunnerOptionsTests : IDisposable
{
    private readonly string _templateRoot = Path.Combine(Path.GetTempPath(), $"elsa-azure-options-{Guid.NewGuid():N}");

    public AzureProviderRunnerOptionsTests()
    {
        Directory.CreateDirectory(_templateRoot);
        File.WriteAllText(Path.Combine(_templateRoot, "main.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_templateRoot, "acr-pull-role.bicep"), "targetScope = 'resourceGroup'");
        File.WriteAllText(Path.Combine(_templateRoot, "sql-bootstrap.sql"), "SELECT 1;");
    }

    [Fact]
    public void Validates_explicit_governed_runner_options()
    {
        ValidOptions().Validate();
    }

    [Fact]
    public void Default_options_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => new AzureProviderRunnerOptions().Validate());
    }

    [Fact]
    public void Rejects_a_symbolic_link_as_template_authority()
    {
        if (OperatingSystem.IsWindows())
            return;

        var link = _templateRoot + "-link";
        Directory.CreateSymbolicLink(link, _templateRoot);
        try
        {
            Assert.Throws<ArgumentException>(() => (ValidOptions() with { TemplateRoot = link }).Validate());
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.0/24")]
    [InlineData("not-an-ip")]
    public void Rejects_broad_or_invalid_sql_bootstrap_addresses(string address)
    {
        Assert.Throws<ArgumentException>(() => (ValidOptions() with { SqlBootstrapIp = address }).Validate());
    }

    [Fact]
    public void Target_scope_fingerprint_is_canonical_stable_and_scope_sensitive()
    {
        var scope = ValidScope();
        var same = scope with { ResourceGroupName = scope.ResourceGroupName.ToUpperInvariant() };
        var changed = scope with { RegistryResourceGroupName = "other-registry-rg" };

        Assert.Equal(scope.ComputeFingerprint(), same.ComputeFingerprint());
        Assert.NotEqual(scope.ComputeFingerprint(), changed.ComputeFingerprint());
        Assert.Equal(64, scope.ComputeFingerprint().Length);
    }

    [Fact]
    public void Provider_scope_fingerprint_binds_bootstrap_authority_and_expiry()
    {
        var options = ValidOptions();
        var scope = ValidScope();

        Assert.NotEqual(
            options.ComputeProviderScopeFingerprint(scope),
            (options with { SqlBootstrapIp = "203.0.113.11" }).ComputeProviderScopeFingerprint(scope));
        Assert.NotEqual(
            options.ComputeProviderScopeFingerprint(scope),
            (options with { ExpiryUtc = options.ExpiryUtc.AddDays(1) }).ComputeProviderScopeFingerprint(scope));
    }

    [Fact]
    public void Secret_lease_is_value_free_in_text_and_json_and_unavailable_after_disposal()
    {
        var lease = new AzureSecretLease("sensitive-value");

        Assert.Equal("sensitive-value", lease.Value.ToString());
        Assert.Equal(nameof(AzureSecretLease), lease.ToString());
        var exception = Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(lease));
        Assert.DoesNotContain("sensitive-value", exception.Message, StringComparison.Ordinal);
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Value);
    }

    [Theory]
    [InlineData("secret://vault/database", true)]
    [InlineData("secret://user:password@vault/database", false)]
    [InlineData("file:///tmp/secret", false)]
    public void Secret_resolution_request_accepts_only_approved_opaque_locators(string reference, bool accepted)
    {
        var request = new AzureSecretResolutionRequest(Guid.NewGuid(), "database", reference);
        if (accepted)
            request.Validate();
        else
            Assert.Throws<ArgumentException>(request.Validate);
    }

    [Fact]
    public async Task Unconfigured_secret_resolver_fails_closed()
    {
        var resolver = new UnconfiguredAzureSecretResolver();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await resolver.ResolveAsync(new(Guid.NewGuid(), "database", "secret://vault/database")));
    }

    public void Dispose() => Directory.Delete(_templateRoot, recursive: true);

    private AzureProviderRunnerOptions ValidOptions() => new()
    {
        Enabled = true,
        AzureCliPath = "az",
        SqlCmdPath = "sqlcmd",
        TemplateRoot = _templateRoot,
        SqlBootstrapObjectId = "11111111-1111-1111-1111-111111111111",
        SqlBootstrapLogin = "proof-bootstrap",
        SqlBootstrapIp = "203.0.113.10",
        ExpiryUtc = new DateOnly(2026, 9, 2)
    };

    private static AzureProviderTargetScope ValidScope() => new(
        "11111111-1111-1111-1111-111111111111",
        "proof-rg",
        "22222222-2222-2222-2222-222222222222",
        "registry-rg",
        "valenceruntimeimages",
        "westeurope");
}
