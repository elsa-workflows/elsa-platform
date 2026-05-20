using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Deployment.Abstractions.Artifacts;

namespace Elsa.Platform.Deployment.Artifacts;

internal static class DeploymentArtifactChecksumService
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async ValueTask<ArtifactDigest> ComputeFileDigestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new ArtifactDigest(ArtifactLayoutConstants.ChecksumAlgorithm, Convert.ToHexString(hash).ToLowerInvariant());
    }

    public static ArtifactDigest ComputeContentDigest(IEnumerable<DeploymentArtifactChecksumEntry> entries)
    {
        var canonical = entries
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => new
            {
                entry.Path,
                Kind = entry.Kind.ToString(),
                entry.Algorithm,
                entry.Digest,
                entry.Size
            })
            .ToArray();
        var json = JsonSerializer.Serialize(canonical, CanonicalJsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return new ArtifactDigest(ArtifactLayoutConstants.ChecksumAlgorithm, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
