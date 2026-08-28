using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.Deployment.Artifacts;

namespace ElsaControl.Deployment.Core.Tests;

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

    public static RegisterWorkspaceArtifactRequest ArtifactRegistration(
        string artifactId = "sha256:claims-prod",
        string reference = "/tmp/claims-prod",
        string referenceProvider = "local",
        Guid? actorAccountId = null) =>
        new(
            artifactId,
            "elsa-control/deployment-artifact/v1alpha1",
            new WorkspaceArtifactDigest("sha256", "claims-prod"),
            WorkspaceArtifactFormat.Folder,
            referenceProvider,
            reference,
            new WorkspaceArtifactManifestSummary("claims", "1.0.0", "prod"),
            [ArtifactResource()],
            [],
            actorAccountId ?? Guid.Parse("20000000-0000-0000-0000-000000000001"));

    public static WorkspaceArtifactResourceSummary ArtifactResource(
        string type = ArtifactTypeIds.ElsaLoomRecipe,
        string logicalId = "payment-retry") =>
        new(type, logicalId, null, "8", new WorkspaceArtifactDigest("sha256", "workflow-hash"));

    public static ArtifactProducer StudioArtifactProducer() =>
        new("studio", "Elsa Studio", "4.0.0", "workflow:claims");

    public static ArtifactDisplayMetadata ArtifactDisplayMetadata() =>
        new(
            "claims",
            "1.0.0",
            "Claims workflow",
            new Dictionary<string, string> { ["domain"] = "claims" },
            new Dictionary<string, string>());

    public static IReadOnlyList<ArtifactCompatibilityHint> LoomRecipeCompatibilityHints() =>
    [
        new ArtifactCompatibilityHint(
            ArtifactTypeIds.ElsaLoomRecipe,
            "elsa-workflows",
            ">=4.0.0",
            ["loom.recipe.apply"],
            new Dictionary<string, string>())
    ];
}
