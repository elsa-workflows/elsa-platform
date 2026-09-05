namespace ElsaControl.Api.ReleaseCatalog;

internal interface IReleaseRegistryReader
{
    ValueTask<IReleaseRegistrySession> OpenAsync(CancellationToken cancellationToken = default);
}

internal interface IReleaseRegistrySession : IAsyncDisposable
{
    ValueTask<byte[]> ReadManifestAsync(string digest, CancellationToken cancellationToken = default);
    ValueTask<byte[]> ReadBlobAsync(string digest, int maximumBytes, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ReleaseRegistryDescriptor>> ReadReferrersAsync(string subjectDigest, CancellationToken cancellationToken = default);
}

internal sealed record ReleaseRegistryDescriptor(string MediaType, string Digest, long Size, string? ArtifactType);

internal sealed record AcrReleaseRegistryAuthority(
    string RegistryHost,
    string Repository,
    string TenantId,
    string ManagedIdentityClientId,
    IReadOnlyList<string> BlobRedirectHosts,
    TimeSpan RequestTimeout);

internal static class ReleaseRegistryProtocol
{
    public const string ManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    public const string IndexMediaType = "application/vnd.oci.image.index.v1+json";
    public const string ReleaseArtifactType = "application/vnd.valence.release-manifest.v2+json";
    public const string BundleMediaType = "application/vnd.dev.sigstore.bundle.v0.3+json";
    public const int MaximumManifestBytes = 4 * 1024 * 1024;
    public const int MaximumBundleBytes = 256 * 1024;

    public static bool IsDigest(string? value) =>
        value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;
}
