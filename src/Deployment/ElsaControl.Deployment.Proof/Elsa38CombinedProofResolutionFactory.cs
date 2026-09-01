using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Azure;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Deployment.Proof;

/// <summary>Safe retained admission facts needed to reconstruct the typed disposable proof plan.</summary>
public sealed record Elsa38CombinedProofAdmission(
    string Version,
    string ImageReference,
    string ImageDigest,
    string ManifestReference,
    string ManifestDigest,
    string SignatureReference,
    string SignatureDigest,
    string SourceCommit,
    IReadOnlyList<string> Features,
    IReadOnlyDictionary<string, string> SecretReferences);

/// <summary>
/// Reconstructs the typed Elsa 3.8 Combined resolution from immutable retained admission facts.
/// It accepts no manifest payload, signer identity, token, or credential material.
/// </summary>
public static class Elsa38CombinedProofResolutionFactory
{
    private const string DistributionId = "valence-runtime";
    private const string ReleaseLine = "3.8";
    private const string ImageRepository = "valenceruntimeimages.azurecr.io/runtime-combined";
    private const string ComponentDeclarationsFormat = "central-package-declarations-v1";
    private const string ComponentDeclarationsDigest = "sha256:1b12815e61c57e538729dc99f7fde637e9576e889d67e58a5928a0380ce7b482";
    private const string SqlPackageVersion = "3.8.0-preview.5413";

    public static ElsaInstancePlanResolutionResult Create(Elsa38CombinedProofAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        Validate(admission);

        var component = new ResolvedElsaComponent(
            "runtime",
            ["studio", "server"],
            new("paid", ImageRepository, admission.ImageReference, admission.ImageDigest),
            ["elsa.studio", "elsa.server"],
            [
                new("studio", "https", 8080, "public", true, "/"),
                new("api", "https", 8080, "public", true, "/elsa/api")
            ],
            ["workflow.runtime", "workflow.studio"]);

        var plan = new ResolvedElsaApplicationPlan(
            ResolvedElsaApplicationPlanSchema.CurrentVersion,
            new(DistributionId, ReleaseLine, admission.Version,
                "https://github.com/valence-works/elsa-production-image", admission.SourceCommit,
                admission.ManifestReference, admission.ManifestDigest,
                new(
                    ComponentDeclarationsFormat,
                    ComponentDeclarationsDigest,
                    [
                        new(AzureWorkloadPlanTranslator.SqlWorkflowPackageId, SqlPackageVersion),
                        new(AzureWorkloadPlanTranslator.SqlQuartzPackageId, SqlPackageVersion)
                    ])),
            new("combined", [component]),
            [
                new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Elsa.Core", admission.Version,
                    admission.ImageDigest, ["elsa.server"],
                    [new("runtime", "Elsa.Runtime", ["elsa.server"], ["workflow.runtime"])])
            ],
            new([
                new("Database:ConnectionString", "string", true, true, false, "ELSA_DATABASE_CONNECTION", null, admission.SecretReferences["sql-connection"], null),
                new("Identity:SigningKey", "string", true, true, false, "ELSA_IDENTITY_SIGNING_KEY", null, admission.SecretReferences["identity-signing-key"], null),
                new("Admin:Password", "string", true, true, false, "ELSA_ADMIN_PASSWORD", null, admission.SecretReferences["admin-password"], null)
            ]),
            new([new("runtime", 1, 1, 500, 1024)], [new("elsa-data", "relational", "persistent", "exclusive", 10)]),
            new("public", "unrestricted", false, [], [new("runtime", "api", "https", 443, "public", true, "/elsa/api")]),
            "Dedicated",
            new("preview", "Preview", "internal", "automatic-within-minor", "explicit-approval", "explicit-migration"),
            [new("managed-runtime", "Run the resolved runtime components.", true, ["container", "persistent-storage"])],
            [
                new(ReleaseManifestEvidenceKinds.Manifest, admission.ManifestReference, admission.ManifestDigest,
                    ReleaseManifestEvidenceContract.DescriptionFor(ReleaseManifestEvidenceKinds.Manifest)),
                new(ReleaseManifestEvidenceKinds.Signature, admission.SignatureReference, admission.SignatureDigest,
                    ReleaseManifestEvidenceContract.DescriptionFor(ReleaseManifestEvidenceKinds.Signature))
            ]).Normalize();

        var contentHash = ResolvedElsaApplicationPlanSerialization.ComputeContentHash(plan);
        var planId = $"proof-{contentHash["sha256:".Length..18]}";
        var reference = new ElsaResolvedPlanReference(
            planId, 1, contentHash, $"https://proof.invalid/api/resolved-plans/{planId}");
        var release = new ElsaCurrentResolvedRelease(
            reference, DistributionId, ReleaseLine, admission.Version, admission.ManifestDigest,
            [new ElsaComponentDigest("runtime", admission.ImageDigest)]);
        return new(true, plan, reference, release, []);
    }

    private static void Validate(Elsa38CombinedProofAdmission admission)
    {
        if (!(string.Equals(admission.Version, ReleaseLine, StringComparison.OrdinalIgnoreCase) ||
              admission.Version.StartsWith(ReleaseLine + ".", StringComparison.OrdinalIgnoreCase) ||
              admission.Version.StartsWith(ReleaseLine + "-", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(admission.ImageReference, $"{ImageRepository}@{admission.ImageDigest}", StringComparison.Ordinal) ||
            !ReleaseManifestEvidenceContract.IsDigest(admission.ImageDigest) ||
            !ReleaseManifestEvidenceContract.IsSafe(
                ReleaseManifestEvidenceKinds.Manifest, admission.ManifestReference, admission.ManifestDigest,
                ReleaseManifestEvidenceContract.DescriptionFor(ReleaseManifestEvidenceKinds.Manifest)) ||
            !ReleaseManifestEvidenceContract.IsSafe(
                ReleaseManifestEvidenceKinds.Signature, admission.SignatureReference, admission.SignatureDigest,
                ReleaseManifestEvidenceContract.DescriptionFor(ReleaseManifestEvidenceKinds.Signature)) ||
            admission.SourceCommit is not { Length: 40 } || !admission.SourceCommit.All(char.IsAsciiHexDigit) ||
            admission.Features is null ||
            !admission.Features.Order(StringComparer.Ordinal).SequenceEqual(
                ProofHostFeatureContract.Supported.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            admission.SecretReferences is null || admission.SecretReferences.Count != 3 ||
            new[] { "sql-connection", "identity-signing-key", "admin-password" }.Any(key =>
                !admission.SecretReferences.TryGetValue(key, out var reference) ||
                !AzureProviderOperationValidation.IsSafeSecretReference(reference)))
            throw new ArgumentException("The retained Elsa 3.8 proof admission facts are invalid.", nameof(admission));
    }
}

public static class ProofHostFeatureContract
{
    public static readonly IReadOnlyList<string> Supported =
    [
        "DefaultAuthentication", "Liquid", "StructuredLogs", "StructuredLogsDashboard",
        "ConsoleLogs", "ConsoleLogsDashboard", "OpenTelemetry"
    ];
}
