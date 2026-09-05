using Azure.Identity;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Api.ReleaseCatalog;

internal static class ReleaseManifestVerifierComposition
{
    internal const string ConfigurationSection = "ReleaseCatalog:Verification";

    internal static void AddVerifier(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var enabledValue = section["Enabled"];
        var enabled = false;
        if (enabledValue is not null && !bool.TryParse(enabledValue, out enabled))
            throw InvalidAuthority();
        if (!enabled)
        {
            services.AddScoped<IReleaseManifestSignatureVerifier, FailClosedReleaseManifestSignatureVerifier>();
            return;
        }

        AcrReleaseRegistryAuthority registry;
        SigstoreReleaseManifestBundleVerifier bundleVerifier;
        string identity;
        string issuer;
        try
        {
            var options = section.Get<ReleaseManifestVerifierOptions>() ?? new();
            var policy = configuration.GetSection(ReleaseCatalogAdmissionOptions.ConfigurationSection)
                .Get<ReleaseCatalogAdmissionOptions>() ?? new();
            identity = policy.ExpectedSignatureSubject?.Trim() ?? "";
            issuer = policy.ExpectedOidcIssuer?.Trim() ?? "";
            registry = new(options.RegistryHost, options.Repository, options.TenantId,
                options.ManagedIdentityClientId, options.BlobRedirectHosts,
                TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            AcrReleaseRegistryReader.ValidateAuthority(registry);
            bundleVerifier = new(new(options.CosignPath, options.CosignSha256,
                options.TrustedRootPath, options.TrustedRootSha256,
                identity, issuer,
                TimeSpan.FromSeconds(options.VerificationTimeoutSeconds)));
        }
        catch (Exception)
        {
            // Configuration and file-system exceptions can contain locators or host paths.
            // Reject startup using one value-free error, with no raw inner exception.
            throw InvalidAuthority();
        }

        services.AddSingleton<IReleaseManifestBundleVerifier>(bundleVerifier);
        services.AddSingleton<IReleaseRegistryReader>(provider =>
            new AcrReleaseRegistryReader(registry,
                new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(registry.ManagedIdentityClientId)),
                provider.GetRequiredKeyedService<HttpClient>(HttpClientName)));
        // This DI-owned client deliberately bypasses global discovery, retries and
        // logging handlers, which could rewrite the pinned authority or retain SAS URLs.
        services.AddKeyedSingleton<HttpClient>(HttpClientName, (_, _) =>
            new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                ActivityHeadersPropagator = null,
                ConnectTimeout = registry.RequestTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }) { Timeout = Timeout.InfiniteTimeSpan });
        services.AddScoped<IReleaseManifestSignatureVerifier>(provider =>
            new ConfiguredAcrReleaseManifestSignatureVerifier(registry,
                provider.GetRequiredService<IReleaseRegistryReader>(),
                provider.GetRequiredService<IReleaseManifestBundleVerifier>(),
                identity, issuer));
    }

    private const string HttpClientName = "release-manifest-registry";

    private static InvalidOperationException InvalidAuthority() =>
        new("Release manifest verification authority is invalid.");

    private sealed class ReleaseManifestVerifierOptions
    {
        public string RegistryHost { get; set; } = "";
        public string Repository { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string ManagedIdentityClientId { get; set; } = "";
        public string[] BlobRedirectHosts { get; set; } = [];
        public string CosignPath { get; set; } = "";
        public string CosignSha256 { get; set; } = "";
        public string TrustedRootPath { get; set; } = "";
        public string TrustedRootSha256 { get; set; } = "";
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int VerificationTimeoutSeconds { get; set; } = 60;
    }
}
