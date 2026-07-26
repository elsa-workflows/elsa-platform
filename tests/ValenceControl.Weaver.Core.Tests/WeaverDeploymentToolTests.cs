using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Weaver.Core.Tools;
using FluentAssertions;

namespace ValenceControl.Weaver.Core.Tests;

public sealed class WeaverDeploymentToolTests
{
    private readonly WeaverDeploymentTools _tools = new();

    [Fact]
    public void SummarizeCockpit_returns_bounded_deployment_issues()
    {
        var revision = new DesiredStateRevision("revision-1", 3, "abc123", "Candidate", DateTimeOffset.Parse("2026-06-07T12:00:00Z"));
        var cockpit = new DeploymentCockpit(
            [
                new WorkflowApplication(
                    "app-1",
                    "Claims",
                    "Acme",
                    [
                        new EnvironmentSummary("env-1", "Production", EnvironmentTier.Production, DeploymentHealth.Degraded, revision, 2, DeploymentStatus.Blocked, DriftStatus.DriftDetected, ["engine-1"])
                    ])
            ],
            [
                new WorkflowEngineRegistration(
                    "engine-1",
                    "claims-prod",
                    "env-1",
                    new EngineEndpointMetadata("https://engine.example", "eu", "1.0", CertificateStatus.Trusted),
                    new EngineCredentialReference("External", "kv://claims/prod", CredentialVerificationStatus.Verified, null),
                    DeploymentHealth.Unreachable,
                    null,
                    [],
                    [],
                    null)
            ],
            [],
            [],
            [],
            [new DriftReportItem("drift-1", "env-1", "engine-1", "RuntimeConfiguration", "desired", "observed", DriftAction.Review)],
            []);

        var summary = _tools.SummarizeCockpit(cockpit);

        summary.ApplicationCount.Should().Be(1);
        summary.EnvironmentCount.Should().Be(1);
        summary.EngineCount.Should().Be(1);
        summary.BlockedEnvironments.Should().ContainSingle(x => x.EnvironmentName == "Production");
        summary.UnhealthyEngines.Should().ContainSingle(x => x.EngineName == "claims-prod" && x.Health == "Unreachable");
        summary.Drift.Should().ContainSingle(x => x.Area == "RuntimeConfiguration");
    }
}
