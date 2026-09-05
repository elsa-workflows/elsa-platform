namespace ElsaControl.Api.ReleaseCatalog;

/// <summary>Host-only verification of the exact downloaded subject and retained bundle.</summary>
internal interface IReleaseManifestBundleVerifier
{
    ValueTask<bool> VerifyAsync(
        ReadOnlyMemory<byte> subject,
        ReadOnlyMemory<byte> bundle,
        CancellationToken cancellationToken = default);
}

/// <summary>Immutable server-owned tool and trust policy, never populated from an HTTP request.</summary>
internal sealed record SigstoreBundleVerificationAuthority(
    string ExecutablePath,
    string ExecutableSha256,
    string TrustedRootPath,
    string TrustedRootSha256,
    string CertificateIdentity,
    string OidcIssuer,
    TimeSpan Timeout,
    int MaximumOutputCharacters = 16_384);
