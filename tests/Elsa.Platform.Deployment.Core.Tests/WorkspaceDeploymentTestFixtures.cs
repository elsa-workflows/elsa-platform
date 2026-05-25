using Elsa.Platform.Deployment.Core.Cockpit;

namespace Elsa.Platform.Deployment.Core.Tests;

internal static class WorkspaceDeploymentTestFixtures
{
    public static readonly Guid WorkspaceId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static EngineCapability Capability(
        string id = "engine.reload-configuration",
        string label = "Reload engine configuration",
        CapabilityBoundary boundary = CapabilityBoundary.EngineApi) =>
        new(id, label, boundary);

    public static RuntimeControl Control(
        string id = "reload-configuration",
        string capabilityId = "engine.reload-configuration",
        CapabilityBoundary boundary = CapabilityBoundary.EngineApi) =>
        new(id, "Reload Configuration", boundary, capabilityId, "Reloads engine API configuration from desired state.");

    public static DeploymentValidation BlockingValidation(
        string id = "secret-missing",
        string scope = "Secret references") =>
        new(id, ValidationSeverity.Blocker, scope, "A required secret reference is missing.");
}
