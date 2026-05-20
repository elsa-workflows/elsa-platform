using Elsa.Platform.PackageCatalog.Abstractions.Deployment;
using FluentAssertions;

namespace Elsa.Platform.PackageCatalog.Abstractions.Tests;

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

        result.HasErrors.Should().BeTrue();
        result.Resolutions.Single().Approval.Should().Be(PackageApprovalState.Rejected);
        result.Resolutions.Single().Trust.Should().Be(PackageTrustState.Trusted);
        result.Resolutions.Single().Suspicion.Should().Be(PackageSuspicionState.Suspicious);
        result.Resolutions.Single().Compatibility.Should().Be(PackageCompatibilityState.Incompatible);
        result.Findings.Select(x => x.Category).Should().Contain([
            DeploymentPackageFindingCategory.Approval,
            DeploymentPackageFindingCategory.Suspicion,
            DeploymentPackageFindingCategory.Compatibility
        ]);
    }

    [Fact]
    public void Finding_categories_cover_required_deployment_validation_states()
    {
        Enum.GetValues<DeploymentPackageFindingCategory>().Should().Contain([
            DeploymentPackageFindingCategory.Discovery,
            DeploymentPackageFindingCategory.ManifestValidation,
            DeploymentPackageFindingCategory.Approval,
            DeploymentPackageFindingCategory.Trust,
            DeploymentPackageFindingCategory.Suspicion,
            DeploymentPackageFindingCategory.Compatibility,
            DeploymentPackageFindingCategory.Feature,
            DeploymentPackageFindingCategory.Conflict
        ]);
    }

    [Fact]
    public void Deployment_package_catalog_contract_is_async_and_transport_agnostic()
    {
        var method = typeof(IDeploymentPackageCatalog).GetMethod(nameof(IDeploymentPackageCatalog.ValidateRequirementsAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<DeploymentPackageValidationResult>));
        method.GetParameters().Select(x => x.ParameterType).Should().Equal(typeof(DeploymentPackageValidationRequest), typeof(CancellationToken));
    }
}
