using System.Net;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.PackageCatalog.Core.Accounts;
using FluentAssertions;

namespace ValenceControl.Api.Tests;

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

        capabilitiesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        capabilities!.Capabilities.Should().Contain(x => x.Id == DeploymentTierCapabilities.ProductionLike);
        tiersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        tiers!.Tiers.Should().HaveCount(4);
        tiers.Tiers.Should().Contain(x => x.Name == EnvironmentTier.Production.ToString() && x.IsDefault);
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

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        impactResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        restored!.Status.Should().Be(DeploymentTierStatus.Active);
        restored.Capabilities.Should().Contain(DeploymentTierCapabilities.SecretVerificationRequired);
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

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

        environmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        environment!.TierId.Should().Be(tier.Id);
        cockpit!.Applications.Single().Environments.Should().ContainSingle(x =>
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

        environmentResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
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

        environmentResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        environment!.TierId.Should().Be(production.Id);
        environment.TierDefinition!.Name.Should().Be(EnvironmentTier.Production.ToString());
        environment.TierDefinition.Capabilities.Should().Contain(DeploymentTierCapabilities.ProductionLike);
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

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        preview!.Validations.Should().Contain(x => x.Id == "deployment.tier.source.unsupported");
        preview.Validations.Should().Contain(x => x.Id == "deployment.tier.target.unsupported");
        preview.Validations.Should().Contain(x => x.Id == "deployment.tier.production-like");
    }

    private static async Task<WorkspaceDeploymentTier> CreateTierAsync(HttpClient client, Guid workspaceId, string name, params string[] capabilities)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers",
            new WorkspaceDeploymentTierRequest(name, null, 90, capabilities));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
        response.StatusCode.Should().Be(HttpStatusCode.Created);
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
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>())!;
    }

    private static async Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(HttpClient client, Guid workspaceId, Guid applicationId, Guid environmentId)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{applicationId}/environments/{environmentId}/revisions",
            new WorkspaceDesiredStateRevisionRequest("Candidate", null, []));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>())!;
    }
}
