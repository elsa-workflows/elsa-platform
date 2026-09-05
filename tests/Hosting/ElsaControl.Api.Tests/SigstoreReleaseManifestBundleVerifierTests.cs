using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ElsaControl.Api.ReleaseCatalog;

namespace ElsaControl.Api.Tests;

public sealed class SigstoreReleaseManifestBundleVerifierTests
{
    [Fact]
    public async Task Verifies_the_exact_subject_with_the_pinned_cosign_arguments_and_cleans_up()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var marker = fixture.Path("invocation");
        var executable = fixture.Executable($$"""
            set -eu
            test "$1" = "verify-blob"
            test "$2" = "--bundle"
            test -f "$3"
            test "$4" = "--trusted-root"
            test "$5" = "{{fixture.TrustedRootPath}}"
            test "$6" = "--certificate-identity"
            test "$7" = "{{Fixture.CertificateIdentity}}"
            test "$8" = "--certificate-oidc-issuer"
            test "$9" = "{{Fixture.OidcIssuer}}"
            test -f "${10}"
            test "$(cat "${10}")" = "subject-bytes"
            test "$(cat "$3")" = "retained-bundle"
            dirname "${10}" > {{ShellLiteral(marker)}}
            printf 'safe-success'
            printf 'safe-diagnostic' >&2
            """);

        var verified = await fixture.Verifier(executable).VerifyAsync(
            Encoding.UTF8.GetBytes("subject-bytes"),
            Encoding.UTF8.GetBytes("retained-bundle"));

        Assert.True(verified);
        var invocationDirectory = File.ReadAllText(marker).Trim();
        Assert.False(Directory.Exists(invocationDirectory));
    }

    [Fact]
    public async Task Returns_false_for_a_nonzero_cosign_exit_without_exposing_output()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var executable = fixture.Executable("printf 'secret-output'; printf 'secret-error' >&2; exit 23");

        Assert.False(await fixture.Verifier(executable).VerifyAsync(
            Encoding.UTF8.GetBytes("subject"),
            Encoding.UTF8.GetBytes("bundle")));
    }

    [Fact]
    public async Task Returns_false_when_combined_cosign_output_exceeds_the_authority_cap()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var executable = fixture.Executable("printf '1234567890123456789012345678901234567890'; printf '12345678901234567890' >&2");

        Assert.False(await fixture.Verifier(executable, outputLimit: 32).VerifyAsync(
            Encoding.UTF8.GetBytes("subject"),
            Encoding.UTF8.GetBytes("bundle")));
    }

    [Fact]
    public async Task Starts_cosign_with_only_the_private_configuration_environment()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var executable = fixture.Executable("""
            set -eu
            test -z "${COSIGN_PASSWORD-}"
            test -z "${SIGSTORE_ROOT_FILE-}"
            test -z "${SIGSTORE_REKOR_URL-}"
            test -z "${HTTP_PROXY-}"
            test -z "${HTTPS_PROXY-}"
            test -z "${ALL_PROXY-}"
            test -z "${http_proxy-}"
            test -z "${https_proxy-}"
            test -z "${all_proxy-}"
            test -z "${HOME-}"
            invocation=$(dirname "${10}")
            test "$XDG_CONFIG_HOME" = "$invocation"
            test "$TMPDIR" = "$invocation"
            test "$TMP" = "$invocation"
            test "$TEMP" = "$invocation"
            """);

        Assert.True(await fixture.Verifier(executable).VerifyAsync(
            Encoding.UTF8.GetBytes("subject"),
            Encoding.UTF8.GetBytes("bundle")));
    }

    [Fact]
    public async Task Returns_false_and_terminates_cosign_when_the_authority_timeout_expires()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var executable = fixture.Executable("sleep 5");
        var stopwatch = Stopwatch.StartNew();

        var verified = await fixture.Verifier(executable, timeout: TimeSpan.FromMilliseconds(100)).VerifyAsync(
            Encoding.UTF8.GetBytes("subject"),
            Encoding.UTF8.GetBytes("bundle"));
        stopwatch.Stop();

        Assert.False(verified);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Verification took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Propagates_cancellation_after_terminating_cosign()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var executable = fixture.Executable("sleep 5");
        using var cancellation = new CancellationTokenSource();
        var running = fixture.Verifier(executable).VerifyAsync(
            Encoding.UTF8.GetBytes("subject"),
            Encoding.UTF8.GetBytes("bundle"),
            cancellation.Token);

        await Task.Delay(100);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    [Fact]
    public async Task Returns_false_when_a_pinned_trusted_root_is_tampered_before_launch()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var marker = fixture.Path("launched");
        var executable = fixture.Executable($"touch {ShellLiteral(marker)}");
        var verifier = fixture.Verifier(executable);
        await File.WriteAllTextAsync(fixture.TrustedRootPath, "tampered");

        Assert.False(await verifier.VerifyAsync(
            Encoding.UTF8.GetBytes("subject"),
            Encoding.UTF8.GetBytes("bundle")));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Rejects_an_authority_with_a_relative_path_or_unpinned_digest()
    {
        using var fixture = new Fixture();
        var executable = fixture.Executable("exit 0");

        Assert.Throws<ArgumentException>(() => new SigstoreReleaseManifestBundleVerifier(
            new SigstoreBundleVerificationAuthority(
                "cosign",
                fixture.Hash(executable),
                fixture.TrustedRootPath,
                fixture.Hash(fixture.TrustedRootPath),
                Fixture.CertificateIdentity,
                Fixture.OidcIssuer,
                TimeSpan.FromSeconds(1))));

        Assert.Throws<ArgumentException>(() => new SigstoreReleaseManifestBundleVerifier(
            new SigstoreBundleVerificationAuthority(
                executable,
                new string('0', 64),
                fixture.TrustedRootPath,
                fixture.Hash(fixture.TrustedRootPath),
                Fixture.CertificateIdentity,
                Fixture.OidcIssuer,
                TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Rejects_a_symlinked_trusted_root()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new Fixture();
        var executable = fixture.Executable("exit 0");
        var link = fixture.Path("trusted-root-link");
        File.CreateSymbolicLink(link, fixture.TrustedRootPath);

        Assert.Throws<ArgumentException>(() => new SigstoreReleaseManifestBundleVerifier(
            new SigstoreBundleVerificationAuthority(
                executable,
                fixture.Hash(executable),
                link,
                fixture.Hash(fixture.TrustedRootPath),
                Fixture.CertificateIdentity,
                Fixture.OidcIssuer,
                TimeSpan.FromSeconds(1))));
    }

    private static string ShellLiteral(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private sealed class Fixture : IDisposable
    {
        public const string CertificateIdentity = "https://github.com/valence-works/elsa-production-image/.github/workflows/build-and-push.yml@refs/heads/main";
        public const string OidcIssuer = "https://token.actions.githubusercontent.com";

        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-sigstore-verifier-{Guid.NewGuid():N}");

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            TrustedRootPath = File("trusted-root.json", "trusted-root");
        }

        public string TrustedRootPath { get; }

        public string Path(string name) => System.IO.Path.Combine(_directory, name);

        public string File(string name, string contents)
        {
            var path = Path(name);
            System.IO.File.WriteAllText(path, contents);
            SetPrivateFileMode(path);
            return path;
        }

        public string Executable(string body)
        {
            var path = File($"cosign-{Guid.NewGuid():N}.sh", $"#!/bin/sh\n{body}\n");
            if (!OperatingSystem.IsWindows())
                System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }

        public string Hash(string path) => Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path))).ToLowerInvariant();

        public SigstoreReleaseManifestBundleVerifier Verifier(
            string executable,
            TimeSpan? timeout = null,
            int outputLimit = 16_384) =>
            new(new SigstoreBundleVerificationAuthority(
                executable,
                Hash(executable),
                TrustedRootPath,
                Hash(TrustedRootPath),
                CertificateIdentity,
                OidcIssuer,
                timeout ?? TimeSpan.FromSeconds(5),
                outputLimit));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private static void SetPrivateFileMode(string path)
        {
            if (!OperatingSystem.IsWindows())
                System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
