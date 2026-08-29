using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;
using ElsaControl.RuntimeBuilder.Core.ReleaseManifests;

namespace ElsaControl.RuntimeBuilder.Core.Tests;

public sealed class ReleaseManifestAdmissionTests
{
    [Fact]
    public async Task Known_good_manifest_projects_verified_release_topology_images_and_safe_evidence()
    {
        var imageDigest = Digest('b');
        var signatureDigest = Digest('c');
        var artifact = Artifact(imageDigest, releaseLine: "3.8");
        var verifier = new StubSignatureVerifier(new(
            true,
            "workflow://valence-works/elsa-production-image/release",
            artifact.Digest,
            $"oci://signatures/release@{signatureDigest}",
            signatureDigest));

        var admission = await new ReleaseManifestAdmissionService(verifier).AdmitAsync(
            artifact,
            new("workflow://valence-works/elsa-production-image/release"));

        Assert.True(admission.Accepted);
        Assert.Empty(admission.Findings);

        var projected = ReleaseManifestPlanProjector.Project(admission, CreatePlan());

        Assert.Equal("3.8", projected.Release.ReleaseLine);
        Assert.Equal("3.8.0-preview.5413", projected.Release.Version);
        Assert.Equal(artifact.Digest, projected.Release.ReleaseManifestDigest);
        Assert.Equal("combined", projected.Topology.Id);
        var component = Assert.Single(projected.Topology.Components);
        Assert.Equal(imageDigest, component.Image.Digest);
        Assert.Equal($"runtime/runtime@{imageDigest}", component.Image.Reference);
        Assert.Equal("runtime/runtime", component.Image.Repository);
        Assert.Equal("/elsa/api", Assert.Single(component.Endpoints).Path);
        Assert.Contains(projected.Evidence, x => x.Kind == ReleaseManifestEvidenceKinds.Manifest && x.Digest == artifact.Digest);
        Assert.Contains(projected.Evidence, x => x.Kind == ReleaseManifestEvidenceKinds.Signature && x.Digest == signatureDigest);
        Assert.Contains(projected.Evidence, x => x.Kind == ReleaseManifestEvidenceKinds.Sbom && x.Digest == Digest('d'));
        Assert.Contains(projected.Evidence, x => x.Kind == ReleaseManifestEvidenceKinds.Provenance && x.Digest == Digest('e'));
        Assert.Contains(projected.Evidence, x => x.Kind == ReleaseManifestEvidenceKinds.VulnerabilityScan && x.Digest == Digest('f'));
        Assert.DoesNotContain(projected.Evidence, x => x.Description.Contains("workflow://", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Same_schema_path_accepts_a_synthetic_later_release_line_without_major_version_branching()
    {
        var artifact = Artifact(Digest('b'), releaseLine: "5.0", releaseVersion: "5.0.0");
        var verifier = new StubSignatureVerifier(new(
            true,
            "subject",
            artifact.Digest,
            $"oci://signatures/release@{Digest('c')}",
            Digest('c')));

        var admission = await new ReleaseManifestAdmissionService(verifier).AdmitAsync(
            artifact,
            new("subject"));

        Assert.True(admission.Accepted);
        var projected = ReleaseManifestPlanProjector.Project(admission, CreatePlan());

        Assert.Equal("5.0", projected.Release.ReleaseLine);
        Assert.Equal("5.0.0", projected.Release.Version);
    }

    [Fact]
    public async Task Unknown_schema_is_rejected_before_projection()
    {
        var artifact = WithPayload(Artifact(Digest('b')), ManifestJson(schemaVersion: "2"));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Null(admission.Manifest);
        Assert.Null(admission.SignatureEvidence);
        Assert.Contains(admission.Findings, x => x.Code == "manifest.schema.unsupported");
        Assert.Throws<InvalidOperationException>(() => ReleaseManifestPlanProjector.Project(admission, CreatePlan()));
    }

    [Fact]
    public async Task Artifact_payload_digest_mismatch_is_rejected_before_verification()
    {
        var artifact = Artifact(Digest('b')) with
        {
            Digest = Digest('9'),
            Reference = $"oci://valence-runtime/release-manifest@{Digest('9')}"
        };
        var verifier = new StubSignatureVerifier(new(
            true,
            "subject",
            artifact.Digest,
            $"oci://signatures/release@{Digest('c')}",
            Digest('c')));

        var admission = await new ReleaseManifestAdmissionService(verifier).AdmitAsync(artifact, new("subject"));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "manifest.payloadDigest.mismatch");
        Assert.Equal(0, verifier.Calls);
        Assert.Null(admission.Manifest);
        Assert.Null(admission.SignatureEvidence);
    }

    [Fact]
    public async Task Invalid_options_do_not_invoke_signature_verifier()
    {
        var artifact = Artifact(Digest('b'));
        var verifier = new StubSignatureVerifier(new(
            true,
            "subject",
            artifact.Digest,
            $"oci://signatures/release@{Digest('c')}",
            Digest('c')));

        var admission = await new ReleaseManifestAdmissionService(verifier).AdmitAsync(artifact, new(""));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "signature.subject.expected.required");
        Assert.Equal(0, verifier.Calls);
        Assert.Null(admission.Manifest);
        Assert.Null(admission.SignatureEvidence);
    }

    [Fact]
    public async Task Artifact_reference_digest_mismatch_is_rejected()
    {
        var artifact = Artifact(Digest('b')) with
        {
            Reference = $"oci://valence-runtime/release-manifest@{Digest('9')}"
        };
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "manifest.referenceDigest.mismatch");
    }

    [Fact]
    public async Task Mutable_artifact_reference_without_digest_is_rejected()
    {
        var artifact = Artifact(Digest('b')) with
        {
            Reference = "oci://valence-runtime/release-manifest"
        };
        var verifier = new StubSignatureVerifier(new(
            true,
            "subject",
            artifact.Digest,
            $"oci://signatures/release@{Digest('c')}",
            Digest('c')));

        var admission = await new ReleaseManifestAdmissionService(verifier).AdmitAsync(artifact, new("subject"));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "manifest.referenceDigest.required");
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task Hostless_artifact_reference_is_rejected()
    {
        var artifact = Artifact(Digest('b'));
        artifact = artifact with { Reference = $"oci:///release-manifest@{artifact.Digest}" };
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "manifest.reference.invalid");
        Assert.Null(admission.Reference);
        Assert.Null(admission.Digest);
    }

    [Fact]
    public async Task Wrong_signature_subject_is_rejected()
    {
        var artifact = Artifact(Digest('b'));
        var admission = await Admit(artifact, new("expected-subject"), new(
            true, "different-subject", artifact.Digest, $"oci://signatures/release@{Digest('c')}", Digest('c')));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "signature.subject.mismatch");
    }

    [Fact]
    public async Task Unsigned_manifest_is_rejected()
    {
        var artifact = Artifact(Digest('b'));
        var admission = await Admit(artifact, verification: new(
            false, "subject", artifact.Digest, $"oci://signatures/release@{Digest('c')}", Digest('c')));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "signature.invalid");
    }

    [Fact]
    public async Task Signature_subject_digest_mismatch_is_rejected()
    {
        var artifact = Artifact(Digest('b'));
        var admission = await Admit(artifact, new("subject"), new(
            true, "subject", Digest('9'), $"oci://signatures/release@{Digest('c')}", Digest('c')));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "signature.subjectDigest.mismatch");
    }

    [Fact]
    public async Task Missing_retained_supply_chain_evidence_is_rejected()
    {
        var artifact = WithPayload(Artifact(Digest('b')), ManifestJson(includeEvidence: false));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.sbom.required");
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.provenance.required");
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.signatures.required");
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.vulnerabilityScan.required");
    }

    [Fact]
    public async Task Mutable_image_reference_and_digest_mismatch_are_rejected()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson(imageReference: $"oci://runtime/runtime:latest@{Digest('c')}", imageDigest: Digest('b')));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "image.reference.immutableRequired");
        Assert.Contains(admission.Findings, x => x.Code == "image.referenceDigest.mismatch");
    }

    [Fact]
    public async Task Image_reference_with_credentials_is_rejected()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson(imageReference: $"oci://user:secret@runtime/runtime@{Digest('b')}", imageDigest: Digest('b')));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "image.reference.invalid");
        Assert.Null(admission.Manifest);
        Assert.Null(admission.SignatureEvidence);
    }

    [Fact]
    public async Task Https_scheme_image_reference_is_rejected()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson(imageReference: $"https://runtime/runtime@{Digest('b')}", imageDigest: Digest('b')));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "image.reference.invalid");
        Assert.Null(admission.Manifest);
        Assert.Null(admission.SignatureEvidence);
    }

    [Fact]
    public async Task Hostless_image_reference_is_rejected()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson(imageReference: $"oci:///runtime/runtime@{Digest('b')}", imageDigest: Digest('b')));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "image.reference.invalid");
    }

    [Theory]
    [InlineData("?token=secret")]
    [InlineData("#mutable-fragment")]
    public async Task Image_reference_with_query_or_fragment_is_rejected(string suffix)
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson(imageReference: $"oci://runtime/runtime@{Digest('b')}{suffix}", imageDigest: Digest('b')));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "image.reference.invalid");
        Assert.Null(admission.Manifest);
        Assert.Null(admission.SignatureEvidence);
    }

    [Fact]
    public async Task Unsafe_retained_evidence_reference_is_rejected()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson().Replace(
                "oci://evidence/sbom@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
                "https://evidence.example/sbom?token=secret",
                StringComparison.Ordinal));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.sbom.invalid");
    }

    [Fact]
    public async Task Hostless_retained_evidence_reference_is_rejected()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson().Replace(
                $"oci://evidence/sbom@{Digest('d')}",
                $"oci:///evidence/sbom@{Digest('d')}",
                StringComparison.Ordinal));
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.sbom.invalid");
    }

    [Fact]
    public async Task Rejected_result_does_not_echo_unsafe_artifact_identifiers()
    {
        var artifact = Artifact(Digest('b'));
        artifact = artifact with { Reference = $"oci://user:secret@runtime/release-manifest@{artifact.Digest}" };
        var admission = await Admit(artifact);

        Assert.False(admission.Accepted);
        Assert.Null(admission.Reference);
        Assert.Null(admission.Digest);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(admission), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_evidence_reference_uses_a_stable_required_finding_code()
    {
        var payload = ManifestJson().Replace(
            $"\"uri\":\"oci://evidence/sbom@{Digest('d')}\",\"digest\"",
            "\"uri\":\"\",\"digest\"",
            StringComparison.Ordinal);
        var admission = await Admit(WithPayload(Artifact(Digest('b')), payload));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "supplyChain.sbom.required");
        Assert.DoesNotContain(admission.Findings, x => x.Code == "supplyChain.sbom.invalid.required");
    }

    [Theory]
    [InlineData("https://attacker.example.test/callback")]
    [InlineData("/elsa/api?token=secret")]
    [InlineData("/elsa/api/../admin")]
    public async Task Unsafe_topology_endpoint_paths_are_rejected(string path)
    {
        var payload = ManifestJson().Replace(
            "\"api\": \"/elsa/api\"",
            $"\"api\": \"{path}\"",
            StringComparison.Ordinal);
        var admission = await Admit(WithPayload(Artifact(Digest('b')), payload));

        Assert.False(admission.Accepted);
        Assert.Contains(admission.Findings, x => x.Code == "topology.endpoint.path.invalid");
    }

    [Fact]
    public async Task Unmodeled_payload_fields_do_not_cross_the_admission_boundary()
    {
        var artifact = WithPayload(
            Artifact(Digest('b')),
            ManifestJson().Replace(
                "\"schemaVersion\": \"1\",",
                "\"schemaVersion\": \"1\", \"unsafePayload\": \"secret-token\",",
                StringComparison.Ordinal));
        var admission = await Admit(artifact);

        Assert.True(admission.Accepted);
        Assert.NotNull(admission.SignatureEvidence);
        Assert.Equal($"oci://signatures/release@{Digest('c')}", admission.SignatureEvidence.Reference);
        Assert.Equal(Digest('c'), admission.SignatureEvidence.Digest);
        var serialized = JsonSerializer.Serialize(admission);
        Assert.DoesNotContain("secret-token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("subject", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("identity", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Projector_rejects_unsafe_unrelated_existing_evidence()
    {
        var admission = await Admit(Artifact(Digest('b')));
        var plan = CreatePlan() with
        {
            Evidence = [new("existing", "https://evidence.example/a?token=secret", Digest('a'), "Existing evidence")]
        };

        Assert.True(admission.Accepted);
        Assert.Throws<InvalidOperationException>(() => ReleaseManifestPlanProjector.Project(admission, plan));
    }

    [Fact]
    public async Task Projector_rejects_existing_evidence_with_mismatched_digest_binding()
    {
        var admission = await Admit(Artifact(Digest('b')));
        var plan = CreatePlan() with
        {
            Evidence = [new("existing", $"https://evidence.example/a@{Digest('a')}", Digest('b'), "Retained immutable evidence.")]
        };

        Assert.True(admission.Accepted);
        Assert.Throws<InvalidOperationException>(() => ReleaseManifestPlanProjector.Project(admission, plan));
    }

    [Fact]
    public async Task Projector_rejects_existing_evidence_with_unallowlisted_description()
    {
        var admission = await Admit(Artifact(Digest('b')));
        var plan = CreatePlan() with
        {
            Evidence = [new("existing", $"https://evidence.example/a@{Digest('a')}", Digest('a'), "Evidence supplied by customer.")]
        };

        Assert.True(admission.Accepted);
        Assert.Throws<InvalidOperationException>(() => ReleaseManifestPlanProjector.Project(admission, plan));
    }

    [Fact]
    public async Task Projector_retains_unrelated_evidence_with_fixed_description_and_digest_binding()
    {
        var admission = await Admit(Artifact(Digest('b')));
        var plan = CreatePlan() with
        {
            Evidence = [new("existing", $"https://evidence.example/a@{Digest('a')}", Digest('a'), "Retained immutable evidence.")]
        };

        var projected = ReleaseManifestPlanProjector.Project(admission, plan);

        Assert.Contains(projected.Evidence, evidence => evidence.Kind == "existing" && evidence.Digest == Digest('a'));
    }

    [Fact]
    public async Task Projector_drops_legacy_existing_evidence_with_missing_kind()
    {
        var admission = await Admit(Artifact(Digest('b')));
        var plan = CreatePlan() with
        {
            Evidence = [new(null!, "https://evidence.example/a", Digest('a'), "Legacy evidence")]
        };

        var projected = ReleaseManifestPlanProjector.Project(admission, plan);

        Assert.DoesNotContain(projected.Evidence, x => x.Reference == "https://evidence.example/a");
    }

    [Fact]
    public async Task Rejection_findings_never_echo_untrusted_identifiers_or_control_characters()
    {
        var payloads = new (string Payload, ReleaseManifestAdmissionOptions Options)[]
        {
            (ManifestJson(schemaVersion: "2\\r\\nschema-secret"), new("subject")),
            (ManifestJson().Replace("\"id\": \"combined\"", "\"id\": \"topology-secret\\r\\nforged\"", StringComparison.Ordinal), new("subject", TopologyId: "combined")),
            (ManifestJson().Replace("\"elsaCore\": \"3.8.0-preview.5413\"", "\"component-secret\\r\\nforged\": \"\"", StringComparison.Ordinal), new("subject")),
            (ManifestJson().Replace("\"registryClass\": \"paid\"", "\"registryClass\": \"registry-secret\\r\\nforged\"", StringComparison.Ordinal), new("subject"))
        };

        foreach (var testCase in payloads)
        {
            var admission = await Admit(WithPayload(Artifact(Digest('b')), testCase.Payload), testCase.Options);
            var serializedFindings = JsonSerializer.Serialize(admission.Findings);

            Assert.False(admission.Accepted);
            Assert.DoesNotContain("secret", serializedFindings, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("forged", serializedFindings, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(serializedFindings, "\r", StringComparison.Ordinal);
            Assert.DoesNotContain(serializedFindings, "\n", StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Null_images_emit_only_null_findings_without_duplicate_noise()
    {
        var payload = ManifestJson().Replace("\"images\": [{", "\"images\": [null, null, {", StringComparison.Ordinal);
        var admission = await Admit(WithPayload(Artifact(Digest('b')), payload));

        Assert.False(admission.Accepted);
        Assert.Equal(2, admission.Findings.Count(x => x.Code == "image.null"));
        Assert.DoesNotContain(admission.Findings, x => x.Code == "image.duplicate");
    }

    private static async Task<ReleaseManifestAdmissionResult> Admit(
        ReleaseManifestArtifact artifact,
        ReleaseManifestAdmissionOptions? options = null,
        ReleaseManifestSignatureVerification? verification = null)
    {
        verification ??= new(true, "subject", artifact.Digest, $"oci://signatures/release@{Digest('c')}", Digest('c'));
        return await new ReleaseManifestAdmissionService(new StubSignatureVerifier(verification)).AdmitAsync(
            artifact,
            options ?? new("subject"));
    }

    private static ReleaseManifestArtifact Artifact(
        string imageDigest,
        string releaseLine = "3.8",
        string releaseVersion = "3.8.0-preview.5413")
    {
        var payload = ManifestJson(
            releaseLine: releaseLine,
            releaseVersion: releaseVersion,
            imageReference: $"oci://runtime/runtime@{imageDigest}",
            imageDigest: imageDigest);
        var digest = PayloadDigest(payload);
        return new($"oci://valence-runtime/release-manifest@{digest}", digest, payload);
    }

    private static ReleaseManifestArtifact WithPayload(ReleaseManifestArtifact artifact, string payload)
    {
        var digest = PayloadDigest(payload);
        return artifact with
        {
            Reference = $"oci://valence-runtime/release-manifest@{digest}",
            Digest = digest,
            Payload = payload
        };
    }

    private static string ManifestJson(
        string schemaVersion = "1",
        string releaseLine = "3.8",
        string releaseVersion = "3.8.0-preview.5413",
        string imageReference = "oci://runtime/runtime@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        string imageDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        bool includeEvidence = true)
    {
        var evidence = includeEvidence
            ? "\"sbom\":{\"uri\":\"oci://evidence/sbom@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\",\"digest\":\"sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\"},\"provenance\":{\"uri\":\"oci://evidence/provenance@sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\",\"digest\":\"sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\"},\"signatures\":[{\"registryClass\":\"paid\",\"identity\":\"workflow\",\"uri\":\"oci://signatures/release@sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"digest\":\"sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\"}],\"vulnerabilityScan\":{\"tool\":\"trivy\",\"policy\":\"fixable-high-critical\",\"report\":\"oci://evidence/scan@sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",\"digest\":\"sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\"}"
            : "";

        return $$"""
        {
          "schemaVersion": "{{schemaVersion}}",
          "distribution": {
            "id": "valence-runtime",
            "generation": "elsa-3",
            "releaseLine": "{{releaseLine}}",
            "releaseVersion": "{{releaseVersion}}",
            "channel": "preview",
            "lifecycle": "Preview",
            "source": {
              "repository": "https://github.com/valence-works/elsa-production-image",
              "commit": "1aeee8df455b21cf3bf3d2b26dfbd512d76da27b",
              "workflow": ".github/workflows/build-and-push.yml",
              "runId": "33253333014"
            }
          },
          "topologies": [{
            "id": "combined",
            "runtimeKinds": ["elsa.server", "elsa.studio"],
            "images": [{
              "registryClass": "paid",
              "reference": "{{imageReference}}",
              "indexDigest": "{{imageDigest}}",
              "platformDigests": {"linux/amd64": "{{imageDigest}}"}
            }],
            "components": {"elsaCore": "{{releaseVersion}}"},
            "endpoints": {"api": "/elsa/api"},
            "compatibility": {"packageManifestSchema": "1.0", "runtimeCapabilities": ["workflow.runtime", "workflow.studio"]},
            "supplyChain": {{{evidence}}}
          }]
        }
        """;
    }

    private static ResolvedElsaApplicationPlan CreatePlan() => new(
        ResolvedElsaApplicationPlanSchema.CurrentVersion,
        new("placeholder", "placeholder", "placeholder", "https://example.invalid", new('a', 40), "oci://placeholder", Digest('a')),
        new("placeholder", [new ResolvedElsaComponent("placeholder", ["server"], new("paid", "oci://placeholder/runtime", $"oci://placeholder/runtime@{Digest('a')}", Digest('a')), ["elsa.server"], [], [])]),
        [],
        new([]),
        new([], []),
        new("public", "restricted", false, [], []),
        "Dedicated",
        new("preview", "Preview", "internal", "automatic-within-minor", "explicit-approval", "explicit-migration"),
        [],
        []);

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private static string PayloadDigest(string payload) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";

    private sealed class StubSignatureVerifier(ReleaseManifestSignatureVerification result) : IReleaseManifestSignatureVerifier
    {
        public int Calls { get; private set; }

        public ValueTask<ReleaseManifestSignatureVerification> VerifyAsync(ReleaseManifestArtifact artifact, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }
}
