using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ElsaControl.Api.Workspace;
using ElsaControl.Deployment.Artifacts;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaControl.Api.Tests;

public sealed class WorkspaceDeploymentApiTests
{
    // Regression guard for the endpoint-filter refactor: the shared ApiExceptionMappingEndpointFilter
    // now maps service exceptions on handlers that previously had no try/catch (and therefore returned
    // 500). These two pin the normalized 400/409 contract so the mapping can't silently regress.
    [Fact]
    public async Task Creating_application_with_blank_name_returns_bad_request()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("blank-app-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var response = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("   ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_revision_for_missing_environment_returns_conflict()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("missing-env-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var response = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{Guid.NewGuid()}/environments/{Guid.NewGuid()}/revisions",
            new WorkspaceDesiredStateRevisionRequest("rev-1", null, []));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Workspace_member_can_read_persisted_deployment_cockpit()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient();
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await SeedDeploymentAsync(app, workspaceId);

        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var cockpit = await response.Content.ReadControlJsonAsync<DeploymentCockpit>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(cockpit!.Applications, x => x.Name == "Claims Operations");
        Assert.Single(cockpit.Engines, x =>
            x.Name == "claims-prod"
            && x.CredentialReference.Reference == "kv://claims/prod/elsa-api");
        Assert.Single(cockpit.ObservabilityBindings, x => x.Provider == "Azure Monitor");
        Assert.Single(cockpit.DriftReport, x => x.Area == "RuntimeConfiguration");
    }

    [Fact]
    public async Task Deployment_cockpit_route_rejects_non_members()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var member = app.CreateTrustedWorkspaceClient("member");
        var workspaceId = await member.GetDefaultWorkspaceIdAsync();
        await SeedDeploymentAsync(app, workspaceId);

        var anonymous = await app.CreateClient().GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        var nonMember = await app.CreateTrustedWorkspaceClient("other").GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonMember.StatusCode);
    }

    [Fact]
    public async Task Normal_dataset_cockpit_loads_under_three_seconds()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var client = app.CreateTrustedWorkspaceClient("large-workspace");
        var workspaceId = await client.GetDefaultWorkspaceIdAsync();
        await SeedNormalDatasetAsync(app, workspaceId);

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync($"/api/workspaces/{workspaceId}/deployments/cockpit");
        stopwatch.Stop();
        var cockpit = await response.Content.ReadControlJsonAsync<DeploymentCockpit>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
        Assert.Equal(25, cockpit!.Applications.Count());
        Assert.Equal(200, cockpit.Engines.Count());
    }

    [Fact]
    public async Task Owner_can_create_update_and_read_environment_tier_shape()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-environment-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var production = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Production.ToString());
        var uat = await CreateTierAsync(owner, workspaceId, "UAT", DeploymentTierCapabilities.PreproductionLike, DeploymentTierCapabilities.PromotionTarget);
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production, production.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        var updateResponse = await owner.PutControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{environment!.Id}",
            new WorkspaceDeploymentEnvironmentRequest("UAT", EnvironmentTier.Stage, uat.Id));
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.Created, environmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Single(cockpit!.Applications.Single().Environments, x =>
            x.Id == environment.Id.ToString("D")
            && x.TierName == "UAT"
            && x.TierCapabilities != null
            && x.TierCapabilities.Contains(DeploymentTierCapabilities.PreproductionLike));
    }

    [Fact]
    public async Task Desired_state_requirements_omit_observability_for_dev_tier()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("requirements-dev-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var dev = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Dev.ToString());
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Dev", EnvironmentTier.Dev, dev.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();

        var response = await owner.GetAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment!.Id}/desired-state-requirements");
        var requirements = await response.Content.ReadControlJsonAsync<WorkspaceDesiredStateRequirementsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Dev", requirements!.TierName);
        Assert.Contains(DeploymentTierCapabilities.DevelopmentLike, requirements.TierCapabilities);
        Assert.Empty(requirements.Requirements);
    }

    [Fact]
    public async Task Desired_state_requirements_include_observability_for_production_tier()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("requirements-prod-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var production = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Production.ToString());
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Prod", EnvironmentTier.Production, production.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();

        var response = await owner.GetAsync($"/api/workspaces/{workspaceId}/deployments/environments/{environment!.Id}/desired-state-requirements");
        var requirements = await response.Content.ReadControlJsonAsync<WorkspaceDesiredStateRequirementsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Production", requirements!.TierName);
        Assert.Contains(DeploymentTierCapabilities.ObservabilityRequired, requirements.TierCapabilities);
        Assert.Single(requirements.Requirements, x =>
            x.Id == DeploymentTierService.ObservabilityBindingRequirementId
            && x.RecordKind == DeploymentTierService.ObservabilityBindingRecordKind
            && x.ValidationId == DeploymentTierService.ObservabilityRequiredValidationId
            && x.Required
            && x.Applicability == DesiredStateRequirementApplicability.CurrentTier);
    }

    [Fact]
    public async Task Environment_assignment_rejects_archived_tiers()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("tier-archive-env-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var uat = await CreateTierAsync(owner, workspaceId, "UAT", DeploymentTierCapabilities.PreproductionLike);
        var archiveResponse = await owner.PostAsync($"/api/workspaces/{workspaceId}/deployments/tiers/{uat.Id}/archive", null);
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();

        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("UAT", EnvironmentTier.Stage, uat.Id));

        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, environmentResponse.StatusCode);
    }

    [Fact]
    public async Task Owner_can_create_environment_without_engine_registration()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("environment-only-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var testTier = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Test.ToString());
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Acme", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();

        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Test", EnvironmentTier.Test, testTier.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.Created, environmentResponse.StatusCode);
        Assert.Equal("Test", environment!.Name);
        Assert.Single(cockpit!.Applications.Single().Environments, x => x.Id == environment.Id.ToString("D") && x.EngineIds.Count == 0);
        Assert.Empty(cockpit.Engines);
    }

    [Fact]
    public async Task Owner_can_register_engine_with_registered_credential_reference()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("engine-credential-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var testTier = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Test.ToString());
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Acme", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Test", EnvironmentTier.Test, testTier.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        var storeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest("Control Key Vault", "Azure Key Vault", null));
        var store = await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>();
        var referenceResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores/{store!.Id}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest("Test engine API", "kv://acme/test/engine-api", null));
        var reference = await referenceResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentCredentialReference>();

        var engineResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/environments/{environment!.Id}/engines",
            new WorkspaceWorkflowEngineRequest(
                "test-weu-01",
                "https://test-engine.example.com",
                null,
                null,
                null,
                [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null,
                reference!.Id));
        var engine = await engineResponse.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>();
        var cockpit = await owner.GetControlJsonAsync<DeploymentCockpit>($"/api/workspaces/{workspaceId}/deployments/cockpit");

        Assert.Equal(HttpStatusCode.Created, storeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, referenceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, engineResponse.StatusCode);
        Assert.Equal("Azure Key Vault", engine!.CredentialProvider);
        Assert.Equal("kv://acme/test/engine-api", engine.CredentialReference);
        Assert.Equal(reference.Id, engine.CredentialReferenceId);
        Assert.Single(cockpit!.Engines, x =>
            x.Name == "test-weu-01"
            && x.CredentialReference.Provider == "Azure Key Vault"
            && x.CredentialReference.Reference == "kv://acme/test/engine-api");
    }

    [Fact]
    public async Task Owner_can_create_local_engine_credential_store_without_echoing_secret_values()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("local-secret-store-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var storeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest(
                "Local engine credentials",
                null,
                null,
                DeploymentSecretStoreType.LocalEncryptedDatabase));
        var store = await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>();
        var referenceResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores/{store!.Id}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest(
                "Dev engine API",
                "local://engine-credentials/dev-engine-api",
                null,
                "super-secret-token"));
        var body = await referenceResponse.Content.ReadAsStringAsync();
        var reference = await referenceResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentCredentialReference>();
        var rotateResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/credential-references/{reference!.Id}/rotate",
            new WorkspaceDeploymentCredentialReferenceRotateRequest("rotated-secret-token"));
        var rotated = await rotateResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentCredentialReference>();

        Assert.Equal(HttpStatusCode.Created, storeResponse.StatusCode);
        Assert.Equal(DeploymentSecretStoreType.LocalEncryptedDatabase, store!.Type);
        Assert.Equal("Local encrypted database", store.Provider);
        Assert.Equal(HttpStatusCode.Created, referenceResponse.StatusCode);
        Assert.Equal(DeploymentSecretStoreType.LocalEncryptedDatabase, reference!.SecretStoreType);
        Assert.True(reference.HasProtectedSecret);
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        Assert.True(rotated!.HasProtectedSecret);
        Assert.DoesNotContain("super-secret-token", body);
        Assert.DoesNotContain("rotated-secret-token", (await rotateResponse.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task External_engine_credential_stores_reject_secret_values()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("external-secret-store-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var storeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest(
                "Control Key Vault",
                null,
                null,
                DeploymentSecretStoreType.AzureKeyVault));
        var store = await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>();

        var referenceResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores/{store!.Id}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest(
                "Prod engine API",
                "kv://claims/prod/engine-api",
                null,
                "do-not-store-here"));

        Assert.Equal(HttpStatusCode.Conflict, referenceResponse.StatusCode);
    }

    [Fact]
    public async Task Owner_can_register_engine_with_credentials_deferred_and_inspect_reference_usage()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("deferred-engine-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var defaults = await owner.GetControlJsonAsync<WorkspaceDeploymentTiersResponse>($"/api/workspaces/{workspaceId}/deployments/tiers");
        var testTier = defaults!.Tiers.Single(x => x.Name == EnvironmentTier.Test.ToString());
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Acme", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application!.Id}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Test", EnvironmentTier.Test, testTier.Id));
        var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        var storeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest("Control Key Vault", null, null, DeploymentSecretStoreType.AzureKeyVault));
        var store = await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>();
        var referenceResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores/{store!.Id}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest("Test engine API", "kv://acme/test/engine-api", null));
        var reference = await referenceResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentCredentialReference>();

        var deferredResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/environments/{environment!.Id}/engines",
            new WorkspaceWorkflowEngineRequest(
                "test-weu-deferred",
                "https://deferred-engine.example.com",
                null,
                null,
                null,
                [],
                [],
                null,
                null,
                EngineCredentialAssignmentStatus.Deferred));
        var assignedResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/environments/{environment.Id}/engines",
            new WorkspaceWorkflowEngineRequest(
                "test-weu-assigned",
                "https://assigned-engine.example.com",
                null,
                null,
                null,
                [],
                [],
                null,
                reference!.Id));
        var deferred = await deferredResponse.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>();
        var assigned = await assignedResponse.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>();
        var reassignedResponse = await owner.PutControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/engines/{deferred!.Id}",
            new WorkspaceWorkflowEngineRequest(
                deferred.Name,
                deferred.BaseUrl,
                deferred.Region,
                null,
                null,
                [],
                [],
                deferred.HostingProvider,
                reference.Id,
                EngineCredentialAssignmentStatus.Assigned));
        var reassigned = await reassignedResponse.Content.ReadControlJsonAsync<WorkspaceWorkflowEngine>();
        var usage = await owner.GetControlJsonAsync<WorkspaceDeploymentCredentialReferenceUsageResponse>(
            $"/api/workspaces/{workspaceId}/deployments/credential-references/{reference.Id}/usage");

        Assert.Equal(HttpStatusCode.Created, deferredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, assignedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reassignedResponse.StatusCode);
        Assert.Equal(EngineCredentialAssignmentStatus.Assigned, assigned!.CredentialAssignmentStatus);
        Assert.Equal(EngineCredentialAssignmentStatus.Deferred, deferred.CredentialAssignmentStatus);
        Assert.Null(deferred.CredentialReferenceId);
        Assert.Empty(deferred.CredentialProvider);
        Assert.Empty(deferred.CredentialReference);
        Assert.Equal(EngineCredentialAssignmentStatus.Assigned, reassigned!.CredentialAssignmentStatus);
        Assert.Equal(reference.Id, reassigned.CredentialReferenceId);
        Assert.Contains(usage!.Items, x =>
            x.EngineName == "test-weu-assigned"
            && x.ApplicationName == "Acme"
            && x.EnvironmentName == "Test");
        Assert.Contains(usage.Items, x =>
            x.EngineName == "test-weu-deferred"
            && x.ApplicationName == "Acme"
            && x.EnvironmentName == "Test");
    }

    [Fact]
    public async Task Secret_store_and_credential_reference_reads_require_deployment_read_permission()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("secret-store-permission-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var storeResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest("Control Key Vault", "Azure Key Vault", null));
        var store = await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>();
        await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/secret-stores/{store!.Id}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest("Test engine API", "kv://acme/test/engine-api", null));
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "secret-store-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("secret-store-reader");

        var deniedStores = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/secret-stores");
        var deniedReferences = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/credential-references");
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.Read);
        var allowedStores = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/secret-stores");
        var allowedReferences = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/credential-references");

        Assert.Equal(HttpStatusCode.Forbidden, deniedStores.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedReferences.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowedStores.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowedReferences.StatusCode);
    }

    [Fact]
    public async Task Owner_can_create_desired_state_revision_and_preview_promotion()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("preview-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);

        var sourceRevisionResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{sourceEnvironment.Id}/revisions",
            new WorkspaceDesiredStateRevisionRequest(
                "Stage candidate",
                "stage123",
                [
                    Record(DesiredStateRecordKind.Workflow, "Payment Retry", "{\"version\":8}"),
                    Record(DesiredStateRecordKind.SecretReference, "Payment API", "{\"reference\":\"kv://claims/prod/payment-api\"}")
                ]));
        var targetRevision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, targetEnvironment.Id, "Prod baseline", "{\"records\":[{\"kind\":\"Workflow\",\"name\":\"Payment Retry\",\"payload\":{\"version\":7}}]}");
        var sourceRevision = await sourceRevisionResponse.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>();

        var previewResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions/preview",
            new WorkspacePromotionPreviewRequestDto(sourceEnvironment.Id, targetEnvironment.Id, sourceRevision!.Id, targetEngine.Id));
        var preview = await previewResponse.Content.ReadControlJsonAsync<PromotionComparison>();

        Assert.Equal(HttpStatusCode.Created, sourceRevisionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal(sourceRevision.RevisionNumber, preview!.SourceRevision);
        Assert.Equal(targetRevision.RevisionNumber, preview.TargetRevision);
        Assert.Contains(preview.Diff, x => x.Name == "Payment Retry" && x.Impact == DiffImpact.Changed);
        Assert.Contains(preview.Diff, x => x.Name == "Payment API" && x.Impact == DiffImpact.Added);
        Assert.Contains(preview.Validations, x => x.Severity == ValidationSeverity.Pass);
    }

    [Fact]
    public async Task Owner_can_list_and_fetch_application_revisions()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("revision-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, _) = await SeedPreviewTopologyAsync(app, workspaceId);
        var sourceRevisionResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{sourceEnvironment.Id}/revisions",
            new WorkspaceDesiredStateRevisionRequest(
                "Stage candidate",
                "stage123",
                [
                    Record(DesiredStateRecordKind.Workflow, "Payment Retry", "{\"version\":8}"),
                    Record(DesiredStateRecordKind.SecretReference, "Payment API", "{\"reference\":\"kv://claims/prod/payment-api\"}")
                ]));
        var targetRevision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, targetEnvironment.Id, "Prod baseline", "{\"records\":[{\"kind\":\"Workflow\",\"name\":\"Payment Retry\",\"payload\":{\"version\":7}}]}");
        var sourceRevision = await sourceRevisionResponse.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>();

        var list = await owner.GetControlJsonAsync<WorkspaceApplicationRevisionsResponse>(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/revisions");
        var detail = await owner.GetControlJsonAsync<WorkspaceDesiredStateRevisionDetail>(
            $"/api/workspaces/{workspaceId}/deployments/revisions/{sourceRevision!.Id}");

        Assert.Equal(HttpStatusCode.Created, sourceRevisionResponse.StatusCode);
        Assert.Equal(2, list!.Items.Count());
        Assert.Contains(list.Items, x => x.Revision.Id == sourceRevision.Id && x.EnvironmentName == sourceEnvironment.Name && x.IsCurrentDesired);
        Assert.Contains(list.Items, x => x.Revision.Id == targetRevision.Id && x.EnvironmentName == targetEnvironment.Name && x.IsCurrentDesired);
        Assert.Equal(sourceRevision.Id, detail!.Summary.Revision.Id);
        Assert.Contains(detail.Records, x => x.Kind == DesiredStateRecordKind.Workflow && x.Name == "Payment Retry");
        Assert.Contains(detail.Records, x => x.Kind == DesiredStateRecordKind.SecretReference && x.Name == "Payment API");
    }

    [Fact]
    public async Task Application_revision_reads_require_deployment_read_permission()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("revision-permission-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, _, _) = await SeedPreviewTopologyAsync(app, workspaceId);
        var revision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, sourceEnvironment.Id, "Stage candidate", "{\"records\":[]}");
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "revision-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("revision-reader");

        var deniedList = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/revisions");
        var deniedDetail = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/revisions/{revision.Id}");
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.Read);
        var allowedList = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/revisions");
        var allowedDetail = await reader.GetAsync($"/api/workspaces/{workspaceId}/deployments/revisions/{revision.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, deniedList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedDetail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowedList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowedDetail.StatusCode);
    }

    [Fact]
    public async Task Promotion_preview_requires_preview_permission_for_readers()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("preview-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var sourceRevision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, sourceEnvironment.Id, "Stage candidate", "{\"records\":[]}");
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "preview-reader", WorkspaceRole.Reader);
        var reader = app.CreateTrustedWorkspaceClient("preview-reader");
        var request = new WorkspacePromotionPreviewRequestDto(sourceEnvironment.Id, targetEnvironment.Id, sourceRevision.Id, targetEngine.Id);

        var denied = await reader.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/promotions/preview", request);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.PreviewPromotion);
        var allowed = await reader.PostControlJsonAsync($"/api/workspaces/{workspaceId}/deployments/promotions/preview", request);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Promotion_with_blank_label_returns_bad_request()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("promotion-label-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();

        var response = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions",
            new WorkspacePromotionRequestDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Owner_can_promote_artifact_backed_revision_and_queue_safe_command()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-promotion-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var artifactResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:payment-retry"));
        var artifact = await artifactResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>();
        Assert.NotNull(artifact);
        var registeredArtifact = artifact!;
        var sourceRevisionResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/applications/{application.Id}/environments/{sourceEnvironment.Id}/revisions",
            new WorkspaceDesiredStateRevisionRequest(
                "Stage artifact",
                "stage-artifact",
                [
                    ArtifactReferenceRecord(registeredArtifact),
                    Record(DesiredStateRecordKind.ObservabilityBinding, "OpenTelemetry", "{\"provider\":\"otlp\"}")
                ]));
        var sourceRevision = await sourceRevisionResponse.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>();

        var promotionResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/promotions",
            new WorkspacePromotionRequestDto(sourceEnvironment.Id, targetEnvironment.Id, sourceRevision!.Id, targetEngine.Id, "Promoted artifact", "prod-artifact"));
        Assert.True(promotionResponse.StatusCode == HttpStatusCode.Created, await promotionResponse.Content.ReadAsStringAsync());
        var promotion = await promotionResponse.Content.ReadControlJsonAsync<WorkspacePromotionResult>();
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, promotion!.TargetRevision.Id);
        var runResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(promotion.TargetRevision.Id, targetEnvironment.Id, targetEngine.Id, confirmation.Id, DeploymentRunMode.Apply));
        var command = await ReadQueuedCommandAsync(app, workspaceId, targetEngine.Id);

        Assert.Equal(HttpStatusCode.Created, artifactResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sourceRevisionResponse.StatusCode);
        Assert.Equal(targetEnvironment.Id, promotion.TargetRevision.EnvironmentId);
        Assert.Contains(registeredArtifact.ArtifactId, promotion.TargetRevision.DesiredStateJson);
        Assert.DoesNotContain("workflow definition payload", promotion.TargetRevision.DesiredStateJson);
        Assert.Equal(HttpStatusCode.Created, runResponse.StatusCode);
        Assert.NotNull(command.Artifact);
        Assert.Equal(registeredArtifact.Id, command.Artifact!.ArtifactRecordId);
        Assert.Equal(registeredArtifact.ContentDigest, command.Artifact.ContentDigest);
        Assert.Equal(promotion.TargetRevision.Id, command.Revision!.RevisionId);
    }

    [Fact]
    public async Task Deployment_run_rejects_artifact_digest_mismatch_before_consuming_confirmation()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-run-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var artifactResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:payment-retry"));
        var artifact = await artifactResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>();
        var revision = await CreateRevisionDirectAsync(
            app,
            workspaceId,
            application.Id,
            targetEnvironment.Id,
            "Digest mismatch",
            DesiredStateJson(ArtifactReferenceRecord(artifact!, new WorkspaceArtifactDigest("sha256", "wrong"))));
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revision.Id);

        var runResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, confirmation.Id, DeploymentRunMode.Apply));
        var storedConfirmation = await ReadConfirmationAsync(app, workspaceId, confirmation.Id);

        Assert.Equal(HttpStatusCode.Conflict, runResponse.StatusCode);
        Assert.Null(storedConfirmation!.UsedAt);
    }

    [Fact]
    public async Task Deployment_run_uses_artifact_type_default_capabilities_when_hints_are_empty()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-capability-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId, includeWorkflowCapability: false);
        var artifactResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration("sha256:payment-retry") with { CompatibilityHints = [] });
        var artifact = await artifactResponse.Content.ReadControlJsonAsync<WorkspaceArtifact>();
        var revision = await CreateRevisionDirectAsync(
            app,
            workspaceId,
            application.Id,
            targetEnvironment.Id,
            "Missing runtime capability",
            DesiredStateJson(ArtifactReferenceRecord(artifact!)));
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revision.Id);

        var runResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, confirmation.Id, DeploymentRunMode.Apply));
        var storedConfirmation = await ReadConfirmationAsync(app, workspaceId, confirmation.Id);

        Assert.Equal(HttpStatusCode.Conflict, runResponse.StatusCode);
        Assert.Null(storedConfirmation!.UsedAt);
    }

    [Fact]
    public async Task Owner_can_roll_back_to_artifact_backed_revision_and_queue_safe_command()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-rollback-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var artifactA = await RegisterWorkflowArtifactAsync(owner, workspaceId, "sha256:payment-retry-a");
        var artifactB = await RegisterWorkflowArtifactAsync(owner, workspaceId, "sha256:payment-retry-b");
        var revisionA = await CreateRevisionDirectAsync(
            app,
            workspaceId,
            application.Id,
            targetEnvironment.Id,
            "Known good artifact",
            DesiredStateJson(ArtifactReferenceRecord(artifactA)));
        var revisionB = await CreateRevisionDirectAsync(
            app,
            workspaceId,
            application.Id,
            targetEnvironment.Id,
            "Bad artifact",
            DesiredStateJson(ArtifactReferenceRecord(artifactB)));
        var deployAConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revisionA.Id);
        var deployAResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revisionA.Id, targetEnvironment.Id, targetEngine.Id, deployAConfirmation.Id, DeploymentRunMode.Apply));
        var deployA = await deployAResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentRun>();
        await CompleteRunAsync(app, workspaceId, deployA!.Id);
        var deployBConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revisionB.Id);
        var deployBResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revisionB.Id, targetEnvironment.Id, targetEngine.Id, deployBConfirmation.Id, DeploymentRunMode.Apply));
        var deployB = await deployBResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentRun>();
        await CompleteRunAsync(app, workspaceId, deployB!.Id);
        var rollbackConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Rollback, revisionA.Id);

        var rollbackResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/rollbacks",
            new WorkspaceRollbackRunRequestDto(revisionA.Id, targetEnvironment.Id, targetEngine.Id, rollbackConfirmation.Id, deployB.Id, DeploymentRunMode.Apply));
        var rollback = await rollbackResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentRun>();
        var detail = await owner.GetControlJsonAsync<WorkspaceDeploymentRunDetailResponse>($"/api/workspaces/{workspaceId}/deployments/runs/{rollback!.Id}");
        var command = Assert.Single(detail!.Commands);

        Assert.Equal(HttpStatusCode.Created, deployAResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, deployBResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rollbackResponse.StatusCode);
        Assert.Equal(deployB.Id, rollback.RollbackSourceRunId);
        Assert.Equal(DeploymentCommandAction.Rollback, command.Action);
        Assert.NotNull(command.Artifact);
        Assert.Equal(artifactA.Id, command.Artifact!.ArtifactRecordId);
        Assert.Equal(artifactA.ArtifactId, command.Artifact.ArtifactId);
        Assert.Equal(artifactA.ContentDigest, command.Artifact.ContentDigest);
    }

    [Fact]
    public async Task Artifact_backed_rollback_rejects_missing_artifact_before_consuming_confirmation()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("artifact-rollback-missing-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var revision = await CreateRevisionDirectAsync(
            app,
            workspaceId,
            application.Id,
            targetEnvironment.Id,
            "Missing rollback artifact",
            DesiredStateJson(MissingArtifactReferenceRecord()));
        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Rollback, revision.Id);

        var rollbackResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/rollbacks",
            new WorkspaceRollbackRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, confirmation.Id, Guid.NewGuid(), DeploymentRunMode.Apply));
        var storedConfirmation = await ReadConfirmationAsync(app, workspaceId, confirmation.Id);

        Assert.Equal(HttpStatusCode.Conflict, rollbackResponse.StatusCode);
        Assert.Null(storedConfirmation!.UsedAt);
    }

    [Fact]
    public async Task Owner_can_confirm_queue_inspect_and_rollback_deployment_run()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("run-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var revision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, targetEnvironment.Id, "Stage candidate", "{\"records\":[]}");

        var confirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revision.Id);
        var runResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, confirmation.Id, DeploymentRunMode.Apply));
        var run = await runResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentRun>();
        var detail = await owner.GetControlJsonAsync<WorkspaceDeploymentRunDetailResponse>($"/api/workspaces/{workspaceId}/deployments/runs/{run!.Id}");

        await CompleteRunAsync(app, workspaceId, run.Id);
        var rollbackConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Rollback, revision.Id);
        var rollbackResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/rollbacks",
            new WorkspaceRollbackRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, rollbackConfirmation.Id, run.Id, DeploymentRunMode.Apply));
        var rollback = await rollbackResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentRun>();

        Assert.Equal(HttpStatusCode.Created, runResponse.StatusCode);
        Assert.Equal(run.Id, detail!.Run.Id);
        Assert.Single(detail.History, x => x.Status == WorkspaceDeploymentRunStatus.Queued);
        Assert.Equal(HttpStatusCode.Created, rollbackResponse.StatusCode);
        Assert.Equal(run.Id, rollback!.RollbackSourceRunId);
    }

    [Fact]
    public async Task Deployment_run_confirmation_rejects_wrong_user_replay_and_expired_confirmation()
    {
        await using var app = new ControlApiTestApplication();
        await app.SeedAsync(_ => Task.CompletedTask);
        var owner = app.CreateTrustedWorkspaceClient("run-owner");
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var (application, sourceEnvironment, targetEnvironment, targetEngine) = await SeedPreviewTopologyAsync(app, workspaceId);
        var revision = await CreateRevisionDirectAsync(app, workspaceId, application.Id, targetEnvironment.Id, "Stage candidate", "{\"records\":[]}");
        var readerAccountId = await app.AddWorkspaceMemberAsync(workspaceId, "run-reader", WorkspaceRole.Reader);
        await app.GrantWorkspaceDeploymentPermissionAsync(workspaceId, readerAccountId, WorkspaceDeploymentPermissions.ExecuteDeployment);
        var reader = app.CreateTrustedWorkspaceClient("run-reader");

        var ownerConfirmation = await CreateConfirmationAsync(owner, workspaceId, ConfirmationActionType.Deploy, revision.Id);
        var wrongUser = await reader.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, ownerConfirmation.Id, DeploymentRunMode.Apply));
        var runResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, ownerConfirmation.Id, DeploymentRunMode.Apply));
        var run = await runResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentRun>();
        await CompleteRunAsync(app, workspaceId, run!.Id);
        var replay = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, ownerConfirmation.Id, DeploymentRunMode.Apply));
        var expiredConfirmationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/confirmations",
            new WorkspaceActionConfirmationRequest(ConfirmationActionType.Deploy, revision.Id.ToString("D"), 0));
        var expiredConfirmation = await expiredConfirmationResponse.Content.ReadControlJsonAsync<ActionConfirmation>();
        var expired = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/runs",
            new WorkspaceDeploymentRunRequestDto(revision.Id, targetEnvironment.Id, targetEngine.Id, expiredConfirmation!.Id, DeploymentRunMode.Apply));

        Assert.Equal(HttpStatusCode.Conflict, wrongUser.StatusCode);
        Assert.Equal(HttpStatusCode.Created, runResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
    }

    private static async Task SeedDeploymentAsync(ControlApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims Operations", null, null));
        var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var engine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                environment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                [
                    new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi),
                    new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)
                ],
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));
        await store.CreateRevisionAsync(workspaceId, new CreateDesiredStateRevisionRequest(application.Id, environment.Id, "Baseline", "abc123", "{\"records\":[]}", null));

        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Guid? correlatedRevisionId = null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ObservabilityBindings (Id, WorkspaceId, EnvironmentId, EngineId, Kind, Provider, Status, Scope, CorrelatedRevisionId, Sample)
            VALUES ({Guid.NewGuid()}, {workspaceId}, {environment.Id}, {engine.Id}, {"Logs"}, {"Azure Monitor"}, {"Connected"}, {"workspace:/prod"}, {correlatedRevisionId}, {"Imported status"});
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO DriftReportItems (Id, WorkspaceId, EnvironmentId, EngineId, Area, Desired, Observed, Action, DetectedAt)
            VALUES ({Guid.NewGuid()}, {workspaceId}, {environment.Id}, {engine.Id}, {"RuntimeConfiguration"}, {"Concurrency 32"}, {"Concurrency 16"}, {"Review"}, {DateTimeOffset.UtcNow.UtcTicks});
            """);
    }

    private static async Task<(WorkspaceDeploymentApplication Application, WorkspaceDeploymentEnvironment SourceEnvironment, WorkspaceDeploymentEnvironment TargetEnvironment, WorkspaceWorkflowEngine TargetEngine)> SeedPreviewTopologyAsync(
        ControlApiTestApplication app,
        Guid workspaceId,
        bool includeWorkflowCapability = true)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest("Claims Operations", null, null));
        var sourceEnvironment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Stage", EnvironmentTier.Stage));
        var targetEnvironment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, "Prod", EnvironmentTier.Production));
        var targetEngine = await store.RegisterEngineAsync(
            workspaceId,
            new RegisterWorkflowEngineRequest(
                targetEnvironment.Id,
                "claims-prod",
                "https://workflows.example.test/elsa",
                "westeurope",
                "Azure Key Vault",
                "kv://claims/prod/elsa-api",
                EngineCapabilities(includeWorkflowCapability),
                [new RuntimeControl("reload-configuration", "Reload Configuration", CapabilityBoundary.EngineApi, "engine.reload-configuration", "Reloads engine API configuration.")],
                null));

        return (application, sourceEnvironment, targetEnvironment, targetEngine);
    }

    private static IReadOnlyList<EngineCapability> EngineCapabilities(bool includeWorkflowCapability) =>
        includeWorkflowCapability
            ? [
                new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi),
                new EngineCapability("workflow-definition.apply", "Apply workflow definitions", CapabilityBoundary.EngineApi)
            ]
            : [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)];

    private static async Task<WorkspaceDesiredStateRevision> CreateRevisionDirectAsync(
        ControlApiTestApplication app,
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        string label,
        string desiredStateJson)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        return await store.CreateRevisionAsync(workspaceId, new CreateDesiredStateRevisionRequest(applicationId, environmentId, label, null, desiredStateJson, null));
    }

    private static async Task<ActionConfirmation> CreateConfirmationAsync(HttpClient client, Guid workspaceId, ConfirmationActionType actionType, Guid targetId)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/confirmations",
            new WorkspaceActionConfirmationRequest(actionType, targetId.ToString("D"), null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<ActionConfirmation>())!;
    }

    private static async Task<WorkspaceArtifact> RegisterWorkflowArtifactAsync(HttpClient client, Guid workspaceId, string artifactId)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/artifacts",
            WorkspaceDeploymentTestFixtures.WorkflowEnvelopeRegistration(artifactId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceArtifact>())!;
    }

    private static async Task<WorkspaceDeploymentTier> CreateTierAsync(HttpClient client, Guid workspaceId, string name, params string[] capabilities)
    {
        var response = await client.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId}/deployments/tiers",
            new WorkspaceDeploymentTierRequest(name, null, 90, capabilities));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadControlJsonAsync<WorkspaceDeploymentTier>())!;
    }

    private static async Task CompleteRunAsync(ControlApiTestApplication app, Guid workspaceId, Guid runId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();
        await store.UpdateRunStatusAsync(workspaceId, runId, WorkspaceDeploymentRunStatus.Succeeded, "Deployment run completed.", DateTimeOffset.UtcNow);
    }

    private static WorkspaceDesiredStateRecordRequest Record(DesiredStateRecordKind kind, string name, string payloadJson) =>
        new(kind, name, JsonSerializer.Deserialize<JsonElement>(payloadJson, ControlApiTestApplication.JsonOptions));

    private static WorkspaceDesiredStateRecordRequest ArtifactReferenceRecord(
        WorkspaceArtifact artifact,
        WorkspaceArtifactDigest? digest = null) =>
        Record(
            DesiredStateRecordKind.ArtifactReference,
            "Payment Retry",
            $$"""
            {
              "artifactRecordId": "{{artifact.Id:D}}",
              "artifactId": "{{artifact.ArtifactId}}",
              "artifactTypeId": "{{artifact.ArtifactTypeId}}",
              "contentDigest": {
                "algorithm": "{{(digest ?? artifact.ContentDigest).Algorithm}}",
                "value": "{{(digest ?? artifact.ContentDigest).Value}}"
              }
            }
            """);

    private static WorkspaceDesiredStateRecordRequest MissingArtifactReferenceRecord() =>
        Record(
            DesiredStateRecordKind.ArtifactReference,
            "Payment Retry",
            $$"""
            {
              "artifactRecordId": "{{Guid.NewGuid():D}}",
              "artifactId": "sha256:missing-payment-retry",
              "artifactTypeId": "{{ArtifactTypeIds.ElsaWorkflowDefinition}}",
              "contentDigest": {
                "algorithm": "sha256",
                "value": "missing"
              }
            }
            """);

    private static string DesiredStateJson(params WorkspaceDesiredStateRecordRequest[] records)
    {
        var items = records.Select(record => new
        {
            kind = record.Kind.ToString(),
            name = record.Name,
            payload = record.Payload
        });
        return JsonSerializer.Serialize(new { records = items }, ControlApiTestApplication.JsonOptions);
    }

    private static async Task<DeploymentCommand> ReadQueuedCommandAsync(
        ControlApiTestApplication app,
        Guid workspaceId,
        Guid engineId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentCommandStore>();
        return (await store.PollPendingCommandsAsync(workspaceId, engineId, 10, DateTimeOffset.UtcNow)).Single();
    }

    private static async Task<ActionConfirmation?> ReadConfirmationAsync(
        ControlApiTestApplication app,
        Guid workspaceId,
        Guid confirmationId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentMutationStore>();
        return await store.GetConfirmationAsync(workspaceId, confirmationId);
    }

    private static async Task SeedNormalDatasetAsync(ControlApiTestApplication app, Guid workspaceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceDeploymentStore>();
        for (var appIndex = 0; appIndex < 25; appIndex++)
        {
            var application = await store.CreateApplicationAsync(workspaceId, new CreateWorkflowApplicationRequest($"Application {appIndex:00}", null, null));
            for (var envIndex = 0; envIndex < 4; envIndex++)
            {
                var environment = await store.CreateEnvironmentAsync(workspaceId, new CreateDeploymentEnvironmentRequest(application.Id, $"Env {envIndex}", EnvironmentTier.Dev));
                for (var engineIndex = 0; engineIndex < 2; engineIndex++)
                {
                    await store.RegisterEngineAsync(
                        workspaceId,
                        new RegisterWorkflowEngineRequest(
                            environment.Id,
                            $"engine-{appIndex:00}-{envIndex:00}-{engineIndex:00}",
                            $"https://engine-{appIndex}-{envIndex}-{engineIndex}.example.test/elsa",
                            null,
                            "Azure Key Vault",
                            $"kv://workspace/{appIndex}/{envIndex}/{engineIndex}",
                            [new EngineCapability("engine.reload-configuration", "Reload engine configuration", CapabilityBoundary.EngineApi)],
                            [],
                            null));
                }
            }
        }
    }
}
