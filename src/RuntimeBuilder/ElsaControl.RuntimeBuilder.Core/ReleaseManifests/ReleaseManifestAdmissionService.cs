using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Core.ReleaseManifests;

/// <summary>
/// Parses and admits a producer release manifest after cryptographic verification. The
/// verifier is intentionally injected so registry/cosign transport and trust policy stay
/// outside the provider-neutral runtime-builder contract.
/// </summary>
public sealed class ReleaseManifestAdmissionService(IReleaseManifestSignatureVerifier signatureVerifier)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ReleaseManifestAdmissionResult> AdmitAsync(
        ReleaseManifestArtifact artifact,
        ReleaseManifestAdmissionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(signatureVerifier);

        var findings = new List<ReleaseManifestAdmissionFinding>();
        ValidateArtifact(artifact, findings);
        ValidateOptions(options, findings);

        // Do not invoke an external verifier for malformed or non-immutable input.
        // This keeps the verifier outside the admission boundary and avoids treating
        // a signature over an invalid envelope as useful evidence.
        if (findings.Count > 0)
            return Rejected(artifact, options, findings);

        CommercialReleaseManifest? manifest = null;
        if (!string.IsNullOrWhiteSpace(artifact.Payload))
            manifest = ParseManifest(artifact.Payload, findings);

        if (manifest is null || findings.Count > 0)
            return Rejected(artifact, options, findings);

        ValidateManifest(manifest, findings);
        ValidateSelection(manifest, options, findings);
        if (findings.Count > 0)
            return Rejected(artifact, options, findings);

        ReleaseManifestSignatureVerification? signature = null;
        try
        {
            signature = await signatureVerifier.VerifyAsync(artifact, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            findings.Add(new("signature.verification.failed", "Release-manifest signature verification failed closed.", "signature"));
        }

        if (signature is null)
            findings.Add(new("signature.missing", "A signature verification result is required.", "signature"));
        else
            ValidateSignature(signature, artifact, options, findings);

        if (findings.Count > 0 || signature is null)
            return Rejected(artifact, options, findings);

        return new(
            true,
            artifact.Reference,
            artifact.Digest,
            SanitizeManifest(manifest),
            new(signature.EvidenceReference, signature.EvidenceDigest),
            options.RegistryClass,
            options.TopologyId,
            findings);
    }

    private static ReleaseManifestAdmissionResult Rejected(
        ReleaseManifestArtifact artifact,
        ReleaseManifestAdmissionOptions options,
        IReadOnlyList<ReleaseManifestAdmissionFinding> findings) => new(
            false,
            null,
            null,
            null,
            null,
            options.RegistryClass,
            options.TopologyId,
            findings);

    private static CommercialReleaseManifest SanitizeManifest(CommercialReleaseManifest manifest) =>
        manifest with
        {
            Topologies = manifest.Topologies
                .Select(topology => topology with
                {
                    SupplyChain = topology.SupplyChain with
                    {
                        // Signature subject identity was validated above, but is not
                        // needed by projection and must not cross the admission boundary.
                        Signatures = []
                    }
                })
                .ToArray()
        };

    private static CommercialReleaseManifest? ParseManifest(
        string payload,
        List<ReleaseManifestAdmissionFinding> findings)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(new("manifest.object.required", "The release manifest must be a JSON object.", "manifest"));
                return null;
            }

            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(schemaVersion.GetString()))
            {
                findings.Add(new("manifest.schema.required", "A release-manifest schema version is required.", "manifest.schemaVersion"));
            }
            else if (!string.Equals(schemaVersion.GetString(), ReleaseManifestSchema.CurrentVersion, StringComparison.Ordinal))
            {
                findings.Add(new("manifest.schema.unsupported", "Release-manifest schema is not supported.", "manifest.schemaVersion"));
            }

            return JsonSerializer.Deserialize<CommercialReleaseManifest>(document.RootElement.GetRawText(), JsonOptions)
                ?? AddAndReturnNull(new("manifest.empty", "The release manifest is empty.", "manifest"));
        }
        catch (JsonException)
        {
            findings.Add(new("manifest.json.invalid", "Release-manifest JSON is invalid.", "manifest"));
            return null;
        }

        CommercialReleaseManifest? AddAndReturnNull(ReleaseManifestAdmissionFinding finding)
        {
            findings.Add(finding);
            return null;
        }
    }

    private static void ValidateArtifact(ReleaseManifestArtifact artifact, List<ReleaseManifestAdmissionFinding> findings)
    {
        Required(artifact.Reference, "manifest.reference.required", "The release-manifest reference is required.", "artifact.reference", findings);
        Required(artifact.Digest, "manifest.digest.required", "The release-manifest digest is required.", "artifact.digest", findings);
        Required(artifact.Payload, "manifest.payload.required", "The release-manifest payload is required.", "artifact.payload", findings);

        if (!string.IsNullOrWhiteSpace(artifact.Reference))
        {
            if (!IsSafeLocator(artifact.Reference, requireAbsolute: true))
                findings.Add(new("manifest.reference.invalid", "The release-manifest reference must be a safe absolute locator.", "artifact.reference"));

            var referenceDigest = ExtractDigest(artifact.Reference);
            if (referenceDigest is null)
                findings.Add(new("manifest.referenceDigest.required", "The release-manifest reference must include an immutable sha256 digest.", "artifact.reference"));
            else if (!string.Equals(referenceDigest, artifact.Digest, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("manifest.referenceDigest.mismatch", "The release-manifest reference digest must match the artifact digest.", "artifact.reference"));
        }

        if (!IsDigest(artifact.Digest))
            findings.Add(new("manifest.digest.invalid", "The release-manifest artifact must use a sha256 digest.", "artifact.digest"));

        if (!string.IsNullOrEmpty(artifact.Payload) && IsDigest(artifact.Digest))
        {
            var payloadDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Payload))).ToLowerInvariant()}";
            if (!string.Equals(payloadDigest, artifact.Digest, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("manifest.payloadDigest.mismatch", "The artifact digest must match the exact UTF-8 release-manifest payload.", "artifact.payload"));
        }
    }

    private static void ValidateOptions(ReleaseManifestAdmissionOptions options, List<ReleaseManifestAdmissionFinding> findings)
    {
        Required(options.ExpectedSignatureSubject, "signature.subject.expected.required", "An expected signature subject is required.", "options.expectedSignatureSubject", findings);
        Required(options.RegistryClass, "image.registryClass.required", "A registry class is required for projection.", "options.registryClass", findings);
    }

    private static void ValidateSignature(
        ReleaseManifestSignatureVerification signature,
        ReleaseManifestArtifact artifact,
        ReleaseManifestAdmissionOptions options,
        List<ReleaseManifestAdmissionFinding> findings)
    {
        if (!signature.IsValid)
            findings.Add(new("signature.invalid", "The release-manifest signature is not valid.", "signature"));
        Required(signature.Subject, "signature.subject.required", "The verified signature subject is required.", "signature.subject", findings);
        Required(signature.SubjectDigest, "signature.subjectDigest.required", "The signed subject digest is required.", "signature.subjectDigest", findings);
        Required(signature.EvidenceReference, "signature.evidenceReference.required", "Retained signature evidence is required.", "signature.evidenceReference", findings);
        Required(signature.EvidenceDigest, "signature.evidenceDigest.required", "Retained signature evidence must have a digest.", "signature.evidenceDigest", findings);

        if (!string.IsNullOrWhiteSpace(signature.Subject)
            && !string.Equals(signature.Subject, options.ExpectedSignatureSubject, StringComparison.Ordinal))
            findings.Add(new("signature.subject.mismatch", "The signature subject is not approved for this release distribution.", "signature.subject"));

        if (!IsDigest(signature.SubjectDigest))
            findings.Add(new("signature.subjectDigest.invalid", "The signed subject must use a sha256 digest.", "signature.subjectDigest"));
        else if (!string.Equals(signature.SubjectDigest, artifact.Digest, StringComparison.OrdinalIgnoreCase))
            findings.Add(new("signature.subjectDigest.mismatch", "The signed subject digest must match the immutable release-manifest digest.", "signature.subjectDigest"));

        if (!IsDigest(signature.EvidenceDigest))
            findings.Add(new("signature.evidenceDigest.invalid", "Signature evidence must use a sha256 digest.", "signature.evidenceDigest"));
        if (!string.IsNullOrWhiteSpace(signature.EvidenceReference)
            && !IsSafeEvidenceReference(signature.EvidenceReference, signature.EvidenceDigest))
            findings.Add(new("signature.evidenceReference.invalid", "Signature evidence must be an immutable safe locator.", "signature.evidenceReference"));
    }

    private static void ValidateManifest(CommercialReleaseManifest manifest, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (!string.Equals(manifest.SchemaVersion, ReleaseManifestSchema.CurrentVersion, StringComparison.Ordinal))
            findings.Add(new("manifest.schema.unsupported", "Release-manifest schema is not supported.", "manifest.schemaVersion"));

        var distribution = manifest.Distribution;
        if (distribution is null)
        {
            findings.Add(new("distribution.required", "Release distribution metadata is required.", "distribution"));
        }
        else
        {
            Required(distribution.Id, "distribution.id.required", "Distribution identity is required.", "distribution.id", findings);
            Required(distribution.Generation, "distribution.generation.required", "Distribution generation is required.", "distribution.generation", findings);
            Required(distribution.ReleaseLine, "distribution.releaseLine.required", "Release line is required.", "distribution.releaseLine", findings);
            Required(distribution.ReleaseVersion, "distribution.releaseVersion.required", "Exact release version is required.", "distribution.releaseVersion", findings);
            Required(distribution.Channel, "distribution.channel.required", "Release channel is required.", "distribution.channel", findings);
            Required(distribution.Lifecycle, "distribution.lifecycle.required", "Release lifecycle is required.", "distribution.lifecycle", findings);
            ValidateSource(distribution.Source, findings);
        }

        var topologies = manifest.Topologies;
        if (topologies is null || topologies.Count == 0)
        {
            findings.Add(new("topologies.required", "At least one release topology is required.", "topologies"));
            return;
        }

        Duplicate(topologies, topology => topology?.Id ?? string.Empty, "topology.duplicate", "topologies", findings);
        foreach (var topology in topologies)
        {
            if (topology is null)
            {
                findings.Add(new("topology.null", "Release topologies cannot contain null items.", "topologies"));
                continue;
            }

            ValidateTopology(topology, findings);
        }
    }

    private static void ValidateSource(ReleaseManifestSource? source, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (source is null)
        {
            findings.Add(new("source.required", "Release source metadata is required.", "distribution.source"));
            return;
        }

        Required(source.Repository, "source.repository.required", "Source repository is required.", "distribution.source.repository", findings);
        Required(source.Commit, "source.commit.required", "Source commit is required.", "distribution.source.commit", findings);
        Required(source.Workflow, "source.workflow.required", "Source workflow is required.", "distribution.source.workflow", findings);
        Required(source.RunId, "source.runId.required", "Source workflow run identity is required.", "distribution.source.runId", findings);

        if (!string.IsNullOrWhiteSpace(source.Repository)
            && (!IsSafeLocator(source.Repository, requireAbsolute: true)
                || !source.Repository.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("source.repository.invalid", "Source repository must be an HTTPS locator without credentials or mutable query state.", "distribution.source.repository"));

        if (!string.IsNullOrWhiteSpace(source.Commit)
            && (source.Commit.Length != 40 || source.Commit.Any(x => !Uri.IsHexDigit(x))))
            findings.Add(new("source.commit.invalid", "Source commit must be a full hexadecimal Git commit identity.", "distribution.source.commit"));
    }

    private static void ValidateTopology(ReleaseManifestTopology topology, List<ReleaseManifestAdmissionFinding> findings)
    {
        Required(topology.Id, "topology.id.required", "Topology identity is required.", "topology.id", findings);
        if (topology.RuntimeKinds is null || topology.RuntimeKinds.Count == 0)
            findings.Add(new("topology.runtimeKinds.required", "At least one topology runtime kind is required.", "topology.runtimeKinds"));

        if (topology.Components is null || topology.Components.Count == 0)
            findings.Add(new("topology.components.required", "The complete component version map is required.", "topology.components"));
        else
        {
            foreach (var component in topology.Components)
            {
                Required(component.Key, "topology.component.id.required", "Component identity is required.", "topology.components", findings);
                Required(component.Value, "topology.component.version.required", "Component version is required.", "topology.components", findings);
            }
        }

        if (topology.Endpoints is not null)
        {
            foreach (var endpoint in topology.Endpoints)
            {
                Required(endpoint.Key, "topology.endpoint.name.required", "Endpoint name is required.", "topology.endpoints", findings);
                Required(endpoint.Value, "topology.endpoint.path.required", "Endpoint path is required.", "topology.endpoints", findings);
                if (!string.IsNullOrWhiteSpace(endpoint.Value) && !EndpointPathPolicy.IsSafe(endpoint.Value))
                    findings.Add(new("topology.endpoint.path.invalid", "Endpoint paths must be safe relative paths.", "topology.endpoints"));
            }
        }

        var compatibility = topology.Compatibility;
        if (compatibility is null)
        {
            findings.Add(new("compatibility.required", "Topology compatibility metadata is required.", "topology.compatibility"));
        }
        else
        {
            Required(compatibility.PackageManifestSchema, "compatibility.packageManifestSchema.required", "Package manifest schema identity is required.", "topology.compatibility", findings);
            if (compatibility.RuntimeCapabilities is null || compatibility.RuntimeCapabilities.Count == 0)
                findings.Add(new("compatibility.runtimeCapabilities.required", "Runtime capability evidence is required.", "topology.compatibility"));
        }

        if (topology.Images is null || topology.Images.Count == 0)
        {
            findings.Add(new("topology.images.required", "At least one topology image is required.", "topology.images"));
        }
        else
        {
            Duplicate(
                topology.Images.Where(image => image is not null),
                image => $"{image.ComponentId ?? topology.Id}:{image.RegistryClass ?? string.Empty}",
                "image.duplicate",
                "topology.images",
                findings);
            foreach (var image in topology.Images)
            {
                if (image is null)
                {
                    findings.Add(new("image.null", "Topology images cannot contain null items.", "topology.images"));
                    continue;
                }

                ValidateImage(image, topology.Id, findings);
            }
        }

        ValidateSupplyChain(topology.SupplyChain, topology.Id, findings);
    }

    private static void ValidateImage(ReleaseManifestImage image, string topologyId, List<ReleaseManifestAdmissionFinding> findings)
    {
        Required(image.RegistryClass, "image.registryClass.required", "Image registry class is required.", "image", findings);
        Required(image.Reference, "image.reference.required", "Image reference is required.", "image", findings);
        Required(image.IndexDigest, "image.indexDigest.required", "Image index digest is required.", "image", findings);

        if (!IsDigest(image.IndexDigest))
            findings.Add(new("image.indexDigest.invalid", "Image index identity must use a sha256 digest.", "image"));

        if (!string.IsNullOrWhiteSpace(image.Reference))
        {
            if (!IsSafeImageReference(image.Reference))
                findings.Add(new("image.reference.invalid", "Image references must be safe locators without credentials or mutable query state.", "image.reference"));

            if (!IsImmutableImageReference(image.Reference))
                findings.Add(new("image.reference.immutableRequired", "Image references must use an immutable sha256 digest, not a tag.", "image.reference"));

            var referenceDigest = ExtractDigest(image.Reference);
            if (referenceDigest is not null && !string.Equals(referenceDigest, image.IndexDigest, StringComparison.OrdinalIgnoreCase))
                findings.Add(new("image.referenceDigest.mismatch", "Image reference digest must match the image index digest.", "image.reference"));
        }

        if (image.PlatformDigests is not null)
        {
            foreach (var platform in image.PlatformDigests)
            {
                Required(platform.Key, "image.platform.required", "Platform identity is required.", "image.platformDigests", findings);
                if (!IsDigest(platform.Value))
                    findings.Add(new("image.platformDigest.invalid", "Platform image identity must use a sha256 digest.", "image.platformDigests"));
            }
        }

        ValidateOptionalStrings(image.Roles, "image.role.required", "image.roles", findings);
        ValidateOptionalStrings(image.Capabilities, "image.capability.required", "image.capabilities", findings);
        if (image.Endpoints is not null)
        {
            foreach (var endpoint in image.Endpoints)
            {
                if (endpoint is null)
                {
                    findings.Add(new("image.endpoint.null", "Image endpoints cannot contain null items.", "image.endpoints"));
                    continue;
                }

                Required(endpoint.Name, "image.endpoint.name.required", "Endpoint name is required.", "image.endpoints", findings);
                Required(endpoint.Protocol, "image.endpoint.protocol.required", "Endpoint protocol is required.", "image.endpoints", findings);
                Required(endpoint.Visibility, "image.endpoint.visibility.required", "Endpoint visibility is required.", "image.endpoints", findings);
                if (endpoint.Port is < 1 or > 65535)
                    findings.Add(new("image.endpoint.port.invalid", "Endpoint port must be between 1 and 65535.", "image.endpoints"));
                if (!string.IsNullOrWhiteSpace(endpoint.Path) && !EndpointPathPolicy.IsSafe(endpoint.Path))
                    findings.Add(new("image.endpoint.path.invalid", "Endpoint paths must be safe relative paths.", "image.endpoints"));
            }
        }
    }

    private static void ValidateSupplyChain(ReleaseManifestSupplyChain? supplyChain, string topologyId, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (supplyChain is null)
        {
            findings.Add(new("supplyChain.required", "Retained release supply-chain evidence is required.", "supplyChain"));
            return;
        }

        ValidateAttestation(supplyChain.Sbom, "supplyChain.sbom.required", "supplyChain.sbom", topologyId, findings);
        ValidateAttestation(supplyChain.Provenance, "supplyChain.provenance.required", "supplyChain.provenance", topologyId, findings);
        if (supplyChain.Signatures is null || supplyChain.Signatures.Count == 0)
            findings.Add(new("supplyChain.signatures.required", "At least one retained signature evidence reference is required.", "supplyChain.signatures"));
        else
        {
            foreach (var signature in supplyChain.Signatures)
            {
                if (signature is null)
                {
                    findings.Add(new("supplyChain.signature.null", "Supply-chain signatures cannot contain null items.", "supplyChain.signatures"));
                    continue;
                }

                Required(signature.RegistryClass, "supplyChain.signature.registryClass.required", "Signature registry class is required.", "supplyChain.signatures", findings);
                Required(signature.Identity, "supplyChain.signature.identity.required", "Signature identity is required.", "supplyChain.signatures", findings);
                ValidateEvidenceReference(
                    signature.Uri,
                    signature.Digest,
                    "supplyChain.signature.reference.required",
                    "supplyChain.signature.reference.invalid",
                    "supplyChain.signatures",
                    findings);
            }
        }

        var scan = supplyChain.VulnerabilityScan;
        if (scan is null)
        {
            findings.Add(new("supplyChain.vulnerabilityScan.required", "Retained vulnerability-scan evidence is required.", "supplyChain.vulnerabilityScan"));
        }
        else
        {
            Required(scan.Tool, "supplyChain.vulnerabilityScan.tool.required", "Vulnerability scanner identity is required.", "supplyChain.vulnerabilityScan", findings);
            Required(scan.Policy, "supplyChain.vulnerabilityScan.policy.required", "Vulnerability policy identity is required.", "supplyChain.vulnerabilityScan", findings);
            ValidateEvidenceReference(
                scan.Report,
                scan.Digest,
                "supplyChain.vulnerabilityScan.reference.required",
                "supplyChain.vulnerabilityScan.reference.invalid",
                "supplyChain.vulnerabilityScan",
                findings);
        }
    }

    private static void ValidateAttestation(
        ReleaseManifestAttestation? attestation,
        string requiredCode,
        string scope,
        string topologyId,
        List<ReleaseManifestAdmissionFinding> findings)
    {
        if (attestation is null)
        {
            findings.Add(new(requiredCode, "Retained attestation evidence is required.", scope));
            return;
        }

        ValidateEvidenceReference(
            attestation.Uri,
            attestation.Digest,
            requiredCode,
            $"{scope}.invalid",
            scope,
            findings);
    }

    private static void ValidateEvidenceReference(
        string? reference,
        string? digest,
        string requiredCode,
        string invalidCode,
        string scope,
        List<ReleaseManifestAdmissionFinding> findings)
    {
        Required(reference, requiredCode, "Evidence reference is required.", scope, findings);
        if (!string.IsNullOrWhiteSpace(reference) && !IsSafeEvidenceReference(reference, digest))
            findings.Add(new(invalidCode, "Evidence must be an immutable safe locator with a sha256 digest.", scope));
        if (!string.IsNullOrWhiteSpace(digest) && !IsDigest(digest))
            findings.Add(new($"{invalidCode}.digest.invalid", "Evidence digest must use a sha256 digest.", scope));
    }

    private static void ValidateSelection(CommercialReleaseManifest manifest, ReleaseManifestAdmissionOptions options, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (manifest.Topologies is null || string.IsNullOrWhiteSpace(options.RegistryClass))
            return;

        var topology = string.IsNullOrWhiteSpace(options.TopologyId)
            ? manifest.Topologies.FirstOrDefault()
            : manifest.Topologies.FirstOrDefault(x => x is not null && string.Equals(x.Id, options.TopologyId, StringComparison.OrdinalIgnoreCase));
        if (topology is null)
        {
            findings.Add(new("selection.topology.notFound", "The requested release topology is not present in the signed manifest.", "options.topologyId"));
            return;
        }

        var imageGroups = topology.Images?.Where(x => x is not null).GroupBy(x => x.ComponentId ?? topology.Id, StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var group in imageGroups)
        {
            if (!group.Any(x => string.Equals(x.RegistryClass, options.RegistryClass, StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("selection.registryClass.notFound", "A selected topology component has no image for the requested registry class.", "selection.registryClass"));
        }

        if (topology.SupplyChain?.Signatures is not null
            && !topology.SupplyChain.Signatures.Any(x => x is not null && string.Equals(x.RegistryClass, options.RegistryClass, StringComparison.OrdinalIgnoreCase)))
            findings.Add(new("selection.signature.notFound", "The selected topology has no retained signature evidence for the requested registry class.", "selection.signature"));
    }

    private static void ValidateOptionalStrings(IReadOnlyList<string>? values, string code, string scope, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (values is null)
            return;
        foreach (var value in values)
            Required(value, code, "Values cannot be blank.", scope, findings);
    }

    private static void Duplicate<T>(IEnumerable<T>? values, Func<T, string> keySelector, string code, string scope, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (values is null)
            return;

        foreach (var group in values.Select(keySelector).Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            findings.Add(new(code, "Duplicate identities are not allowed.", scope));
    }

    private static void Required(string? value, string code, string message, string scope, List<ReleaseManifestAdmissionFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value))
            findings.Add(new(code, message, scope));
    }

    internal static bool IsDigest(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length == "sha256:".Length + 64
        && value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
        && value["sha256:".Length..].All(Uri.IsHexDigit);

    internal static string? ExtractDigest(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        var marker = reference.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;

        var digest = reference[(marker + 1)..].Trim();
        return IsDigest(digest) ? digest : null;
    }

    internal static bool IsSafeEvidenceReference(string reference, string? digest)
    {
        if (!IsSafeLocator(reference, requireAbsolute: true))
            return false;

        var embeddedDigest = ExtractDigest(reference);
        if (reference.Contains('@', StringComparison.Ordinal) && embeddedDigest is null)
            return false;
        if (embeddedDigest is not null && IsDigest(digest) && !string.Equals(embeddedDigest, digest, StringComparison.OrdinalIgnoreCase))
            return false;
        return IsDigest(digest) || embeddedDigest is not null;
    }

    internal static bool IsSafeRetainedReference(string? reference) =>
        !string.IsNullOrWhiteSpace(reference) && IsSafeLocator(reference, requireAbsolute: true);

    private static bool IsSafeLocator(string value, bool requireAbsolute)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.Contains('?', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal))
            return false;

        if (!Uri.TryCreate(value, requireAbsolute ? UriKind.Absolute : UriKind.RelativeOrAbsolute, out var uri))
            return false;
        if (requireAbsolute && string.IsNullOrWhiteSpace(uri.Scheme))
            return false;

        if (string.IsNullOrWhiteSpace(uri.Host) || string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
            return false;

        return string.IsNullOrEmpty(uri.UserInfo)
            && (string.Equals(uri.Scheme, "oci", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsImmutableImageReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        if (reference.Any(char.IsWhiteSpace) || reference.Contains('?', StringComparison.Ordinal) || reference.Contains('#', StringComparison.Ordinal))
            return false;

        var digest = ExtractDigest(reference);
        if (digest is null)
            return false;

        var name = reference[..reference.IndexOf('@')];
        var schemeMarker = name.IndexOf("://", StringComparison.Ordinal);
        if (schemeMarker >= 0)
            name = name[(schemeMarker + 3)..];
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var lastSlash = name.LastIndexOf('/');
        return name[(lastSlash + 1)..].IndexOf(':') < 0;
    }

    internal static bool IsSafeImageReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        if (reference.Any(char.IsWhiteSpace) || reference.Contains('?', StringComparison.Ordinal) || reference.Contains('#', StringComparison.Ordinal))
            return false;

        var schemeMarker = reference.IndexOf("://", StringComparison.Ordinal);
        if (schemeMarker >= 0)
        {
            // Image references are OCI artifacts, not arbitrary web locators;
            // only the oci:// scheme is accepted when a scheme is present.
            return reference[..schemeMarker].Equals("oci", StringComparison.OrdinalIgnoreCase)
                && IsSafeLocator(reference, requireAbsolute: true);
        }

        // Standard OCI image references are commonly written without a scheme.
        // Reject user-info-like forms and require a digest marker; the full
        // immutability check is performed separately.
        var digestMarker = reference.IndexOf('@');
        return digestMarker > 0
            && reference.IndexOf('@', digestMarker + 1) < 0
            && !reference.StartsWith("/", StringComparison.Ordinal)
            && !reference[..digestMarker].Contains("\\", StringComparison.Ordinal);
    }
}
