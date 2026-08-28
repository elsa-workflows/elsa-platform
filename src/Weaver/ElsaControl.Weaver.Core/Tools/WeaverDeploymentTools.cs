using ElsaControl.Deployment.Core.Cockpit;

namespace ElsaControl.Weaver.Core.Tools;

public sealed class WeaverDeploymentTools
{
    public WeaverDeploymentSummary SummarizeCockpit(DeploymentCockpit cockpit)
    {
        var environments = cockpit.Applications.SelectMany(application => application.Environments).ToList();
        var blockedEnvironments = environments
            .Where(environment => environment.DeploymentStatus == DeploymentStatus.Blocked)
            .Select(environment => new WeaverDeploymentEnvironmentIssue(environment.Id, environment.Name, "Deployment is blocked."))
            .ToList();
        var unhealthyEngines = cockpit.Engines
            .Where(engine => engine.Health is not DeploymentHealth.Healthy)
            .Select(engine => new WeaverDeploymentEngineIssue(engine.Id, engine.Name, engine.Health.ToString()))
            .ToList();
        var driftItems = cockpit.DriftReport
            .Take(10)
            .Select(item => new WeaverDeploymentDriftIssue(item.EnvironmentId, item.EngineId, item.Area, item.Action.ToString()))
            .ToList();

        return new WeaverDeploymentSummary(
            cockpit.Applications.Count,
            environments.Count,
            cockpit.Engines.Count,
            blockedEnvironments,
            unhealthyEngines,
            driftItems);
    }
}

public sealed record WeaverDeploymentSummary(
    int ApplicationCount,
    int EnvironmentCount,
    int EngineCount,
    IReadOnlyList<WeaverDeploymentEnvironmentIssue> BlockedEnvironments,
    IReadOnlyList<WeaverDeploymentEngineIssue> UnhealthyEngines,
    IReadOnlyList<WeaverDeploymentDriftIssue> Drift);

public sealed record WeaverDeploymentEnvironmentIssue(string EnvironmentId, string EnvironmentName, string Reason);

public sealed record WeaverDeploymentEngineIssue(string EngineId, string EngineName, string Health);

public sealed record WeaverDeploymentDriftIssue(string EnvironmentId, string EngineId, string Area, string Action);
