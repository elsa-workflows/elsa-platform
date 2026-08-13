using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Api.Authentication;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Core.Cockpit;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.ComponentManifest;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Core.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ValenceControl.Api.Tests.Healing;

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
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Contains(HealingPermissions.ConfigureAutoMerge, initialJson.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()));

        var updated = await owner.PutControlJsonAsync(configurationUri, ConfigurationRequest(initialJson.GetProperty("version").GetString()!));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var confirmationResponse = await owner.PostControlJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingEmergencyStop });
        var confirmation = await confirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, confirmationResponse.StatusCode);

        var stopBody = new { confirmationId = confirmation.GetProperty("id").GetGuid() };
        var stopped = await owner.PostControlJsonAsync(ApplicationUri(app, "/stop"), stopBody);
        var replay = await owner.PostControlJsonAsync(ApplicationUri(app, "/stop"), stopBody);

        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        Assert.True((await stopped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationKillSwitch").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var replayProblem = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("deployment.confirmation.used", replayProblem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(replayProblem.GetProperty("correlationId").GetString()));

        var resumeConfirmationResponse = await owner.PostControlJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingEmergencyResume });
        var resumeConfirmation = await resumeConfirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var resumed = await owner.PostControlJsonAsync(
            ApplicationUri(app, "/resume"),
            new { confirmationId = resumeConfirmation.GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.False((await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationKillSwitch").GetBoolean());

        var wrongConfirmationResponse = await owner.PostControlJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingEmergencyStop });
        var wrongConfirmation = await wrongConfirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var current = await owner.GetFromJsonAsync<JsonElement>(configurationUri);
        var mismatched = await owner.PutControlJsonAsync(configurationUri, ConfigurationRequest(
            current.GetProperty("version").GetString()!, automaticMergeEnabled: true,
            confirmationId: wrongConfirmation.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.Equal("deployment.confirmation.target", (await mismatched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Configuration_rejects_foreign_and_duplicate_environment_overrides()
    {
        await using var app = await CreateApplicationAsync("healing-config-environments", createRevision: true);
        var uri = ApplicationUri(app, "/configuration");
        var current = await app.Owner.GetFromJsonAsync<JsonElement>(uri);
        var version = current.GetProperty("version").GetString()!;
        var environmentId = current.GetProperty("environments")[0].GetProperty("environmentId").GetGuid();

        var foreign = await app.Owner.PutControlJsonAsync(
            uri,
            ConfigurationRequest(version, environments: [EnvironmentRequest(Guid.NewGuid())]));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("healing.environment.not-found", (await foreign.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var duplicate = await app.Owner.PutControlJsonAsync(
            uri,
            ConfigurationRequest(version, environments: [EnvironmentRequest(environmentId), EnvironmentRequest(environmentId)]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("healing.environment.duplicate", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
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

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("healing.request.invalid", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("JsonException", problem.ToString());
    }

    [Fact]
    public async Task Authority_profile_semantic_nulls_duplicate_names_and_null_versions_use_problem_details()
    {
        await using var app = await CreateApplicationAsync("healing-authority-validation");
        var credentialReferenceId = await CreateGitHubCredentialReferenceAsync(app);
        var uri = ApplicationUri(app, "/authority-profiles");

        var semanticNull = await app.Owner.PostControlJsonAsync(uri, new
        {
            name = (string?)null,
            installationId = (string?)null,
            repositoryOwner = (string?)null,
            repositoryName = (string?)null,
            credentialReferenceId
        });
        Assert.Equal(HttpStatusCode.BadRequest, semanticNull.StatusCode);
        Assert.Equal("healing.invalid-configuration", (await semanticNull.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var request = new
        {
            name = "Duplicate profile",
            installationId = "42",
            repositoryOwner = "acme",
            repositoryName = "claims",
            credentialReferenceId
        };
        var created = await app.Owner.PostControlJsonAsync(uri, request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var providerId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("providerConnection").GetProperty("id").GetGuid();
        var duplicate = await app.Owner.PostControlJsonAsync(uri, new
        {
            request.name,
            installationId = "43",
            repositoryOwner = "acme",
            repositoryName = "other",
            credentialReferenceId
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("healing.administration-conflict", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var nullVersion = await app.Owner.PostControlJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/validate"),
            new { version = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, nullVersion.StatusCode);
        Assert.Equal("healing.provider.version", (await nullVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
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

        var missingConfirmation = await app.Owner.PostControlJsonAsync(uri, request);
        Assert.Equal(HttpStatusCode.BadRequest, missingConfirmation.StatusCode);
        Assert.Equal("deployment.confirmation.missing", (await missingConfirmation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var confirmationResponse = await app.Owner.PostControlJsonAsync(
            ApplicationUri(app, "/confirmations"),
            new { actionType = ConfirmationActionType.HealingAutomaticMerge, automaticMergeEnabled = true });
        var confirmation = await confirmationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var confirmed = await app.Owner.PostControlJsonAsync(uri, new
        {
            request.name,
            request.installationId,
            request.repositoryOwner,
            request.repositoryName,
            request.credentialReferenceId,
            request.automaticMergeEnabled,
            confirmationId = confirmation.GetProperty("id").GetGuid()
        });

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        Assert.True((await confirmed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mergePolicy").GetProperty("automaticMergeEnabled").GetBoolean());
    }

    [Fact]
    public async Task Healing_permissions_support_explicit_grants_revocation_and_safe_cross_workspace_denial()
    {
        await using var app = await CreateApplicationAsync("healing-permission-owner");
        const string readerSubject = "healing-reader";
        var readerId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, readerSubject, WorkspaceRole.Reader);
        var reader = app.Factory.CreateTrustedWorkspaceClient(readerSubject);
        var uri = ApplicationUri(app, "/configuration");

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(uri)).StatusCode);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, readerId, HealingPermissions.Read);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(uri)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutControlJsonAsync(uri, ConfigurationRequest(""))).StatusCode);

        var revoke = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/permissions/revocations",
            new WorkspacePermissionRevokeRequest(readerId, HealingPermissions.Read));
        revoke.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync(uri)).StatusCode);

        var outsider = app.Factory.CreateTrustedWorkspaceClient("healing-outsider");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(uri)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Owner.GetAsync($"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{Guid.NewGuid():D}/configuration")).StatusCode);

        var outsiderWorkspaceId = await outsider.GetDefaultWorkspaceIdAsync();
        var outsiderApplicationResponse = await outsider.PostControlJsonAsync(
            $"/api/workspaces/{outsiderWorkspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Outsider API", null));
        var outsiderApplication = await outsiderApplicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Owner.GetAsync($"/api/workspaces/{outsiderWorkspaceId:D}/healing/applications/{outsiderApplication!.Id:D}/configuration")).StatusCode);
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
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal("Unverified", registeredJson.GetProperty("trustState").GetString());

        var replay = await SendManifestAsync(app.Owner, uri, canonical, ContentDigest(canonical), idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var changedPayload = CanonicalManifest(buildId: "build-2");
        var idempotencyConflict = await SendManifestAsync(app.Owner, uri, changedPayload, ContentDigest(changedPayload), idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, idempotencyConflict.StatusCode);
        Assert.Equal("healing.idempotency-key.conflict", (await idempotencyConflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var invalidDigest = await SendManifestAsync(app.Owner, uri, canonical, "sha256:" + new string('0', 64));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDigest.StatusCode);
        var malformedDigest = await SendManifestAsync(app.Owner, uri, canonical, "SHA256:not-a-digest");
        Assert.Equal(HttpStatusCode.BadRequest, malformedDigest.StatusCode);
        Assert.Equal("healing.content-digest.invalid", (await malformedDigest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var oversizedKey = await SendManifestAsync(app.Owner, uri, canonical, ContentDigest(canonical), new string('k', 257));
        Assert.Equal(HttpStatusCode.BadRequest, oversizedKey.StatusCode);
        Assert.Equal("healing.idempotency-key.invalid", (await oversizedKey.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var nonCanonical = canonical + Environment.NewLine;
        var nonCanonicalResponse = await SendManifestAsync(app.Owner, uri, nonCanonical, ContentDigest(nonCanonical));
        Assert.Equal(HttpStatusCode.BadRequest, nonCanonicalResponse.StatusCode);
        Assert.Equal("healing.manifest.non-canonical", (await nonCanonicalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var arbitraryRevision = await SendManifestAsync(app.Owner, ApplicationUri(app, $"/revisions/{Guid.NewGuid():D}/component-manifests"), canonical, ContentDigest(canonical));
        Assert.Equal(HttpStatusCode.NotFound, arbitraryRevision.StatusCode);
        var mismatchedRevision = CanonicalManifest(sourceRevision: new string('f', 40));
        var mismatchedRevisionResponse = await SendManifestAsync(app.Owner, uri, mismatchedRevision, ContentDigest(mismatchedRevision));
        Assert.Equal(HttpStatusCode.Conflict, mismatchedRevisionResponse.StatusCode);
        Assert.Equal("healing.manifest.revision-mismatch", (await mismatchedRevisionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var manifestId = registeredJson.GetProperty("id").GetGuid();
        const string configurerSubject = "manifest-configurer";
        var configurerId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, configurerSubject, WorkspaceRole.Reader);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, configurerId, HealingPermissions.Configure);
        var configurer = app.Factory.CreateTrustedWorkspaceClient(configurerSubject);
        Assert.Equal(HttpStatusCode.Forbidden, (await configurer.PostAsync(ApplicationUri(app, $"/component-manifests/{manifestId:D}/verify"), null)).StatusCode);
        var autoMergeConfirmation = await configurer.PostControlJsonAsync(ApplicationUri(app, "/confirmations"), new { actionType = ConfirmationActionType.HealingAutomaticMerge, automaticMergeEnabled = true });
        Assert.Equal(HttpStatusCode.Forbidden, autoMergeConfirmation.StatusCode);
        var verified = await app.Owner.PostAsync(ApplicationUri(app, $"/component-manifests/{manifestId:D}/verify"), null);
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal("Verified", (await verified.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trustState").GetString());
        Assert.Equal("Untrusted", (await app.Owner.GetFromJsonAsync<JsonElement>(ApplicationUri(app, "/configuration"))).GetProperty("manifestReadiness").GetString());
        var revoked = await app.Owner.PostAsync(ApplicationUri(app, $"/component-manifests/{manifestId:D}/revoke"), null);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
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
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Owner.PostAsync(AttestationUri(app, manifestId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AttestManifestAsync(app, manifestId, "sha256:" + new string('0', 64), "build-1")).StatusCode);
        (await AttestManifestAsync(app, manifestId, ManifestDigest(canonical), "build-1")).EnsureSuccessStatusCode();

        var configurationUri = ApplicationUri(app, "/configuration");
        var ready = await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri);
        Assert.Equal("Ready", ready.GetProperty("manifestReadiness").GetString());
        var stagingEnvironmentResponse = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications/{app.ApplicationId:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Staging", EnvironmentTier.Stage));
        var stagingEnvironment = await stagingEnvironmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
        stagingEnvironmentResponse.EnsureSuccessStatusCode();
        var stagingRevisionResponse = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications/{app.ApplicationId:D}/environments/{stagingEnvironment!.Id:D}/revisions",
            new WorkspaceDesiredStateRevisionRequest("staging-1", new string('e', 40), []));
        var stagingRevision = await stagingRevisionResponse.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>();
        stagingRevisionResponse.EnsureSuccessStatusCode();
        Assert.Equal("Stale", (await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri)).GetProperty("manifestReadiness").GetString());

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
        Assert.Equal("Ready", (await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri)).GetProperty("manifestReadiness").GetString());

        var environmentId = ready.GetProperty("environments")[0].GetProperty("environmentId").GetGuid();
        var nextRevision = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications/{app.ApplicationId:D}/environments/{environmentId:D}/revisions",
            new WorkspaceDesiredStateRevisionRequest("release-2", new string('f', 40), []));
        nextRevision.EnsureSuccessStatusCode();

        var stale = await app.Owner.GetFromJsonAsync<JsonElement>(configurationUri);
        Assert.Equal("Stale", stale.GetProperty("manifestReadiness").GetString());
    }

    [Fact]
    public async Task Configure_member_can_create_draft_but_only_owner_can_activate_suspend_and_revoke()
    {
        await using var app = await CreateApplicationAsync("healing-binding-owner");
        var credentialReferenceId = await CreateGitHubCredentialReferenceAsync(app);
        var authorityResponse = await app.Owner.PostControlJsonAsync(
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
        Assert.Equal(HttpStatusCode.Created, authorityResponse.StatusCode);
        Assert.DoesNotContain("secret://", authority.ToString());
        var providerId = authority.GetProperty("providerConnection").GetProperty("id").GetGuid();
        var providerVersion = authority.GetProperty("providerConnection").GetProperty("version").GetString()!;
        Assert.Equal("PendingValidation", authority.GetProperty("providerConnection").GetProperty("status").GetString());
        var pathPolicyId = authority.GetProperty("pathPolicy").GetProperty("id").GetGuid();
        var evidencePolicyId = authority.GetProperty("evidencePolicy").GetProperty("id").GetGuid();
        var mergePolicyId = authority.GetProperty("mergePolicy").GetProperty("id").GetGuid();
        var catalog = await app.Owner.GetFromJsonAsync<JsonElement>(ApplicationUri(app, "/authority-catalog"));
        Assert.Equal(1, catalog.GetProperty("providerConnections").GetArrayLength());
        Assert.DoesNotContain("credentialReference", catalog.ToString());

        var validatedResponse = await app.Owner.PostControlJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/validate"),
            new { version = providerVersion });
        var validated = await validatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, validatedResponse.StatusCode);
        Assert.Equal("Active", validated.GetProperty("status").GetString());
        var repositoryProviderId = validated.GetProperty("repositoryProviderId").GetString()!;
        providerVersion = validated.GetProperty("version").GetString()!;

        const string memberSubject = "healing-configurer";
        var memberId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, memberSubject, WorkspaceRole.Reader);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, memberId, HealingPermissions.Configure);
        var member = app.Factory.CreateTrustedWorkspaceClient(memberSubject);

        var actorLinkUri = ApplicationUri(app, $"/provider-connections/{providerId:D}/actor-links/12345");
        var otherApplicationResponse = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Other API", null));
        var otherApplication = await otherApplicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        otherApplicationResponse.EnsureSuccessStatusCode();
        var crossApplicationActorLinkUri =
            $"/api/workspaces/{app.WorkspaceId:D}/healing/applications/{otherApplication!.Id:D}" +
            $"/provider-connections/{providerId:D}/actor-links/12345";
        Assert.Equal(HttpStatusCode.NotFound, (await app.Owner.PutControlJsonAsync(crossApplicationActorLinkUri, new
        {
            providerActorLogin = "cross-application-escalation",
            controlAccountId = memberId
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutControlJsonAsync(actorLinkUri, new
        {
            providerActorLogin = "healing-maintainer",
            controlAccountId = memberId
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.PutControlJsonAsync(actorLinkUri, new
        {
            providerActorLogin = "healing-maintainer",
            controlAccountId = memberId
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutControlJsonAsync(actorLinkUri, new
        {
            providerActorLogin = "identity-escalation",
            controlAccountId = memberId
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync(actorLinkUri)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.PutControlJsonAsync(actorLinkUri, new
        {
            providerActorLogin = "healing-maintainer-updated",
            controlAccountId = memberId
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.DeleteAsync(actorLinkUri)).StatusCode);
        await using (var auditScope = app.Factory.Services.CreateAsyncScope())
        {
            var auditEvents = await auditScope.ServiceProvider.GetRequiredService<HealingDbContext>()
                .Set<HealingAuditEvent>().AsNoTracking()
                .Where(x => x.WorkspaceId == app.WorkspaceId && x.AggregateType == "provider-actor-link")
                .OrderBy(x => x.Sequence)
                .ToArrayAsync();
            Assert.Equal(
                ["actor-link-created", "actor-link-updated", "actor-link-revoked"],
                auditEvents.Select(x => x.EventType));
            Assert.All(auditEvents, x => Assert.Equal(app.ApplicationId, x.CausationId));
            var correlationIds = auditEvents.Select(x => x.CorrelationId).ToArray();
            Assert.Equal(correlationIds.Length, correlationIds.Distinct().Count());
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostControlJsonAsync(ApplicationUri(app, "/authority-profiles"), new
        {
            name = "Unauthorized", installationId = "99",
            repositoryOwner = "acme", repositoryName = "other", credentialReferenceId
        })).StatusCode);
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
            workflowReference = "refs/tags/valence-control-healing-v1",
            workflowRevision = new string('a', 40),
            pathPolicyId,
            evidencePolicyId,
            mergePolicyId
        };

        var draftResponse = await member.PostControlJsonAsync(collectionUri, request);
        var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, draftResponse.StatusCode);
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("version").GetString()));
        var bindingId = draft.GetProperty("id").GetGuid();
        var applicationAudit = await app.Owner.GetFromJsonAsync<JsonElement>(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/audit?applicationId={app.ApplicationId:D}&take=100");
        var auditEventTypes = applicationAudit.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("eventType").GetString());
        Assert.All(["actor-link-created", "actor-link-updated", "actor-link-revoked"], eventType => Assert.Contains(eventType, auditEventTypes));

        var updateUri = ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}");
        var missingVersion = await member.PutControlJsonAsync(updateUri, request);
        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        Assert.Equal("healing.binding.version.required", (await missingVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
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
            request.workflowReference,
            request.workflowRevision,
            request.pathPolicyId,
            request.evidencePolicyId,
            request.mergePolicyId,
            version = draft.GetProperty("version").GetString()
        };
        var updated = await member.PutControlJsonAsync(updateUri, updatedRequest);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedJson = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(updatedRequest.version, updatedJson.GetProperty("version").GetString());
        var stale = await member.PutControlJsonAsync(updateUri, updatedRequest);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("healing.binding.stale", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

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
            request.workflowReference,
            request.workflowRevision,
            request.pathPolicyId,
            request.evidencePolicyId,
            request.mergePolicyId
        };
        var invalidBinding = await member.PostControlJsonAsync(collectionUri, oversizedBinding);
        Assert.Equal(HttpStatusCode.BadRequest, invalidBinding.StatusCode);
        Assert.Equal("healing.binding.invalid", (await invalidBinding.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/activate"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/activate"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/suspend"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/activate"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Owner.PostAsync(ApplicationUri(app, $"/source-ownership-bindings/{bindingId:D}/revoke"), null)).StatusCode);

        var suspendedProvider = await app.Owner.PostControlJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/suspend"),
            new { version = providerVersion });
        Assert.Equal(HttpStatusCode.OK, suspendedProvider.StatusCode);
        var suspendedProviderJson = await suspendedProvider.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Suspended", suspendedProviderJson.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await app.Owner.PostControlJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/activate"),
            new { version = suspendedProviderJson.GetProperty("version").GetString() })).StatusCode);
        var revalidatedProvider = await app.Owner.PostControlJsonAsync(
            ApplicationUri(app, $"/provider-connections/{providerId:D}/validate"),
            new { version = suspendedProviderJson.GetProperty("version").GetString() });
        Assert.Equal(HttpStatusCode.OK, revalidatedProvider.StatusCode);
        Assert.Equal("Active", (await revalidatedProvider.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
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
        var factory = new ControlApiTestApplication(configureServices: services =>
        {
            services.RemoveAll<IProviderConnectionValidator>();
            services.AddSingleton<IProviderConnectionValidator, TestProviderConnectionValidator>();
        });
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostControlJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Claims API", null));
        var application = await applicationResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentApplication>();
        applicationResponse.EnsureSuccessStatusCode();

        Guid? revisionId = null;
        if (createRevision)
        {
            var environmentResponse = await owner.PostControlJsonAsync(
                $"/api/workspaces/{workspaceId:D}/deployments/applications/{application!.Id:D}/environments",
                new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
            var environment = await environmentResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentEnvironment>();
            environmentResponse.EnsureSuccessStatusCode();
            var revisionResponse = await owner.PostControlJsonAsync(
                $"/api/workspaces/{workspaceId:D}/deployments/applications/{application.Id:D}/environments/{environment!.Id:D}/revisions",
                new WorkspaceDesiredStateRevisionRequest("release-1", "0123456789012345678901234567890123456789", []));
            var revision = await revisionResponse.Content.ReadControlJsonAsync<WorkspaceDesiredStateRevision>();
            revisionResponse.EnsureSuccessStatusCode();
            revisionId = revision!.Id;
        }
        return new TestApplication(factory, owner, workspaceId, application!.Id, revisionId);
    }

    private static async Task<Guid> CreateGitHubCredentialReferenceAsync(TestApplication app)
    {
        var storeResponse = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/secret-stores",
            new WorkspaceDeploymentSecretStoreRequest(
                "Healing GitHub Apps", null, null, DeploymentSecretStoreType.LocalEncryptedDatabase));
        var store = await storeResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentSecretStore>();
        storeResponse.EnsureSuccessStatusCode();
        var referenceResponse = await app.Owner.PostControlJsonAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/deployments/secret-stores/{store!.Id:D}/credential-references",
            new WorkspaceDeploymentCredentialReferenceRequest(
                "Claims GitHub App", "github-app://claims", null, "test-private-key"));
        var reference = await referenceResponse.Content.ReadControlJsonAsync<WorkspaceDeploymentCredentialReference>();
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
            [new ValenceControl.Healing.ComponentManifest.ComponentManifestEntry("package:Elsa.Acme.Claims/1.0.0", "package", "Elsa.Acme.Claims", "1.0.0", "sha256:" + new string('a', 64), "https://github.com/acme/claims", "0123456789012345678901234567890123456789", true, [new ComponentManifestAssembly("Elsa.Acme.Claims", "1.0.0", null, "lib/net10.0/Elsa.Acme.Claims.dll", "sha256:" + new string('b', 64))], [])]);
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
        return client.PostControlJsonAsync(
            AttestationUri(app, manifestId),
            new { manifestDigest, buildId });
    }

    private static string AttestationUri(TestApplication app, Guid manifestId) =>
        $"/api/builder/healing/workspaces/{app.WorkspaceId:D}/applications/{app.ApplicationId:D}/component-manifests/{manifestId:D}/attest";

    private static string ContentDigest(string body) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant()}";

    private static string ManifestDigest(string body) =>
        ComponentManifestSerializer.Deserialize(body).ManifestDigest!;

    private sealed record TestApplication(ControlApiTestApplication Factory, HttpClient Owner, Guid WorkspaceId, Guid ApplicationId, Guid? Revision)
        : IAsyncDisposable
    {
        public Guid RevisionId => Revision ?? throw new InvalidOperationException("No revision was created.");
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
