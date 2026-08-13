using System.Security.Cryptography;
using System.Text;

namespace ValenceControl.Healing.Core.Incidents;

public sealed record HealingFingerprint(string Version, string Value);

public sealed class HealingFingerprintService
{
    public const string CurrentVersion = "1";

    public HealingFingerprint Compute(
        NormalizedHealingSignal signal,
        IEnumerable<string> componentCandidates,
        string repairRepositoryKey)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(componentCandidates);
        ArgumentException.ThrowIfNullOrWhiteSpace(repairRepositoryKey);

        var canonical = new StringBuilder("healing-fingerprint-v1");
        Append(canonical, signal.ExceptionType);
        Append(canonical, signal.OperationName);
        Append(canonical, repairRepositoryKey.Trim().ToLowerInvariant());
        foreach (var component in componentCandidates
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(x => x.Trim())
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
            Append(canonical, component);
        foreach (var frame in signal.Frames)
        {
            Append(canonical, frame.AssemblyName ?? string.Empty);
            Append(canonical, frame.TypeName);
            Append(canonical, frame.MethodName);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return new HealingFingerprint(
            CurrentVersion,
            $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}");
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append('|').Append(value.Length).Append(':').Append(value);
}
