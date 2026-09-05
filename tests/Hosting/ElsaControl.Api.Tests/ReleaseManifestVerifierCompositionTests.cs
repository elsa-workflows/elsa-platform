using System.Security.Cryptography;
using ElsaControl.Api.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class ReleaseManifestVerifierCompositionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "elsa-verifier-composition-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Default_configuration_preserves_fail_closed_verification()
    {
        var services = new ServiceCollection();
        ReleaseManifestVerifierComposition.AddVerifier(services, Configuration());
        await using var provider = services.BuildServiceProvider();
        var verifier = provider.GetRequiredService<IReleaseManifestSignatureVerifier>();
        var result = await verifier.VerifyAsync(new("oci://example.invalid/a@sha256:" + new string('a', 64), "sha256:" + new string('a', 64), "{}"));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Explicitly_enabled_but_incomplete_authority_rejects_startup_without_values()
    {
        var configuration = Configuration(new()
        {
            ["ReleaseCatalog:Verification:Enabled"] = "true",
            ["ReleaseCatalog:Verification:RegistryHost"] = "untrusted-secret.example"
        });
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseManifestVerifierComposition.AddVerifier(new ServiceCollection(), configuration));
        Assert.DoesNotContain("untrusted-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_pinned_authority_composes_only_the_configured_verifier()
    {
        var services = new ServiceCollection();
        ReleaseManifestVerifierComposition.AddVerifier(services, Configuration(ValidConfiguration()));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<ConfiguredAcrReleaseManifestSignatureVerifier>(provider.GetRequiredService<IReleaseManifestSignatureVerifier>());
        Assert.IsType<AcrReleaseRegistryReader>(provider.GetRequiredService<IReleaseRegistryReader>());
        Assert.IsType<SigstoreReleaseManifestBundleVerifier>(provider.GetRequiredService<IReleaseManifestBundleVerifier>());
    }

    [Fact]
    public void Registry_transport_does_not_apply_global_discovery_or_resilience_handlers()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => builder.AddHttpMessageHandler(() =>
            throw new InvalidOperationException("Global transport rewriting must never be used.")));
        ReleaseManifestVerifierComposition.AddVerifier(services, Configuration(ValidConfiguration()));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<AcrReleaseRegistryReader>(provider.GetRequiredService<IReleaseRegistryReader>());
    }

    [Theory]
    [InlineData("ReleaseCatalog:Verification:Enabled", "invalid-secret")]
    [InlineData("ReleaseCatalog:Admission:ExpectedSignatureSubject", "")]
    [InlineData("ReleaseCatalog:Admission:ExpectedOidcIssuer", "")]
    [InlineData("ReleaseCatalog:Verification:ManagedIdentityClientId", "")]
    [InlineData("ReleaseCatalog:Verification:RegistryHost", "https://untrusted-secret.example")]
    [InlineData("ReleaseCatalog:Verification:CosignSha256", "invalid")]
    [InlineData("ReleaseCatalog:Verification:RequestTimeoutSeconds", "0")]
    public void Invalid_authority_rejects_before_credential_or_network_use(string key, string value)
    {
        var values = ValidConfiguration();
        values[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReleaseManifestVerifierComposition.AddVerifier(new ServiceCollection(), Configuration(values)));
        Assert.Equal("Release manifest verification authority is invalid.", error.Message);
        Assert.Null(error.InnerException);
    }

    private Dictionary<string, string?> ValidConfiguration()
    {
        Directory.CreateDirectory(_directory);
        var executable = Path.Combine(_directory, "tool");
        var root = Path.Combine(_directory, "trusted-root.json");
        File.WriteAllText(executable, "test tool; never executed by composition");
        File.WriteAllText(root, "{}");
        return new()
        {
            ["ReleaseCatalog:Verification:Enabled"] = "true",
            ["ReleaseCatalog:Verification:RegistryHost"] = "demo123.azurecr.io",
            ["ReleaseCatalog:Verification:Repository"] = "release-manifests/release-manifest",
            ["ReleaseCatalog:Verification:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["ReleaseCatalog:Verification:ManagedIdentityClientId"] = "22222222-2222-2222-2222-222222222222",
            ["ReleaseCatalog:Verification:CosignPath"] = executable,
            ["ReleaseCatalog:Verification:CosignSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(executable))),
            ["ReleaseCatalog:Verification:TrustedRootPath"] = root,
            ["ReleaseCatalog:Verification:TrustedRootSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(root))),
            ["ReleaseCatalog:Admission:ExpectedSignatureSubject"] = "https://github.com/example/producer/.github/workflows/release.yml@refs/heads/main",
            ["ReleaseCatalog:Admission:ExpectedOidcIssuer"] = "https://token.actions.githubusercontent.com"
        };
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? []).Build();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
