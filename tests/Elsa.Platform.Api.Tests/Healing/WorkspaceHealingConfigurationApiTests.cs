using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Authentication;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.ComponentManifest;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Ownership;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class WorkspaceHealingConfigurationApiTests
{
    [Fact]
    public async Task Owner_can_configure_and_one_use_target_bound_confirmation_stops_healing()
    {
        await using var app = await CreateApplicationAsync("healing-config-owner");
        var owner = app.Owner;
        var configurationUri = ApplicationUri(app, "/configuration");

        var initial = await owner.GetAsync(configurationUri);
        var initialJson = await initial.Content.ReadFromJsonAsync<JsonElement>();
        initial.StatusCode.Should().Be(HttpStatusCode.OK);
        initialJson.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).Should().Contain(HealingPermissions.ConfigureAutoMerge);

        var updated = await owner.PutPlatformJsonAsync(configurationUri, ConfigurationRequest(initialJson.GetProperty("version").GetString()!));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmationResponse = await owner.PostPlatformJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingEmergencyStop });
        var confirmation = await confirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        confirmationResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var stopBody = new { confirmationId = confirmation.GetProperty("id").GetGuid() };
        var stopped = await owner.PostPlatformJsonAsync(ApplicationUri(app, "/stop"), stopBody);
        var replay = await owner.PostPlatformJsonAsync(ApplicationUri(app, "/stop"), stopBody);

        stopped.StatusCode.Should().Be(HttpStatusCode.OK);
        (await stopped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationKillSwitch").GetBoolean().Should().BeTrue();
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var replayProblem = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayProblem.GetProperty("code").GetString().Should().Be("deployment.confirmation.used");
        replayProblem.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();

        var resumeConfirmationResponse = await owner.PostPlatformJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingEmergencyResume });
        var resumeConfirmation = await resumeConfirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var resumed = await owner.PostPlatformJsonAsync(
            ApplicationUri(app, "/resume"),
            new { confirmationId = resumeConfirmation.GetProperty("id").GetGuid() });
        resumed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationKillSwitch").GetBoolean().Should().BeFalse();

        var wrongConfirmationResponse = await owner.PostPlatformJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingEmergencyStop });
        var wrongConfirmation = await wrongConfirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var current = await owner.GetFromJsonAsync<JsonElement>(configurationUri);
        var mismatched = await owner.PutPlatformJsonAsync(configurationUri, ConfigurationRequest(
            current.GetProperty("version").GetString()!, automaticMergeEnabled: true,
            confirmationId: wrongConfirmation.GetProperty("id").GetGuid()));
        mismatched.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("deployment.confirmation.target");
    }

    [Fact]
    public async Task Configuration_rejects_foreign_and_duplicate_environment_overrides()
    {
        await using var app = await CreateApplicationAsync("healing-config-environments", createRevision: true);
        var uri = ApplicationUri(app, "/configuration");
        var current = await app.Owner.GetFromJsonAsync<JsonElement>(uri);
        var version = current.GetProperty("version").GetString()!;
        var environmentId = current.GetProperty("environments")[0].GetProperty("environmentId").GetGuid();

        var foreign = await app.Owner.PutPlatformJsonAsync(
            uri,
            ConfigurationRequest(version, environments: [EnvironmentRequest(Guid.NewGuid())]));
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.environment.not-found");

        var duplicate = await app.Owner.PutPlatformJsonAsync(
            uri,
            ConfigurationRequest(version, environments: [EnvironmentRequest(environmentId), EnvironmentRequest(environmentId)]));
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.environment.duplicate");
    }

    [Fact]
    public async Task Framework_binding_failures_use_safe_healing_problem_details()
    {
        await using var app = await CreateApplicationAsync("healing-invalid-json");
        using var request = new HttpRequestMessage(HttpMethod.Put, ApplicationUri(app, "/configuration"))
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        };

        var response = await app.Owner.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        problem.GetProperty("code").GetString().Should().Be("healing.request.invalid");
        problem.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        problem.ToString().Should().NotContain("JsonException");
    }

    [Fact]
    public async Task Authority_profile_semantic_nulls_duplicate_names_and_null_versions_use_problem_details()
    {
        await using var app = await CreateApplicationAsync("healing-authority-validation");
        var credentialReferenceId = await CreateGitHubCredentialReferenceAsync(app);
        var uri = ApplicationUri(app, "/authority-profiles");

        var semanticNull = await app.Owner.PostPlatformJsonAsync(uri, new
        {
            name = (string?)null,
            installationId = (string?)null,
            repositoryOwner = (string?)null,
            repositoryName = (string?)null,
            credentialReferenceId
        });
        semanticNull.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await semanticNull.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.invalid-configuration");

        var request = new
        {
            name = "Duplicate profile",
            installationId = "42",
            repositoryOwner = "acme",
            repositoryName = "claims",
            credentialReferenceId
        };
        var created = await app.Owner.PostPlatformJsonAsync(uri, request);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var providerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("providerConnection").GetProperty("id").GetGuid();
        var duplicate = await app.Owner.PostPlatformJsonAsync(uri, new
        {
            request.name,
            installationId = "43",
            repositoryOwner = "acme",
            repositoryName = "other",
            credentialReferenceId
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.administration-conflict");

        var nullVersion = await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/validate"),
            new { version = (string?)null });
        nullVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await nullVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.provider.version");
    }

    [Fact]
    public async Task Automatic_merge_authority_requires_a_one_use_target_bound_confirmation()
    {
        await using var app = await CreateApplicationAsync("healing-authority-automerge");
        var credentialReferenceId = await CreateGitHubCredentialReferenceAsync(app);
        var uri = ApplicationUri(app, "/authority-profiles");
        var request = new
        {
            name = "Automatic repair profile",
            installationId = "42",
            repositoryOwner = "acme",
            repositoryName = "claims",
            credentialReferenceId,
            automaticMergeEnabled = true
        };

        var missingConfirmation = await app.Owner.PostPlatformJsonAsync(uri, request);
        missingConfirmation.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingConfirmation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("deployment.confirmation.missing");

        var confirmationResponse = await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingAutomaticMerge, automaticMergeEnabled = true });
        var confirmation = await confirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var confirmed = await app.Owner.PostPlatformJsonAsync(uri, new
        {
            request.name,
            request.installationId,
            request.repositoryOwner,
            request.repositoryName,
            request.credentialReferenceId,
            request.automaticMergeEnabled,
            confirmationId = confirmation.GetProperty("id").GetGuid()
        });

        confirmed.StatusCode.Should().Be(HttpStatusCode.Created);
        (await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mergePolicy")
            .GetProperty("automaticMergeEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Healing_permissions_support_explicit_grants_revocation_and_safe_cross_workspace_denial()
    {
        await using var app = await CreateApplicationAsync("healing-permission-owner");
        const string readerSubject = "healing-reader";
        var readerId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, readerSubject, WorkspaceRole.Reader);
        var reader = app.Factory.CreateTrustedWorkspaceClient(readerSubject);
        var uri = ApplicationUri(app, "/configuration");

        (await reader.GetAsync(uri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, readerId, HealingPermissions.Read);
        (await reader.GetAsync(uri)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PutPlatformJsonAsync(uri, ConfigurationRequest(""))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var revoke = await app.Owner.PostPlatformJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/permissions/revocations",
            new WorkspacePermissionRevokeRequest(readerId, HealingPermissions.Read));
        revoke.EnsureSuccessStatusCode();
        (await reader.GetAsync(uri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var outsider = app.Factory.CreateTrustedWorkspaceClient("healing-outsider");
        (await outsider.GetAsync(uri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await app.Owner.GetAsync($"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{Guid.NewGuid():D}/configuration")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var outsiderWorkspaceId = await outsider.GetDefaultWorkspaceIdAsync();
        var outsiderApplicationResponse = await outsider.PostPlatformJsonAsync(
            $"/api/workspaces/{outsiderWorkspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Outsider API", null));
        var outsiderApplication = await outsiderApplicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
        (await app.Owner.GetAsync($"/api/workspaces/{outsiderWorkspaceId:D}/healing/applications/{outsiderApplication!.Id:D}/configuration")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Canonical_manifest_requires_body_digest_is_idempotent_and_owner_controls_trust()
    {
        await using var app = await CreateApplicationAsync("healing-manifest-owner", createRevision: true);
        var canonical = CanonicalManifest();
        var uri = ApplicationUri(app, $"/revisions/{app.RevisionId:D}/component-manifests");
        const string idempotencyKey = "manifest-delivery-1";

        var registered = await SendManifestAsync(app.Owner, uri, canonical, ContentDigest(canonical), idempotencyKey);
        var registeredJson = await registered.Content.ReadFromJsonAsync<JsonElement>();
        registered.StatusCode.Should().Be(HttpStatusCode.Created);
        registeredJson.GetProperty("trustState").GetString().Should().Be("Unverified");

        var replay = await SendManifestAsync(app.Owner, uri, canonical, ContentDigest(canonical), idempotencyKey);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var changedPayload = CanonicalManifest(buildId: "build-2");
        var idempotencyConflict = await SendManifestAsync(app.Owner, uri, changedPayload, ContentDigest(changedPayload), idempotencyKey);
        idempotencyConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await idempotencyConflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.idempotency-key.conflict");
        var invalidDigest = await SendManifestAsync(app.Owner, uri, canonical, "sha256:" + new string('0', 64));
        invalidDigest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var malformedDigest = await SendManifestAsync(app.Owner, uri, canonical, "SHA256:not-a-digest");
        malformedDigest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await malformedDigest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.content-digest.invalid");
        var oversizedKey = await SendManifestAsync(app.Owner, uri, canonical, ContentDigest(canonical), new string('k', 257));
        oversizedKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await oversizedKey.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.idempotency-key.invalid");
        var nonCanonical = canonical + Environment.NewLine;
        var nonCanonicalResponse = await SendManifestAsync(app.Owner, uri, nonCanonical, ContentDigest(nonCanonical));
        nonCanonicalResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await nonCanonicalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("healing.manifest.non-canonical");
        var arbitraryRevision = await SendManifestAsync(app.Owner, ApplicationUri(app, $"/revisions/{Guid.NewGuid():D}/component-manifests"), canonical, ContentDigest(canonical));
        arbitraryRevision.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var mismatchedRevision = CanonicalManifest(sourceRevision: new string('f', 40));
        var mismatchedRevisionResponse = await SendManifestAsync(app.Owner, uri, mismatchedRevision, ContentDigest(mismatchedRevision));
        mismatchedRevisionResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await mismatchedRevisionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.manifest.revision-mismatch");

        var manifestId = registeredJson.GetProperty("id").GetGuid();
        const string configurerSubject = "manifest-configurer";
        var configurerId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, configurerSubject, WorkspaceRole.Reader);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, configurerId, HealingPermissions.Configure);
        var configurer = app.Factory.CreateTrustedWorkspaceClient(configurerSubject);
        (await configurer.PostAsync(ApplicationUri(app, $"/component-manifests/{manifestId:D}/verify"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var autoMergeConfirmation = await configurer.PostPlatformJsonAsync(ApplicationUri(app, "/confirmations"), new { actionType = ConfirmationActionType.HealingAutomaticMerge, automaticMergeEnabled = true });
        autoMergeConfirmation.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var verified = await app.Owner.PostAsync(ApplicationUri(app, $"/component-manifests/{manifestId:D}/verify"), null);
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
        (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trustState").GetString().Should().Be("Verified");
        (await app.Owner.GetFromJsonAsync<JsonElement>(ApplicationUri(app, "/configuration")))
            .GetProperty("manifestReadiness").GetString().Should().Be("Untrusted");
        var revoked = await app.Owner.PostAsync(ApplicationUri(app, $"/component-manifests/{manifestId:D}/revoke"), null);
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Manifest_readiness_requires_automation_authority_for_a_current_revision()
    {
        await using var app = await CreateApplicationAsync("healing-manifest-readiness", createRevision: true);
        var canonical = CanonicalManifest();
        var registered = await SendManifestAsync(
            app.Owner,
            ApplicationUri(app, $"/revisions/{app.RevisionId:D}/component-manifests"),
            canonical,
            ContentDigest(canonical),
            "readiness-delivery");
        registered.EnsureSuccessStatusCode();
        var manifestId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await app.Owner.PostAsync(AttestationUri(app, manifestId), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AttestManifestAsync(app, manifestId, "sha256:" + new string('0', 64), "build-1"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AttestManifestAsync(app, manifestId, ManifestDigest(canonical), "build-1")).EnsureSuccessStatusCode();

        var configurationUri = ApplicationUri(app, "/configuration");
        var ready = await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri);
        ready.GetProperty("manifestReadiness").GetString().Should().Be("Ready");
        var stagingEnvironmentResponse = await app.Owner.PostPlatformJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications/{app.ApplicationId:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Staging", EnvironmentTier.Stage));
        var stagingEnvironment = await stagingEnvironmentResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>();
        stagingEnvironmentResponse.EnsureSuccessStatusCode();
        var stagingRevisionResponse = await app.Owner.PostPlatformJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications/{app.ApplicationId:D}/environments/{stagingEnvironment!.Id:D}/revisions",
            new WorkspaceDesiredStateRevisionRequest("staging-1", new string('e', 40), []));
        var stagingRevision = await stagingRevisionResponse.Content.ReadPlatformJsonAsync<WorkspaceDesiredStateRevision>();
        stagingRevisionResponse.EnsureSuccessStatusCode();
        (await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri))
            .GetProperty("manifestReadiness").GetString().Should().Be("Stale");

        var stagingCanonical = CanonicalManifest(new string('e', 40), "build-staging");
        var stagingManifestResponse = await SendManifestAsync(
            app.Owner,
            ApplicationUri(app, $"/revisions/{stagingRevision!.Id:D}/component-manifests"),
            stagingCanonical,
            ContentDigest(stagingCanonical),
            "readiness-staging-delivery");
        var stagingManifestId = (await stagingManifestResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        stagingManifestResponse.EnsureSuccessStatusCode();
        (await AttestManifestAsync(app, stagingManifestId, ManifestDigest(stagingCanonical), "build-staging")).EnsureSuccessStatusCode();
        (await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri))
            .GetProperty("manifestReadiness").GetString().Should().Be("Ready");

        var environmentId = ready.GetProperty("environments")[0].GetProperty("environmentId").GetGuid();
        var nextRevision = await app.Owner.PostPlatformJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications/{app.ApplicationId:D}/environments/{environmentId:D}/revisions",
            new WorkspaceDesiredStateRevisionRequest("release-2", new string('f', 40), []));
        nextRevision.EnsureSuccessStatusCode();

        var stale = await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri);
        stale.GetProperty("manifestReadiness").GetString().Should().Be("Stale");
    }

    [Fact]
    public async Task Configure_member_can_create_draft_but_only_owner_can_activate_suspend_and_revoke()
    {
        await using var app = await CreateApplicationAsync("healing-binding-owner");
        var credentialReferenceId = await CreateGitHubCredentialReferenceAsync(app);
        var authorityResponse = await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, "/authority-profiles"),
            new
            {
                name = "Claims repair",
                installationId = "42",
                repositoryOwner = "acme",
                repositoryName = "claims",
                credentialReferenceId
            });
        var authority = await authorityResponse.Content.ReadFromJsonAsync<JsonElement>();
        authorityResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        authority.ToString().Should().NotContain("secret://");
        var providerId = authority.GetProperty("providerConnection").GetProperty("id").GetGuid();
        var providerVersion = authority.GetProperty("providerConnection").GetProperty("version").GetString()!;
        authority.GetProperty("providerConnection").GetProperty("status").GetString().Should().Be("PendingValidation");
        var pathPolicyId = authority.GetProperty("pathPolicy").GetProperty("id").GetGuid();
        var evidencePolicyId = authority.GetProperty("evidencePolicy").GetProperty("id").GetGuid();
        var mergePolicyId = authority.GetProperty("mergePolicy").GetProperty("id").GetGuid();
        var catalog = await app.Owner.GetFromJsonAsync<JsonElement>(ApplicationUri(app, "/authority-catalog"));
        catalog.GetProperty("providerConnections").GetArrayLength().Should().Be(1);
        catalog.ToString().Should().NotContain("credentialReference");

        var validatedResponse = await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/validate"),
            new { version = providerVersion });
        var validated = await validatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        validatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        validated.GetProperty("status").GetString().Should().Be("Active");
        var repositoryProviderId = validated.GetProperty("repositoryProviderId").GetString()!;
        providerVersion = validated.GetProperty("version").GetString()!;

        const string memberSubject = "healing-configurer";
        var memberId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, memberSubject, WorkspaceRole.Reader);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, memberId, HealingPermissions.Configure);
        var member = app.Factory.CreateTrustedWorkspaceClient(memberSubject);
        (await member.PostPlatformJsonAsync(ApplicationUri(app, "/authority-profiles"), new
        {
            name = "Unauthorized", installationId = "99",
            repositoryOwner = "acme", repositoryName = "other", credentialReferenceId
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var collectionUri = ApplicationUri(app, "/source-ownership-bindings");
        var request = new
        {
            name = "Claims packages",
            selectorKind = "Package",
            selectorPattern = "Elsa.Acme.*",
            priority = 10,
            providerConnectionId = providerId,
            repositoryProviderId,
            repositoryOwner = "acme",
            repositoryName = "claims",
            targetBranch = "main",
            workflowIdentity = ".github/workflows/healing.yml",
            workflowRevision = "refs/heads/main",
            pathPolicyId,
            evidencePolicyId,
            mergePolicyId
        };

        var draftResponse = await member.PostPlatformJsonAsync(collectionUri, request);
        var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        draft.GetProperty("status").GetString().Should().Be("Draft");
        draft.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        var bindingId = draft.GetProperty("id").GetGuid();

        var updateUri = ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}");
        var missingVersion = await member.PutPlatformJsonAsync(updateUri, request);
        missingVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missingVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.binding.version.required");
        var updatedRequest = new
        {
            request.name,
            request.selectorKind,
            request.selectorPattern,
            priority = 11,
            request.providerConnectionId,
            request.repositoryProviderId,
            request.repositoryOwner,
            request.repositoryName,
            request.targetBranch,
            request.workflowIdentity,
            request.workflowRevision,
            request.pathPolicyId,
            request.evidencePolicyId,
            request.mergePolicyId,
            version = draft.GetProperty("version").GetString()
        };
        var updated = await member.PutPlatformJsonAsync(updateUri, updatedRequest);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedJson = await updated.Content.ReadFromJsonAsync<JsonElement>();
        updatedJson.GetProperty("version").GetString().Should().NotBe(updatedRequest.version);
        var stale = await member.PutPlatformJsonAsync(updateUri, updatedRequest);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("healing.binding.stale");

        var oversizedBinding = new
        {
            name = new string('x', 513),
            request.selectorKind,
            request.selectorPattern,
            request.priority,
            request.providerConnectionId,
            request.repositoryProviderId,
            request.repositoryOwner,
            request.repositoryName,
            request.targetBranch,
            request.workflowIdentity,
            request.workflowRevision,
            request.pathPolicyId,
            request.evidencePolicyId,
            request.mergePolicyId
        };
        var invalidBinding = await member.PostPlatformJsonAsync(collectionUri, oversizedBinding);
        invalidBinding.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidBinding.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("healing.binding.invalid");

        (await member.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/activate"), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/activate"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/suspend"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/activate"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/revoke"), null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var suspendedProvider = await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/suspend"),
            new { version = providerVersion });
        suspendedProvider.StatusCode.Should().Be(HttpStatusCode.OK);
        var suspendedProviderJson = await suspendedProvider.Content.ReadFromJsonAsync<JsonElement>();
        suspendedProviderJson.GetProperty("status").GetString().Should().Be("Suspended");
        (await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/activate"),
            new { version = suspendedProviderJson.GetProperty("version").GetString() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var revalidatedProvider = await app.Owner.PostPlatformJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/validate"),
            new { version = suspendedProviderJson.GetProperty("version").GetString() });
        revalidatedProvider.StatusCode.Should().Be(HttpStatusCode.OK);
        (await revalidatedProvider.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Active");
    }

    private static object EnvironmentRequest(Guid environmentId) => new
    {
        environmentId,
        discoveryEnabled = true,
        repairDispatchEnabled = true,
        environmentKillSwitch = false,
        occurrenceThreshold = (int?)null,
        debounceWindow = (string?)null
    };

    private static object ConfigurationRequest(
        string version,
        bool automaticMergeEnabled = false,
        Guid? confirmationId = null,
        IReadOnlyList<object>? environments = null) => new
    {
        discoveryEnabled = true,
        repairDispatchEnabled = true,
        automaticMergeEnabled,
        signalProfileVersion = "1.0",
        defaultAttemptLimit = 2,
        verificationWindow = "00:15:00",
        timeBudget = "00:30:00",
        concurrencyBudget = 1,
        inferenceBudget = 1000,
        repositoryRunBudget = 1,
        environments = environments ?? Array.Empty<object>(),
        version,
        confirmationId
    };

    private static async Task<TestApplication> CreateApplicationAsync(string ownerSubject, bool createRevision = false)
    {
        var factory = new PlatformApiTestApplication(configureServices: services =>
        {
            services.RemoveAll<IProviderConnectionValidator>();
            services.AddSingleton<IProviderConnectionValidator, TestProviderConnectionValidator>();
        });
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims API", null));
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
        applicationResponse.EnsureSuccessStatusCode();

        Guid? revisionId = null;
        if (createRevision)
        {
            var environmentResponse = await owner.PostPlatformJsonAsync(
                $"/api/workspaces/{workspaceId:D}/deployments/applications/{application!.Id:D}/environments",
                new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
            var environment = await environmentResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>();
            environmentResponse.EnsureSuccessStatusCode();
            var revisionResponse = await owner.PostPlatformJsonAsync(
                $"/api/workspaces/{workspaceId:D}/deployments/applications/{application.Id:D}/environments/{environment!.Id:D}/revisions",
                new WorkspaceDesiredStateRevisionRequest("release-1", "0123456789012345678901234567890123456789", []));
            var revision = await revisionResponse.Content.ReadPlatformJsonAsync<WorkspaceDesiredStateRevision>();
            revisionResponse.EnsureSuccessStatusCode();
            revisionId = revision!.Id;
        }
        return new TestApplication(factory, owner, workspaceId, application!.Id, revisionId);
    }

    private static async Task<Guid> CreateGitHubCredentialReferenceAsync(TestApplication app)
    {
        var storeResponse = await app.Owner.PostPlatformJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest(
                "Healing GitHub Apps", null, null, DeploymentSecretStoreType.LocalEncryptedDatabase));
        var store = await storeResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentSecretStore>();
        storeResponse.EnsureSuccessStatusCode();
        var referenceResponse = await app.Owner.PostPlatformJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/secret-stores/{store!.Id:D}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest(
                "Claims GitHub App", "github-app://claims", null, "test-private-key"));
        var reference = await referenceResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentCredentialReference>();
        referenceResponse.EnsureSuccessStatusCode();
        return reference!.Id;
    }

    private sealed class TestProviderConnectionValidator : IProviderConnectionValidator
    {
        public ValueTask<ProviderConnectionValidationResult> ValidateAsync(
            ProviderConnection connection,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderConnectionValidationResult.Valid(
                $"github-{connection.RepositoryOwner}-{connection.RepositoryName}"));
    }

    private static string ApplicationUri(TestApplication app, string suffix) =>
        $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{app.ApplicationId:D}{suffix}";

    private static string CanonicalManifest(string sourceRevision = "0123456789012345678901234567890123456789", string? buildId = "build-1")
    {
        var manifest = new HealingComponentManifest(
            "1.0",
            new ComponentManifestApplication("Claims API", "1.0.0", "net10.0", null),
            new ComponentManifestRevision(sourceRevision, "https://github.com/acme/claims", buildId, DateTimeOffset.Parse("2026-07-16T12:00:00Z")),
            [new Elsa.Platform.Healing.ComponentManifest.ComponentManifestEntry("package:Elsa.Acme.Claims/1.0.0", "package", "Elsa.Acme.Claims", "1.0.0", "sha256:" + new string('a', 64), "https://github.com/acme/claims", "0123456789012345678901234567890123456789", true, [new ComponentManifestAssembly("Elsa.Acme.Claims", "1.0.0", null, "lib/net10.0/Elsa.Acme.Claims.dll", "sha256:" + new string('b', 64))], [])]);
        return ComponentManifestSerializer.Serialize(manifest);
    }

    private static Task<HttpResponseMessage> SendManifestAsync(HttpClient client, string uri, string body, string digest, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        request.Headers.Add("Content-Digest", digest);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> AttestManifestAsync(
        TestApplication app,
        Guid manifestId,
        string manifestDigest,
        string buildId)
    {
        var client = app.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "builder-dev-key");
        return client.PostPlatformJsonAsync(
            AttestationUri(app, manifestId),
            new { manifestDigest, buildId });
    }

    private static string AttestationUri(TestApplication app, Guid manifestId) =>
        $"/api/builder/healing/workspaces/{app.WorkspaceId:D}/applications/{app.ApplicationId:D}/component-manifests/{manifestId:D}/attest";

    private static string ContentDigest(string body) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant()}";

    private static string ManifestDigest(string body) =>
        ComponentManifestSerializer.Deserialize(body).ManifestDigest!;

    private sealed record TestApplication(PlatformApiTestApplication Factory, HttpClient Owner, Guid WorkspaceId, Guid ApplicationId, Guid? Revision)
        : IAsyncDisposable
    {
        public Guid RevisionId => Revision ?? throw new InvalidOperationException("No revision was created.");
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
