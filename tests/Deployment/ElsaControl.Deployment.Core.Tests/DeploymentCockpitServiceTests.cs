using ElsaControl.Deployment.Core.Cockpit;
using Xunit;

namespace ElsaControl.Deployment.Core.Tests;

public sealed class DeploymentCockpitServiceTests
{
    [Fact]
    public async Task Cockpit_exposes_workspace_scoped_environment_governance_without_secret_values()
    {
        var service = new DeploymentCockpitService(new InMemoryDeploymentCockpitStore());

        var cockpit = await service.GetCockpitAsync(Guid.Parse("00000000-0000-0000-0000-000000000010"));

        Assert.Single(cockpit.Applications, x => x.Id == "claims-ops");
        Assert.Contains(cockpit.Applications.SelectMany(x => x.Environments), x =>
            x.Id == "claims-prod"
            && x.Health == DeploymentHealth.Unreachable
            && x.DeploymentStatus == DeploymentStatus.Blocked);
        Assert.Contains(cockpit.Engines, x =>
            x.Id == "stage-engine"
            && x.Endpoint.Version == "Elsa 4.0.0"
            && x.CredentialReference.Provider == "Azure Key Vault"
            && x.CredentialReference.Reference == "kv://acme-control/stage/elsa-api");
        Assert.DoesNotContain(cockpit.Engines.Select(x => x.CredentialReference.Reference), reference =>
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

        Assert.Contains(comparison.Diff, x => x.Category == DiffCategory.SecretReferences);
        Assert.Contains(comparison.Diff, x => x.Category == DiffCategory.EngineBindings);
        Assert.Contains(comparison.Validations, x => x.Severity == ValidationSeverity.Blocker);
        Assert.Equal(39, comparison.RollbackRevision);
    }
}
