using System.Diagnostics;
using System.Security.Cryptography;

namespace ElsaControl.Api.ReleaseCatalog;

/// <summary>
/// Verifies a downloaded OCI subject against its retained Sigstore bundle through the
/// server-pinned cosign executable. The process boundary deliberately returns only a boolean;
/// command output, certificates, payloads and platform exceptions never cross this boundary.
/// </summary>
internal sealed class SigstoreReleaseManifestBundleVerifier : IReleaseManifestBundleVerifier
{
    private const int MaximumSubjectBytes = 4 * 1024 * 1024;
    private const int MaximumBundleBytes = 256 * 1024;
    private const int MaximumAuthorityPathLength = 4096;
    private const int MaximumPolicyLength = 2048;
    private const int MaximumOutputCharacters = 1 * 1024 * 1024;
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan CaptureDrainTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);

    private readonly SigstoreBundleVerificationAuthority _authority;

    public SigstoreReleaseManifestBundleVerifier(SigstoreBundleVerificationAuthority authority)
    {
        ValidateAuthority(authority);
        _authority = authority;
    }

    public async ValueTask<bool> VerifyAsync(
        ReadOnlyMemory<byte> subject,
        ReadOnlyMemory<byte> bundle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (subject.IsEmpty || bundle.IsEmpty || subject.Length > MaximumSubjectBytes || bundle.Length > MaximumBundleBytes)
            return false;

        string? invocationDirectory = null;
        try
        {
            if (!IsAuthorityCurrent(_authority))
                return false;

            invocationDirectory = CreatePrivateDirectory();
            var subjectPath = Path.Combine(invocationDirectory, "subject.bin");
            var bundlePath = Path.Combine(invocationDirectory, "bundle.json");
            await WritePrivateFileAsync(subjectPath, subject, cancellationToken).ConfigureAwait(false);
            await WritePrivateFileAsync(bundlePath, bundle, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // The authority is server-owned but the files are mutable. Recheck both the
            // regular-file property and exact digest immediately before starting cosign.
            if (!IsAuthorityCurrent(_authority))
                return false;

            return await ExecuteCosignAsync(
                invocationDirectory,
                subjectPath,
                bundlePath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            DeletePrivateDirectory(invocationDirectory);
        }
    }

    private async Task<bool> ExecuteCosignAsync(
        string invocationDirectory,
        string subjectPath,
        string bundlePath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            EnableRaisingEvents = true,
            StartInfo = CreateStartInfo(invocationDirectory, subjectPath, bundlePath)
        };

        try
        {
            if (!process.Start())
                return false;
        }
        catch (Exception)
        {
            return false;
        }

        using var captureCancellation = new CancellationTokenSource();
        var outputLimitReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outputBudget = new OutputBudget(_authority.MaximumOutputCharacters);
        var standardOutputTask = DrainOutputAsync(process.StandardOutput, outputBudget, outputLimitReached, captureCancellation.Token);
        var standardErrorTask = DrainOutputAsync(process.StandardError, outputBudget, outputLimitReached, captureCancellation.Token);
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(_authority.Timeout);
        var cancellationSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            cancellationSignal);

        var completedTask = await Task.WhenAny(
            exitTask,
            timeoutTask,
            cancellationSignal.Task,
            outputLimitReached.Task).ConfigureAwait(false);

        if (completedTask == cancellationSignal.Task ||
            completedTask == timeoutTask ||
            completedTask == outputLimitReached.Task)
        {
            captureCancellation.Cancel();
            _ = await TerminateAsync(process, exitTask).ConfigureAwait(false);
            _ = await ObserveCaptureTasksAsync(
                standardOutputTask,
                standardErrorTask,
                captureCancellation).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        if (!await ObserveCaptureTasksAsync(
                standardOutputTask,
                standardErrorTask,
                captureCancellation).ConfigureAwait(false) ||
            outputLimitReached.Task.IsCompleted)
            return false;

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await exitTask.ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ProcessStartInfo CreateStartInfo(string invocationDirectory, string subjectPath, string bundlePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _authority.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = invocationDirectory
        };

        startInfo.ArgumentList.Add("verify-blob");
        startInfo.ArgumentList.Add("--bundle");
        startInfo.ArgumentList.Add(bundlePath);
        startInfo.ArgumentList.Add("--trusted-root");
        startInfo.ArgumentList.Add(_authority.TrustedRootPath);
        startInfo.ArgumentList.Add("--certificate-identity");
        startInfo.ArgumentList.Add(_authority.CertificateIdentity);
        startInfo.ArgumentList.Add("--certificate-oidc-issuer");
        startInfo.ArgumentList.Add(_authority.OidcIssuer);
        startInfo.ArgumentList.Add(subjectPath);

        // Cosign must not inherit credentials, proxy settings, tool overrides or ambient
        // configuration. Absolute executable and pinned trust-root paths need no PATH lookup.
        startInfo.Environment.Clear();
        startInfo.Environment["XDG_CONFIG_HOME"] = invocationDirectory;
        startInfo.Environment["TMPDIR"] = invocationDirectory;
        startInfo.Environment["TMP"] = invocationDirectory;
        startInfo.Environment["TEMP"] = invocationDirectory;
        if (!OperatingSystem.IsWindows())
            startInfo.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        return startInfo;
    }

    private static async Task<bool> DrainOutputAsync(
        StreamReader reader,
        OutputBudget outputBudget,
        TaskCompletionSource<bool> outputLimitReached,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return true;

                if (!outputBudget.TryTake(read))
                {
                    outputLimitReached.TrySetResult(true);
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> ObserveCaptureTasksAsync(
        Task<bool> standardOutputTask,
        Task<bool> standardErrorTask,
        CancellationTokenSource captureCancellation)
    {
        var capturesTask = Task.WhenAll(standardOutputTask, standardErrorTask);
        try
        {
            if (await Task.WhenAny(capturesTask, Task.Delay(CaptureDrainTimeout)).ConfigureAwait(false) != capturesTask)
            {
                captureCancellation.Cancel();
                return false;
            }

            return (await capturesTask.ConfigureAwait(false)).All(value => value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> TerminateAsync(Process process, Task exitTask)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            if (await Task.WhenAny(exitTask, Task.Delay(TerminationTimeout)).ConfigureAwait(false) != exitTask)
                return false;
            await exitTask.ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ValidateAuthority(SigstoreBundleVerificationAuthority authority)
    {
        if (authority is null ||
            !IsSafeAbsoluteFile(authority.ExecutablePath) ||
            !IsSafeAbsoluteFile(authority.TrustedRootPath) ||
            !IsSha256(authority.ExecutableSha256) ||
            !IsSha256(authority.TrustedRootSha256) ||
            !IsSafePolicy(authority.CertificateIdentity) ||
            !IsSafePolicy(authority.OidcIssuer) ||
            authority.Timeout <= TimeSpan.Zero ||
            authority.Timeout > MaximumTimeout ||
            authority.MaximumOutputCharacters is < 1 or > MaximumOutputCharacters ||
            !HasExactSha256(authority.ExecutablePath, authority.ExecutableSha256) ||
            !HasExactSha256(authority.TrustedRootPath, authority.TrustedRootSha256))
            throw new ArgumentException("The Sigstore verification authority is invalid.", nameof(authority));
    }

    private static bool IsAuthorityCurrent(SigstoreBundleVerificationAuthority authority) =>
        IsSafeAbsoluteFile(authority.ExecutablePath) &&
        IsSafeAbsoluteFile(authority.TrustedRootPath) &&
        HasExactSha256(authority.ExecutablePath, authority.ExecutableSha256) &&
        HasExactSha256(authority.TrustedRootPath, authority.TrustedRootSha256);

    private static bool IsSafeAbsoluteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumAuthorityPathLength ||
            path.Any(char.IsControl) || !Path.IsPathFullyQualified(path))
            return false;

        try
        {
            var file = new FileInfo(path);
            return file.Exists &&
                (file.Attributes & FileAttributes.Directory) == 0 &&
                (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
                file.LinkTarget is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsSafePolicy(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumPolicyLength &&
        !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool HasExactSha256(string path, string expected)
    {
        if (!IsSha256(expected))
            return false;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            var actual = SHA256.HashData(stream);
            var expectedBytes = Convert.FromHexString(expected);
            return CryptographicOperations.FixedTimeEquals(actual, expectedBytes);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string CreatePrivateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-sigstore-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }

    private static async Task WritePrivateFileAsync(
        string path,
        ReadOnlyMemory<byte> contents,
        CancellationToken cancellationToken)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        await using var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeletePrivateDirectory(string? directory)
    {
        if (directory is null)
            return;

        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Cleanup is best effort and must not replace the verification result or expose a
            // platform-specific path/error to the caller.
        }
    }

    private sealed class OutputBudget(int capacity)
    {
        private int _remaining = capacity;

        public bool TryTake(int count)
        {
            while (true)
            {
                var remaining = Volatile.Read(ref _remaining);
                if (count > remaining)
                    return false;

                if (Interlocked.CompareExchange(ref _remaining, remaining - count, remaining) == remaining)
                    return true;
            }
        }
    }
}
