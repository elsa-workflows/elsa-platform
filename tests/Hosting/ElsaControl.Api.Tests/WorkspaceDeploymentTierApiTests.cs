using System.Net;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceDeploymentTierApiTests
{
    [Fact]
    public async Task Owner_can_list_capabilities_and_default_tiers()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var capabilitiesResponse = await owner.GetAsync($"/api/workspaces/{workspaceId}/deployments/tier-capabilities");
        var tiersResponse = await owner.GetAsync($"/api/workspaces/{workspaceId}/deployments/tiers");
        var capabilities = await capabilitiesResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentTierCapabilitiesResponse>();
        var tiers = await tiersResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentTiersResponse>();

        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);
        Assert.Contains(capabilities!.Capabilities, x => x.Id == DeploymentTierCapabilities.ProductionLike);
        Assert.Equal(HttpStatusCode.OK, tiersResponse.StatusCode);
        Assert.Equal(4, tiers!.Tiers.Count());
        Assert.Contains(tiers.Tiers, x => x.Name == EnvironmentTier.Production.ToString() && x.IsDefault);
    }

    [Fact]
    public async Task Owner_can_create_update_preview_archive_and_restore_tier()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-admin");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var createResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers",
            new WorkspaceDeploymentTierRequest(
                "UAT",
                "User acceptance",
                50,
                [DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget]));
        var created = await createResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentTier>();
        var impactResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers/{created!.Id}/impact-preview",
            new WorkspaceDeploymentTierImpactPreviewRequest(
                [DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.SecretVerificationRequired]));
        var updateResponse = await owner.PutControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers/{created.Id}",
            new WorkspaceDeploymentTierRequest(
                "UAT",
                "Final validation",
                55,
                [DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget, DeploymentTierCapabilities.SecretVerificationRequired],
                ImpactAccepted: true));
        var archiveResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/deployments/tiers/{created.Id}/archive", null);
        var restoreResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/deployments/tiers/{created.Id}/restore", null);
        var restored = await restoreResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentTier>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, impactResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        Assert.Equal(DeploymentTierStatus.Active, restored!.Status);
        Assert.Contains(DeploymentTierCapabilities.SecretVerificationRequired, restored.Capabilities);
    }

    [Fact]
    public async Task Duplicate_tier_names_and_non_admin_mutations_are_rejected()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "tier-reader", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.Read);
        var reader = app.CreateTrustedWorkspaceClient("tier-reader");
        var request = new WorkspaceDeploymentTierRequest("UAT", null, 20, [DeploymentTierCapabilities.TestLike]);

        var created = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/tiers", request);
        var duplicate = await owner.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/tiers", request);
        var denied = await reader.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/tiers", request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Environment_create_update_uses_tier_id_and_cockpit_returns_tier_shape()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-env-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var tier = await CreateTierAsync(owner, workspaceId, "Production EU", DeploymentTierCapabilities.ProductionLike, DeploymentTierCapabilities.PromotionTarget);

        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Prod EU", EnvironmentTier.Production, tier.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.Created, environmentResponse.StatusCode);
        Assert.Equal(tier.Id, environment!.TierId);
        Assert.Single(cockpit!.Applications.Single().Environments, x =>
            x.Name == "Prod EU"
            && x.TierName == "Production EU"
            && x.TierCapabilities != null
            && x.TierCapabilities.Contains(DeploymentTierCapabilities.ProductionLike));
    }

    [Fact]
    public async Task Archived_tier_assignment_is_rejected()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-archive-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var tier = await CreateTierAsync(owner, workspaceId, "Certification", DeploymentTierCapabilities.PreproductionLike);
        await owner.PostAsync($"/api/workspaces/{workspaceId}/deployments/tiers/{tier.Id}/archive", null);
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();

        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Cert", EnvironmentTier.Stage, tier.Id));

        Assert.Equal(HttpStatusCode.Conflict, environmentResponse.StatusCode);
    }

    [Fact]
    public async Task Legacy_environment_requests_map_to_default_tiers_during_transition()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-legacy-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();

        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production, null));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        var tiers = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var production = tiers!.Tiers.Single(x => x.Name == EnvironmentTier.Production.ToString());

        Assert.Equal(HttpStatusCode.Created, environmentResponse.StatusCode);
        Assert.Equal(production.Id, environment!.TierId);
        Assert.Equal(EnvironmentTier.Production.ToString(), environment.TierDefinition!.Name);
        Assert.Contains(DeploymentTierCapabilities.ProductionLike, environment.TierDefinition.Capabilities);
    }

    [Fact]
    public async Task Promotion_preview_uses_tier_capabilities_rather_than_names()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-preview-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var sourceTier = await CreateTierAsync(owner, workspaceId, "Build Output", DeploymentTierCapabilities.DevelopmentLike);
        var targetTier = await CreateTierAsync(owner, workspaceId, "Customer Acceptance", DeploymentTierCapabilities.ProductionLike);
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var sourceEnvironment = await CreateEnvironmentAsync(owner, workspaceId, application!.Id, "Build", EnvironmentTier.Dev, sourceTier.Id);
        var targetEnvironment = await CreateEnvironmentAsync(owner, workspaceId, application.Id, "Accept", EnvironmentTier.Production, targetTier.Id);
        var engine = await RegisterEngineAsync(owner, workspaceId, targetEnvironment.Id);
        var revision = await CreateRevisionAsync(owner, workspaceId, application.Id, sourceEnvironment.Id);

        var previewResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions/preview",
            new WorkspacePromotionPreviewRequestDto(sourceEnvironment.Id, targetEnvironment.Id, revision.Id, engine.Id));
        var preview = await previewResponse.Content.ReadControlJsonAsync<PromotionComparison>();

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Contains(preview!.Validations, x => x.Id == "deployment.tier.source.unsupported");
        Assert.Contains(preview.Validations, x => x.Id == "deployment.tier.target.unsupported");
        Assert.Contains(preview.Validations, x => x.Id == "deployment.tier.production-like");
    }

    private static async Task<WorkspaceDeploymentTier> CreateTierAsync(HttpClient client, Guid workspaceId, string name, params string[] capabilities)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers",
            new WorkspaceDeploymentTierRequest(name, null, 90, capabilities));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentTier>())!;
    }

    private static async Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        HttpClient client,
        Guid workspaceId,
        Guid applicationId,
        string name,
        EnvironmentTier legacyTier,
        Guid tierId)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments",
            new WorkspaceDeploymentEnvironmentRequest(name, legacyTier, tierId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>())!;
    }

    private static async Task<WorkspaceWorkflowEngine> RegisterEngineAsync(HttpClient client, Guid workspaceId, Guid environmentId)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/environments/{environmentId}/engines",
            new WorkspaceWorkflowEngineRequest(
                "claims-target",
                "https://workflows.example.test/elsa",
                null,
                "Azure Key Vault",
                "kv://claims/target",
                [],
                [],
                null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>())!;
    }

    private static async Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(HttpClient client, Guid workspaceId, Guid applicationId, Guid environmentId)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments/{environmentId}/revisions",
            new WorkspaceDesiredStateRevisionRequest("Candidate", null, []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>())!;
    }
}
