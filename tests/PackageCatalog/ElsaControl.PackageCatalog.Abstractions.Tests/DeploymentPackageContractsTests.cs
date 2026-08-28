using ElsaControl.PackageCatalog.Abstractions.Deployment;

namespace ElsaControl.PackageCatalog.Abstractions.Tests;

public sealed class DeploymentPackageContractsTests
{
    [Fact]
    public void Validation_result_keeps_governance_signals_distinct()
    {
        var requirement = new DeploymentPackageRequirement("Elsa.Email", Version: "1.0.0", Features: ["email"]);
        var result = new DeploymentPackageValidationResult(
            false,
            [
                new DeploymentPackageResolution(
                    requirement,
                    Guid.NewGuid(),
                    "1.0.0",
                    PackageManifestValidationState.Valid,
                    PackageApprovalState.Rejected,
                    PackageTrustState.Trusted,
                    PackageSuspicionState.Suspicious,
                    PackageCompatibilityState.Incompatible)
            ],
            [
                new DeploymentPackageFinding(DeploymentPackageFindingSeverity.Error, DeploymentPackageFindingCategory.Approval, "package.rejected", "Package is rejected.", "Elsa.Email", "1.0.0"),
                new DeploymentPackageFinding(DeploymentPackageFindingSeverity.Warning, DeploymentPackageFindingCategory.Suspicion, "package.suspicious", "Package has a suspicious manifest change.", "Elsa.Email", "1.0.0"),
                new DeploymentPackageFinding(DeploymentPackageFindingSeverity.Error, DeploymentPackageFindingCategory.Compatibility, "package.incompatible", "Package is incompatible with the target runtime.", "Elsa.Email", "1.0.0")
            ]);

        Assert.True(result.HasErrors);
        Assert.Equal(PackageApprovalState.Rejected, result.Resolutions.Single().Approval);
        Assert.Equal(PackageTrustState.Trusted, result.Resolutions.Single().Trust);
        Assert.Equal(PackageSuspicionState.Suspicious, result.Resolutions.Single().Suspicion);
        Assert.Equal(PackageCompatibilityState.Incompatible, result.Resolutions.Single().Compatibility);
        var categories = result.Findings.Select(x => x.Category);
        Assert.Contains(DeploymentPackageFindingCategory.Approval, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Suspicion, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Compatibility, categories);
    }

    [Fact]
    public void Finding_categories_cover_required_deployment_validation_states()
    {
        var categories = Enum.GetValues<DeploymentPackageFindingCategory>();
        Assert.Contains(DeploymentPackageFindingCategory.Discovery, categories);
        Assert.Contains(DeploymentPackageFindingCategory.ManifestValidation, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Approval, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Trust, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Suspicion, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Compatibility, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Feature, categories);
        Assert.Contains(DeploymentPackageFindingCategory.Conflict, categories);
    }

    [Fact]
    public void Deployment_package_catalog_contract_is_async_and_transport_agnostic()
    {
        var method = typeof(IDeploymentPackageCatalog).GetMethod(nameof(IDeploymentPackageCatalog.ValidateRequirementsAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DeploymentPackageValidationResult>), method!.ReturnType);
        Assert.Equal([typeof(DeploymentPackageValidationRequest), typeof(CancellationToken)], method.GetParameters().Select(x => x.ParameterType));
    }
}
