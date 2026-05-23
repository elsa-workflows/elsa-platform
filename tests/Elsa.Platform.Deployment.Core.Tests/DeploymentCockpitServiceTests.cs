using Elsa.Platform.Deployment.Core.Cockpit;
using FluentAssertions;
using Xunit;

namespace Elsa.Platform.Deployment.Core.Tests;

public sealed class DeploymentCockpitServiceTests
{
    [Fact]
    public async Task Cockpit_exposes_workspace_scoped_environment_governance_without_secret_values()
    {
        var service = new DeploymentCockpitService(new InMemoryDeploymentCockpitStore());

        var cockpit = await service.GetCockpitAsync(Guid.Parse("00000000-0000-0000-0000-000000000010"));

        cockpit.Applications.Should().ContainSingle(x => x.Id == "claims-ops");
        cockpit.Applications.SelectMany(x => x.Environments).Should().Contain(x =>
            x.Id == "claims-prod"
            && x.Health == DeploymentHealth.Unreachable
            && x.DeploymentStatus == DeploymentStatus.Blocked);
        cockpit.Engines.Should().Contain(x =>
            x.Id == "stage-engine"
            && x.Endpoint.Version == "Elsa 4.0.0"
            && x.CredentialReference.Provider == "Azure Key Vault"
            && x.CredentialReference.Reference == "kv://acme-platform/stage/elsa-api");
        cockpit.Engines.Select(x => x.CredentialReference.Reference).Should().NotContain(reference =>
            reference.Contains("password", StringComparison.OrdinalIgnoreCase)
            || reference.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Promotion_comparison_records_diff_validation_and_rollback_gates()
    {
        var service = new DeploymentCockpitService(new InMemoryDeploymentCockpitStore());

        var comparison = (await service.GetCockpitAsync(Guid.NewGuid()))
            .Comparisons
            .Single(x => x.SourceEnvironmentId == "claims-stage" && x.TargetEnvironmentId == "claims-prod");

        comparison.Diff.Should().Contain(x => x.Category == DiffCategory.SecretReferences);
        comparison.Diff.Should().Contain(x => x.Category == DiffCategory.EngineBindings);
        comparison.Validations.Should().Contain(x => x.Severity == ValidationSeverity.Blocker);
        comparison.RollbackRevision.Should().Be(39);
    }
}
