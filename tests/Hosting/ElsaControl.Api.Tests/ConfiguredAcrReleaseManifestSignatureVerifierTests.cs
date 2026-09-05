using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ElsaControl.Api.ReleaseCatalog;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Api.Tests;

public sealed class ConfiguredAcrReleaseManifestSignatureVerifierTests
{
    [Fact]
    public async Task Multibyte_payload_over_byte_limit_is_rejected_before_encoding_or_registry_access()
    {
        var fixture = new Fixture();
        var artifact = fixture.Artifact with
        {
            Payload = new string('\u0800', ReleaseRegistryProtocol.MaximumManifestBytes / 3 + 1)
        };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var verification = fixture.Verifier.VerifyAsync(artifact);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(verification.IsCompletedSuccessfully);
        Assert.False((await verification).IsValid);
        Assert.Equal(0, fixture.Reader.Opens);
        Assert.True(allocated < ReleaseRegistryProtocol.MaximumManifestBytes,
            "Oversized payload rejection must not allocate its UTF-8 byte buffer.");
    }

    [Fact]
    public async Task Exact_subject_payload_and_retained_bundle_produce_only_bound_verification_facts()
    {
        var fixture = new Fixture();
        var result = await fixture.Verifier.VerifyAsync(fixture.Artifact);

        Assert.True(result.IsValid);
        Assert.Equal(Fixture.Identity, result.Subject);
        Assert.Equal(fixture.Artifact.Digest, result.SubjectDigest);
        Assert.Equal(fixture.EvidenceDigest, result.EvidenceDigest);
        Assert.Equal(Fixture.Reference(fixture.EvidenceDigest), result.EvidenceReference);
        Assert.Equal(fixture.Artifact.PayloadDigest, result.BoundPayloadDigest);
        Assert.Equal(Fixture.Issuer, result.OidcIssuer);
        Assert.Equal(1, fixture.BundleVerifier.Calls);
        Assert.True(fixture.Reader.Disposed);
    }

    [Theory]
    [InlineData("oci://other.azurecr.io/releases/manifest@")]
    [InlineData("https://registry.azurecr.io/releases/manifest@")]
    [InlineData("oci://user:secret@registry.azurecr.io/releases/manifest@")]
    [InlineData("oci://registry.azurecr.io/other/manifest@")]
    public async Task Unapproved_reference_is_rejected_before_registry_access(string prefix)
    {
        var fixture = new Fixture();
        var result = await fixture.Verifier.VerifyAsync(fixture.Artifact with { Reference = prefix + fixture.Artifact.Digest });

        Assert.False(result.IsValid);
        Assert.Equal(0, fixture.Reader.Opens);
        Assert.DoesNotContain("user:secret", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_payload_tampering_is_not_authorized_by_a_valid_subject(bool recomputeCallerHash)
    {
        var fixture = new Fixture();
        var tampered = fixture.Artifact with { Payload = "tampered" };
        if (recomputeCallerHash)
            tampered = tampered with { PayloadDigest = Fixture.Digest(Encoding.UTF8.GetBytes(tampered.Payload)) };

        Assert.False((await fixture.Verifier.VerifyAsync(tampered)).IsValid);
        Assert.Equal(0, fixture.BundleVerifier.Calls);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("evidence")]
    [InlineData("payload")]
    [InlineData("bundle")]
    public async Task Downloaded_bytes_must_match_their_immutable_digest(string corruptedPart)
    {
        var fixture = new Fixture();
        fixture.Reader.CorruptedPart = corruptedPart;

        Assert.False((await fixture.Verifier.VerifyAsync(fixture.Artifact)).IsValid);
        Assert.Equal(0, fixture.BundleVerifier.Calls);
        Assert.True(fixture.Reader.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_ambiguous_signature_evidence_fails_closed(int count)
    {
        var fixture = new Fixture();
        fixture.Reader.ReferrerCount = count;

        Assert.False((await fixture.Verifier.VerifyAsync(fixture.Artifact)).IsValid);
        Assert.Equal(0, fixture.BundleVerifier.Calls);
    }

    [Theory]
    [InlineData("wrong-artifact")]
    [InlineData("wrong-title")]
    [InlineData("wrong-evidence-subject")]
    [InlineData("duplicate-payload")]
    public async Task Signed_structural_mismatches_are_not_admitted(string defect)
    {
        var fixture = new Fixture(defect);

        Assert.False((await fixture.Verifier.VerifyAsync(fixture.Artifact)).IsValid);
        Assert.Equal(0, fixture.BundleVerifier.Calls);
    }

    [Fact]
    public async Task Failed_crypto_verification_does_not_return_evidence()
    {
        var fixture = new Fixture();
        fixture.BundleVerifier.Accept = false;
        var result = await fixture.Verifier.VerifyAsync(fixture.Artifact);

        Assert.False(result.IsValid);
        Assert.Empty(result.EvidenceReference);
        Assert.Empty(result.EvidenceDigest);
    }

    [Fact]
    public async Task Transport_failures_do_not_expose_exception_payloads()
    {
        var fixture = new Fixture();
        fixture.Reader.ThrowOnOpen = true;
        var result = await fixture.Verifier.VerifyAsync(fixture.Artifact);

        Assert.False(result.IsValid);
        Assert.DoesNotContain("private-token", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Caller_cancellation_propagates_before_access()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Verifier.VerifyAsync(fixture.Artifact, cancellation.Token));
        Assert.Equal(0, fixture.Reader.Opens);
    }

    private sealed class Fixture
    {
        public const string Identity = "https://github.com/example/producer/.github/workflows/release.yml@refs/heads/main";
        public const string Issuer = "https://token.actions.githubusercontent.com";
        public FakeReader Reader { get; }
        public FakeBundleVerifier BundleVerifier { get; }
        public ConfiguredAcrReleaseManifestSignatureVerifier Verifier { get; }
        public ReleaseManifestArtifact Artifact { get; }
        public string EvidenceDigest { get; }

        public Fixture(string? defect = null)
        {
            var payload = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"2.0\"}");
            var bundle = Encoding.UTF8.GetBytes("retained-bundle");
            var payloadDescriptor = new { mediaType = "application/json", digest = Digest(payload), size = payload.Length,
                annotations = new Dictionary<string, string> { ["org.opencontainers.image.title"] = defect == "wrong-title" ? "wrong.json" : "release/release-manifest.json" } };
            var subject = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, mediaType = ReleaseRegistryProtocol.ManifestMediaType,
                artifactType = defect == "wrong-artifact" ? "wrong" : ReleaseRegistryProtocol.ReleaseArtifactType,
                layers = defect == "duplicate-payload" ? new[] { payloadDescriptor, payloadDescriptor } : new[] { payloadDescriptor } });
            var subjectDigest = Digest(subject);
            var evidence = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, mediaType = ReleaseRegistryProtocol.ManifestMediaType,
                artifactType = ReleaseRegistryProtocol.BundleMediaType,
                subject = new { mediaType = ReleaseRegistryProtocol.ManifestMediaType, digest = defect == "wrong-evidence-subject" ? Digest([]) : subjectDigest, size = subject.Length },
                layers = new[] { new { mediaType = ReleaseRegistryProtocol.BundleMediaType, digest = Digest(bundle), size = bundle.Length } } });
            EvidenceDigest = Digest(evidence);
            Artifact = new(Reference(subjectDigest), subjectDigest, Encoding.UTF8.GetString(payload), Digest(payload));
            Reader = new(subject, evidence, payload, bundle);
            BundleVerifier = new(subject, bundle);
            Verifier = new(new("registry.azurecr.io", "releases/manifest", Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), [], TimeSpan.FromSeconds(10)), Reader, BundleVerifier, Identity, Issuer);
        }

        public static string Digest(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        public static string Reference(string digest) => $"oci://registry.azurecr.io/releases/manifest@{digest}";
    }

    private sealed class FakeReader(byte[] subject, byte[] evidence, byte[] payload, byte[] bundle) : IReleaseRegistryReader, IReleaseRegistrySession
    {
        public int Opens { get; private set; }
        public bool Disposed { get; private set; }
        public bool ThrowOnOpen { get; set; }
        public int ReferrerCount { get; set; } = 1;
        public string? CorruptedPart { get; set; }

        public ValueTask<IReleaseRegistrySession> OpenAsync(CancellationToken cancellationToken = default)
        {
            Opens++;
            if (ThrowOnOpen) throw new InvalidOperationException("private-token");
            return ValueTask.FromResult<IReleaseRegistrySession>(this);
        }

        public ValueTask<byte[]> ReadManifestAsync(string digest, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(digest == Fixture.Digest(subject) ? Bytes("subject", subject) : Bytes("evidence", evidence));

        public ValueTask<byte[]> ReadBlobAsync(string digest, int maximumBytes, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(digest == Fixture.Digest(payload) ? Bytes("payload", payload) : Bytes("bundle", bundle));

        public ValueTask<IReadOnlyList<ReleaseRegistryDescriptor>> ReadReferrersAsync(string subjectDigest, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ReleaseRegistryDescriptor>>(Enumerable.Repeat(
                new ReleaseRegistryDescriptor(ReleaseRegistryProtocol.ManifestMediaType, Fixture.Digest(evidence), evidence.Length, ReleaseRegistryProtocol.BundleMediaType), ReferrerCount).ToArray());

        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        private byte[] Bytes(string part, byte[] value) => CorruptedPart == part ? Encoding.UTF8.GetBytes("corrupted") : value;
    }

    private sealed class FakeBundleVerifier(byte[] expectedSubject, byte[] expectedBundle) : IReleaseManifestBundleVerifier
    {
        public int Calls { get; private set; }
        public bool Accept { get; set; } = true;

        public ValueTask<bool> VerifyAsync(ReadOnlyMemory<byte> subject, ReadOnlyMemory<byte> bundle, CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedSubject, subject.ToArray());
            Assert.Equal(expectedBundle, bundle.ToArray());
            Calls++;
            return ValueTask.FromResult(Accept);
        }
    }
}
