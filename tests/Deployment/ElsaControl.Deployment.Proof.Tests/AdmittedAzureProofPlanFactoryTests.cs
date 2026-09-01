using ElsaControl.Deployment.Azure;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseManifests;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class AdmittedAzureProofPlanFactoryTests
{
    private const string ManifestDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ImageDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SignatureDigest = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Creates_deterministic_submission_from_admitted_plan()
    {
        var factory = new AdmittedAzureProofPlanFactory(
            Resolution(), new("proof", "westeurope"), new string('c', 64), new string('d', 64), ["studio", "server"]);
        var selection = Selection();
        var environment = new DeploymentProofEnvironment("proof", "westeurope", "azure", []);

        var first = factory.Create(selection, environment);
        var second = factory.Create(selection, environment);

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(first.Plan.Fingerprint, second.Plan.Fingerprint);
        Assert.StartsWith("azure-proof:", first.IdempotencyKey);
        Assert.Equal(new string('c', 64), first.TemplateFingerprint);
        Assert.Equal(new string('d', 64), first.ProviderScopeFingerprint);
        Assert.Equal("3.8.0-preview.5413", first.Plan.ElsaVersion);
    }

    [Fact]
    public void Creates_submission_for_governed_north_europe_fallback()
    {
        var factory = new AdmittedAzureProofPlanFactory(
            Resolution(), new("proof", "northeurope"), new string('c', 64), new string('d', 64), ["studio", "server"]);

        var submission = factory.Create(
            Selection(), new DeploymentProofEnvironment("proof", "northeurope", "azure", []));

        Assert.Equal("northeurope", submission.Plan.Location);
    }

    [Fact]
    public void Rejects_selection_or_environment_mismatch_with_value_free_error()
    {
        var factory = new AdmittedAzureProofPlanFactory(
            Resolution(), new("proof", "westeurope"), new string('c', 64), new string('d', 64), ["studio", "server"]);

        var environmentError = Assert.Throws<DeploymentProofStageException>(() => factory.Create(
            Selection(), new("secret-environment", "eastus", "azure", [])));
        var selectionError = Assert.Throws<DeploymentProofStageException>(() => factory.Create(
            Selection() with { ElsaVersion = "secret-version" }, new("proof", "westeurope", "azure", [])));
        var featureError = Assert.Throws<DeploymentProofStageException>(() => factory.Create(
            Selection() with { Features = ["unadmitted"] }, new("proof", "westeurope", "azure", [])));

        Assert.Equal("azure.proof.environmentMismatch", environmentError.Code);
        Assert.Equal("azure.proof.planMismatch", selectionError.Code);
        Assert.Equal("azure.proof.authorityInvalid", featureError.Code);
        Assert.DoesNotContain("secret", environmentError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", selectionError.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DeploymentProofSelection Selection() => new(
        "selection", "3.8.0-preview.5413", "combined", ["studio", "server"],
        $"valenceruntimeimages.azurecr.io/runtime-combined@{ImageDigest}", ImageDigest);

    private static ElsaInstancePlanResolutionResult Resolution()
    {
        var plan = CreatePlan();
        var reference = new ElsaResolvedPlanReference(
            "proof-plan", 1, ManifestDigest, "https://proof.example.test/api/resolved-plans/proof-plan");
        return new(
            true,
            plan,
            reference,
            new ElsaCurrentResolvedRelease(
                reference, "valence-runtime", "3.8", "3.8.0-preview.5413", ManifestDigest,
                [new ElsaComponentDigest("runtime", ImageDigest)]),
            []);
    }

    private static ResolvedElsaApplicationPlan CreatePlan()
    {
        var component = new ResolvedElsaComponent(
            "runtime", ["studio", "server"],
            new("paid", "valenceruntimeimages.azurecr.io/runtime-combined", $"valenceruntimeimages.azurecr.io/runtime-combined@{ImageDigest}", ImageDigest),
            ["elsa.studio", "elsa.server"],
            [new("studio", "https", 8080, "public", true, "/"), new("api", "https", 8080, "public", true, "/elsa/api")],
            ["workflow.runtime", "workflow.studio"]);

        return new(
            ResolvedElsaApplicationPlanSchema.CurrentVersion,
            new("valence-runtime", "3.8", "3.8.0-preview.5413", "https://github.com/valence-works/elsa-production-image", "1aeee8df455b21cf3bf3d2b26dfbd512d76da27b", "oci://release-manifest.example/manifest", ManifestDigest),
            new("combined", [component]),
            [new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Elsa.Core", "3.8.0-preview.5413", ImageDigest, ["elsa.server"], [new("runtime", "Elsa.Runtime", ["elsa.server"], ["workflow.runtime"])])],
            new([new("Database:ConnectionString", "string", true, true, false, "ELSA_DATABASE_CONNECTION", null, "secret://vault/database-connection", null)]),
            new([new("runtime", 1, 1, 500, 1024)], [new("elsa-data", "relational", "persistent", "exclusive", 10)]),
            new("public", "unrestricted", false, [], [new("runtime", "api", "https", 443, "public", true, "/elsa/api")]),
            "Dedicated",
            new("preview", "Preview", "internal", "automatic-within-minor", "explicit-approval", "explicit-migration"),
            [new("managed-runtime", "Run the resolved runtime components.", true, ["container", "persistent-storage"])],
            [
                new(ReleaseManifestEvidenceKinds.Manifest, "oci://release-manifest.example/manifest", ManifestDigest, "Verified release manifest"),
                new(ReleaseManifestEvidenceKinds.Signature, "oci://release-manifest.example/signature", SignatureDigest, "Verified release manifest signature")
            ]);
    }
}
