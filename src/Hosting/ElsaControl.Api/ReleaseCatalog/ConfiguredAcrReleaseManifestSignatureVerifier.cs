using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Api.ReleaseCatalog;

internal sealed class ConfiguredAcrReleaseManifestSignatureVerifier : IReleaseManifestSignatureVerifier
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly ReleaseManifestSignatureVerification Rejected = new(false, "", "", "", "");
    private readonly AcrReleaseRegistryAuthority _authority;
    private readonly IReleaseRegistryReader _reader;
    private readonly IReleaseManifestBundleVerifier _bundleVerifier;
    private readonly string _identity;
    private readonly string _issuer;

    public ConfiguredAcrReleaseManifestSignatureVerifier(AcrReleaseRegistryAuthority authority,
        IReleaseRegistryReader reader, IReleaseManifestBundleVerifier bundleVerifier, string identity, string issuer)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _bundleVerifier = bundleVerifier ?? throw new ArgumentNullException(nameof(bundleVerifier));
        _identity = identity;
        _issuer = issuer;
    }

    public async ValueTask<ReleaseManifestSignatureVerification> VerifyAsync(
        ReleaseManifestArtifact artifact, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (artifact is null || !ReleaseRegistryProtocol.IsDigest(artifact.Digest) ||
                !ReleaseRegistryProtocol.IsDigest(artifact.PayloadDigest) ||
                !string.Equals(artifact.Reference, Reference(artifact.Digest), StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(_identity) || string.IsNullOrWhiteSpace(_issuer) ||
                string.IsNullOrEmpty(artifact.Payload) || artifact.Payload.Length > ReleaseRegistryProtocol.MaximumManifestBytes ||
                Utf8.GetByteCount(artifact.Payload) > ReleaseRegistryProtocol.MaximumManifestBytes)
                return Rejected;

            var payload = Utf8.GetBytes(artifact.Payload);
            if (!Matches(payload, artifact.PayloadDigest!, ReleaseRegistryProtocol.MaximumManifestBytes))
                return Rejected;

            await using var session = await _reader.OpenAsync(cancellationToken);
            var subject = await session.ReadManifestAsync(artifact.Digest, cancellationToken);
            if (!Matches(subject, artifact.Digest, ReleaseRegistryProtocol.MaximumManifestBytes))
                return Rejected;

            using var subjectDocument = ParseManifest(subject, ReleaseRegistryProtocol.ReleaseArtifactType);
            var payloadLayer = PayloadLayer(subjectDocument.RootElement);
            if (payloadLayer.MediaType != "application/json" || payloadLayer.Size != payload.Length ||
                payloadLayer.Digest != artifact.PayloadDigest)
                return Rejected;
            var downloadedPayload = await session.ReadBlobAsync(payloadLayer.Digest, payload.Length, cancellationToken);
            if (!Matches(downloadedPayload, payloadLayer, ReleaseRegistryProtocol.MaximumManifestBytes) ||
                !CryptographicOperations.FixedTimeEquals(downloadedPayload, payload))
                return Rejected;

            var referrers = await session.ReadReferrersAsync(artifact.Digest, cancellationToken);
            if (referrers.Count > 64)
                return Rejected;
            var signatures = referrers.Where(x => x.ArtifactType == ReleaseRegistryProtocol.BundleMediaType).ToArray();
            if (signatures.Length != 1 || signatures[0].MediaType != ReleaseRegistryProtocol.ManifestMediaType ||
                !ValidDescriptor(signatures[0], ReleaseRegistryProtocol.MaximumManifestBytes))
                return Rejected;

            var signature = signatures[0];
            var evidence = await session.ReadManifestAsync(signature.Digest, cancellationToken);
            if (!Matches(evidence, signature, ReleaseRegistryProtocol.MaximumManifestBytes))
                return Rejected;
            using var evidenceDocument = ParseManifest(evidence, ReleaseRegistryProtocol.BundleMediaType);
            var evidenceSubject = Descriptor(Property(evidenceDocument.RootElement, "subject"));
            if (evidenceSubject.MediaType != ReleaseRegistryProtocol.ManifestMediaType ||
                evidenceSubject.Digest != artifact.Digest || evidenceSubject.Size != subject.Length)
                return Rejected;

            var bundleLayers = Property(evidenceDocument.RootElement, "layers");
            if (bundleLayers.ValueKind != JsonValueKind.Array || bundleLayers.GetArrayLength() != 1)
                return Rejected;
            var bundleLayer = Descriptor(bundleLayers[0]);
            if (bundleLayer.MediaType != ReleaseRegistryProtocol.BundleMediaType ||
                !ValidDescriptor(bundleLayer, ReleaseRegistryProtocol.MaximumBundleBytes))
                return Rejected;
            var bundle = await session.ReadBlobAsync(bundleLayer.Digest, (int)bundleLayer.Size, cancellationToken);
            if (!Matches(bundle, bundleLayer, ReleaseRegistryProtocol.MaximumBundleBytes) ||
                !await _bundleVerifier.VerifyAsync(subject, bundle, cancellationToken))
                return Rejected;

            return new(true, _identity, artifact.Digest, Reference(signature.Digest), signature.Digest,
                _issuer, artifact.PayloadDigest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The admission service projects fixed findings. Never expose transport,
            // parser, credential or tool output through the verification result.
            return Rejected;
        }
    }

    private string Reference(string digest) => $"oci://{_authority.RegistryHost}/{_authority.Repository}@{digest}";

    private static bool Matches(byte[] bytes, ReleaseRegistryDescriptor descriptor, int maximumBytes) =>
        bytes.LongLength == descriptor.Size && Matches(bytes, descriptor.Digest, maximumBytes);

    private static bool Matches(byte[] bytes, string digest, int maximumBytes) =>
        bytes.Length is > 0 && bytes.Length <= maximumBytes && ReleaseRegistryProtocol.IsDigest(digest) &&
        string.Equals("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), digest, StringComparison.Ordinal);

    private static bool ValidDescriptor(ReleaseRegistryDescriptor descriptor, int maximumBytes) =>
        ReleaseRegistryProtocol.IsDigest(descriptor.Digest) && descriptor.Size > 0 && descriptor.Size <= maximumBytes;

    private static JsonDocument ParseManifest(byte[] bytes, string artifactType)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try
        {
            var root = document.RootElement;
            if (!Property(root, "schemaVersion").TryGetInt32(out var version) || version != 2 ||
                String(root, "mediaType") != ReleaseRegistryProtocol.ManifestMediaType ||
                String(root, "artifactType") != artifactType)
                throw new InvalidDataException();
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static ReleaseRegistryDescriptor PayloadLayer(JsonElement root)
    {
        var layers = Property(root, "layers");
        if (layers.ValueKind != JsonValueKind.Array || layers.GetArrayLength() is 0 or > 64)
            throw new InvalidDataException();
        ReleaseRegistryDescriptor? selected = null;
        foreach (var layer in layers.EnumerateArray())
        {
            var annotations = Property(layer, "annotations", required: false);
            if (annotations.ValueKind == JsonValueKind.Undefined)
                continue;
            var title = Property(annotations, "org.opencontainers.image.title", required: false);
            if (title.ValueKind == JsonValueKind.Undefined)
                continue;
            if (title.ValueKind != JsonValueKind.String)
                throw new InvalidDataException();
            if (title.GetString() != "release/release-manifest.json")
                continue;
            if (selected is not null)
                throw new InvalidDataException();
            selected = Descriptor(layer);
        }
        return selected ?? throw new InvalidDataException();
    }

    private static ReleaseRegistryDescriptor Descriptor(JsonElement value)
    {
        if (!Property(value, "size").TryGetInt64(out var size))
            throw new InvalidDataException();
        return new(String(value, "mediaType"), String(value, "digest"), size, null);
    }

    private static string String(JsonElement value, string name)
    {
        var property = Property(value, name);
        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? throw new InvalidDataException()
            : throw new InvalidDataException();
    }

    private static JsonElement Property(JsonElement value, string name, bool required = true)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException();
        var found = false;
        var result = default(JsonElement);
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name != name)
                continue;
            if (found)
                throw new InvalidDataException();
            found = true;
            result = property.Value;
        }
        return found || !required ? result : throw new InvalidDataException();
    }
}
