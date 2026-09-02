using ElsaControl.Deployment.Azure;

namespace ElsaControl.Deployment.Proof.Tests;

public sealed class Elsa38CombinedProofResolutionFactoryTests
{
    private const string ImageDigest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ManifestDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SignatureDigest = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Creates_translatable_typed_resolution_from_retained_admission_facts()
    {
        var result = Elsa38CombinedProofResolutionFactory.Create(Admission());

        var translated = AzureWorkloadPlanTranslator.Translate(result.Plan, new("proof", "westeurope"));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Reference);
        Assert.NotNull(result.CurrentResolvedRelease);
        Assert.Empty(translated.Findings);
        Assert.Equal("3.8.0-preview.5413", translated.Plan!.ElsaVersion);
        Assert.Equal(ImageDigest["sha256:".Length..], translated.Plan.ImageDigest);
        Assert.Equal(3, translated.Plan.SecretReferences.Count);
        Assert.DoesNotContain(result.Plan!.Packages, package =>
            package.PackageId is AzureWorkloadPlanTranslator.SqlWorkflowPackageId or AzureWorkloadPlanTranslator.SqlQuartzPackageId);
        Assert.Collection(
            result.Plan.Release.ComponentDeclarations!.Packages,
            package => Assert.Equal(AzureWorkloadPlanTranslator.SqlWorkflowPackageId, package.Id),
            package => Assert.Equal(AzureWorkloadPlanTranslator.SqlQuartzPackageId, package.Id));
    }

    [Fact]
    public void Rejects_tagged_image_or_unsafe_evidence()
    {
        Assert.Throws<ArgumentException>(() => Elsa38CombinedProofResolutionFactory.Create(
            Admission() with { ImageReference = "valenceruntimeimages.azurecr.io/runtime-combined:latest" }));
        Assert.Throws<ArgumentException>(() => Elsa38CombinedProofResolutionFactory.Create(
            Admission() with { SignatureReference = "https://user:secret@proof.example.test/signature" }));
    }

    private static Elsa38CombinedProofAdmission Admission() => new(
        "3.8.0-preview.5413",
        $"valenceruntimeimages.azurecr.io/runtime-combined@{ImageDigest}",
        ImageDigest,
        $"oci://proof.example.test/manifests/release@{ManifestDigest}",
        ManifestDigest,
        $"oci://proof.example.test/signatures/release@{SignatureDigest}",
        SignatureDigest,
        "1aeee8df455b21cf3bf3d2b26dfbd512d76da27b",
        ProofHostFeatureContract.Supported,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sql-connection"] = "secret://proof/sql-connection",
            ["identity-signing-key"] = "secret://proof/identity-signing-key",
            ["admin-password"] = "secret://proof/admin-password"
        });
}
