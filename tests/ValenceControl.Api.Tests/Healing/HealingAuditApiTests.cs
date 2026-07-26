using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ValenceControl.Api.Healing;
using ValenceControl.Api.Workspace;
using ValenceControl.Deployment.Core.Workspace;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Security;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using ValenceControl.PackageCatalog.Core.Accounts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ValenceControl.Api.Tests.Healing;

public sealed class HealingAuditApiTests
{
    [Fact]
    public async Task Overview_and_usage_return_bounded_workspace_scoped_outcomes()
    {
        await using var app = await CreateAsync("healing-report-owner");
        await SeedReportingScenarioAsync(app);

        var overviewResponse = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/overview?applicationId={app.ApplicationId:D}&environmentId={app.EnvironmentId:D}");
        var overview = await overviewResponse.Content.ReadFromJsonAsync<JsonElement>();

        overviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        overview.GetProperty("applications").GetProperty("enabled").GetInt64().Should().Be(1);
        overview.GetProperty("repairability").GetProperty("repairable").GetInt64().Should().Be(1);
        overview.GetProperty("repairActivity").GetProperty("blockedAttempts").GetInt64().Should().Be(1);
        overview.GetProperty("verificationOutcomes").EnumerateArray()
            .Should().Contain(x => x.GetProperty("name").GetString() == "FailedVerification");
        overview.GetProperty("recentIncidents").EnumerateArray().Should().HaveCount(2);
        overview.GetProperty("permissions").EnumerateArray().Select(x => x.GetString())
            .Should().Contain(HealingPermissions.Read);

        var usageResponse = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/usage?applicationId={app.ApplicationId:D}&from=2026-07-15T00:00:00Z&to=2026-07-17T00:00:00Z");
        var usage = await usageResponse.Content.ReadFromJsonAsync<JsonElement>();
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        usage.GetProperty("attempts").GetInt64().Should().Be(1);
        usage.GetProperty("inputUnits").GetInt64().Should().Be(20);
        usage.GetProperty("outputUnits").GetInt64().Should().Be(10);
        usage.GetProperty("repositoryRuns").GetInt64().Should().Be(1);
        usage.GetProperty("providerOperations").GetInt64().Should().Be(1);
        usage.GetRawText().ToLowerInvariant().Should().NotContain("prompt");
        usage.GetRawText().Should().NotContain("protected-production-payload");
    }

    [Fact]
    public async Task Audit_reconstructs_decisions_with_opaque_pagination_and_safe_projection()
    {
        await using var app = await CreateAsync("healing-audit-owner");
        var ids = await SeedReportingScenarioAsync(app);
        await AppendAuditAsync(app, ids.BlockedIncidentId, "candidate-classified", "threshold-reached", "status", "ready");
        await AppendAuditAsync(app, ids.BlockedIncidentId, "repair-dispatched", "policy-allowed", "attemptCount", "1");
        await AppendAuditAsync(app, ids.BlockedIncidentId, "merge-blocked", "verification-required", "gateReason", "revision-unverified");

        var firstResponse = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/audit?applicationId={app.ApplicationId:D}&incidentId={ids.BlockedIncidentId:D}&take=2");
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        first.GetProperty("items").EnumerateArray().Should().HaveCount(2);
        var cursor = first.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNullOrWhiteSpace();
        var newest = first.GetProperty("items").EnumerateArray().First();
        newest.GetProperty("eventType").GetString().Should().Be("merge-blocked");
        newest.GetProperty("details").GetProperty("gateReason").GetString().Should().Be("revision-unverified");

        var secondResponse = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/audit?incidentId={ids.BlockedIncidentId:D}&take=2&cursor={Uri.EscapeDataString(cursor!)}");
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        second.GetProperty("items").EnumerateArray().Should().ContainSingle();

        var serialized = first.GetRawText() + second.GetRawText();
        serialized.Should().NotContain("safeDetailJson");
        serialized.Should().NotContain("protected-production-payload");
        serialized.Should().NotContain("normalizedStackJson");
        (await app.Owner.GetAsync($"/api/workspaces/{app.WorkspaceId:D}/healing/audit?cursor=not-a-cursor"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reporting_requires_healing_read_and_does_not_disclose_cross_workspace_identifiers()
    {
        await using var app = await CreateAsync("healing-isolation-owner");
        var ids = await SeedReportingScenarioAsync(app);
        await AppendAuditAsync(app, ids.BlockedIncidentId, "repair-blocked", "policy-denied", "status", "blocked");
        const string readerSubject = "healing-report-reader";
        var readerId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, readerSubject, WorkspaceRole.Reader);
        var reader = app.Factory.CreateTrustedWorkspaceClient(readerSubject);
        var route = $"/api/workspaces/{app.WorkspaceId:D}/healing/audit?incidentId={ids.BlockedIncidentId:D}";

        (await reader.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, readerId, HealingPermissions.Read);
        (await reader.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.OK);
        var foreign = await reader.GetFromJsonAsync<JsonElement>(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/audit?incidentId={Guid.NewGuid():D}");
        foreign.GetProperty("items").EnumerateArray().Should().BeEmpty();

        var outsider = app.Factory.CreateTrustedWorkspaceClient("healing-report-outsider");
        (await outsider.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Incident_audit_includes_the_deployment_observation_linked_by_verification()
    {
        await using var app = await CreateAsync("healing-deployment-audit-owner");
        var ids = await SeedReportingScenarioAsync(app);
        await AppendAuditAsync(app, ids.DeploymentObservationId, "deployment-observed", "trusted-delivery", "revision", new string('e', 40));

        var response = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/audit?incidentId={ids.BlockedIncidentId:D}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("eventType").GetString().Should().Be("deployment-observed");
    }

    private static async Task<ReportingIds> SeedReportingScenarioAsync(TestApplication app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var providerId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var pathPolicyId = Guid.NewGuid();
        var evidencePolicyId = Guid.NewGuid();
        var mergePolicyId = Guid.NewGuid();
        var blockedIncidentId = Guid.NewGuid();
        var blockedEpisodeId = Guid.NewGuid();
        var healedIncidentId = Guid.NewGuid();
        var healedEpisodeId = Guid.NewGuid();
        var failedIncidentId = Guid.NewGuid();
        var failedEpisodeId = Guid.NewGuid();
        var deploymentObservationId = Guid.NewGuid();

        var configuration = new HealingConfiguration
        {
            Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            DiscoveryEnabled = true, RepairEnabled = true, AutomaticMergeEnabled = true,
            SignalProfileVersion = HealingContractVersions.SignalProfile, DefaultAttemptLimit = 2,
            VerificationWindow = TimeSpan.FromMinutes(15), TimeBudget = TimeSpan.FromMinutes(10),
            ConcurrencyBudget = 2, InferenceBudget = 1_000, RepositoryRunBudget = 3,
            CreatedAt = now.AddDays(-1), UpdatedAt = now
        };
        configuration.Environments.Add(new HealingEnvironmentConfiguration
        {
            Id = Guid.NewGuid(), HealingConfigurationId = configuration.Id, WorkspaceId = app.WorkspaceId,
            ApplicationId = app.ApplicationId, EnvironmentId = app.EnvironmentId, DiscoveryEnabled = true,
            RepairEnabled = true, CreatedAt = now.AddDays(-1), UpdatedAt = now
        });
        db.HealingConfigurations.Add(configuration);
        db.ProviderConnections.Add(new ProviderConnection
        {
            Id = providerId, WorkspaceId = app.WorkspaceId, Provider = "GitHub", InstallationId = "installation",
            RepositoryProviderId = "repo-1", RepositoryOwner = "acme", RepositoryName = "orders",
            CredentialReference = "credential-reference", Status = ProviderConnectionStatus.Active,
            CreatedAt = now.AddDays(-1), UpdatedAt = now
        });
        AddPolicies(db, app, pathPolicyId, evidencePolicyId, mergePolicyId, now);
        db.SourceOwnershipBindings.Add(new SourceOwnershipBinding
        {
            Id = bindingId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId, Name = "orders",
            SelectorKind = SourceSelectorKind.Application, SelectorPattern = "orders", Priority = 1,
            ProviderConnectionId = providerId, RepositoryProviderId = "repo-1", RepositoryOwner = "acme",
            RepositoryName = "orders", TargetBranch = "main", WorkflowIdentity = "repair.yml",
            WorkflowReference = "repair.yml@refs/heads/main", WorkflowRevision = new string('a', 40),
            PathPolicyId = pathPolicyId, EvidencePolicyId = evidencePolicyId, MergePolicyId = mergePolicyId,
            Status = SourceOwnershipBindingStatus.Active, CreatedAt = now.AddDays(-1), UpdatedAt = now
        });
        AddIncident(db, app, blockedIncidentId, HealingIncidentStatus.NeedsHuman, IncidentSeverity.Error, bindingId, now, "1");
        AddIncident(db, app, healedIncidentId, HealingIncidentStatus.Healed, IncidentSeverity.Warning, bindingId, now.AddHours(-2), "2");
        AddIncident(db, app, failedIncidentId, HealingIncidentStatus.FailedVerification, IncidentSeverity.Fatal, null, now.AddHours(-1), "3");
        await db.SaveChangesAsync();

        AddEpisodeAndImpact(db, app, blockedIncidentId, blockedEpisodeId, VerificationOutcome.PendingDeployment, now);
        AddEpisodeAndImpact(db, app, healedIncidentId, healedEpisodeId, VerificationOutcome.Healed, now.AddHours(-2));
        AddEpisodeAndImpact(db, app, failedIncidentId, failedEpisodeId, VerificationOutcome.FailedVerification, now.AddHours(-1));
        await db.SaveChangesAsync();
        foreach (var (incidentId, episodeId) in new[] { (blockedIncidentId, blockedEpisodeId), (healedIncidentId, healedEpisodeId), (failedIncidentId, failedEpisodeId) })
            (await db.HealingIncidents.FindAsync(incidentId))!.ActiveEpisodeId = episodeId;
        await db.SaveChangesAsync();

        db.DeploymentObservations.Add(new DeploymentObservation
        {
            Id = deploymentObservationId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            EnvironmentId = app.EnvironmentId, Revision = new string('e', 40), DeployedAt = now.AddMinutes(-4),
            Source = DeploymentObservationSource.ExternalDelivery, SourceIdempotencyKey = "delivery-report-1",
            TrustIdentity = "delivery-system", EvidenceDigest = new string('9', 64), AcceptedAt = now.AddMinutes(-3)
        });
        db.VerificationResults.Add(new VerificationResult
        {
            Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            EpisodeId = blockedEpisodeId, EnvironmentId = app.EnvironmentId, RepairedRevision = new string('e', 40),
            WindowStartedAt = now.AddMinutes(-4), WindowEndsAt = now.AddMinutes(11), Outcome = VerificationOutcome.Deployed,
            DeploymentObservationId = deploymentObservationId
        });

        var evidenceId = Guid.NewGuid();
        db.EvidenceBundles.Add(new EvidenceBundle
        {
            Id = evidenceId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            IncidentId = blockedIncidentId, Tier = EvidenceTier.DefaultRedacted,
            CanonicalJson = "{\"protected\":\"protected-production-payload\"}", Digest = new string('d', 64),
            ProvenanceJson = "{}", OmissionsJson = "[\"request.body\"]", SizeBytes = 42,
            CreatedAt = now.AddMinutes(-10), ExpiresAt = now.AddHours(1)
        });
        db.RepairAttempts.Add(new RepairAttempt
        {
            Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            IncidentId = blockedIncidentId, EpisodeId = blockedEpisodeId, BindingId = bindingId, AttemptNumber = 1,
            TargetRevision = new string('e', 40), Status = RepairAttemptStatus.Failed, EvidenceBundleId = evidenceId,
            RepairClassification = RepairClassification.RevisionUnverified, NonceHash = new string('f', 64),
            BudgetJson = "{}", UsageJson = JsonSerializer.Serialize(new RepairUsageSummary(
                20, 10, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(5), 1)),
            InputUnits = 20, OutputUnits = 10, AgentDurationTicks = TimeSpan.FromSeconds(12).Ticks,
            RepositoryRunDurationTicks = TimeSpan.FromSeconds(5).Ticks, RepositoryRuns = 1,
            StartedAt = now.AddMinutes(-8), CompletedAt = now.AddMinutes(-5), OutcomeCode = "validation-failed"
        });
        db.ProviderOperations.Add(new ProviderOperation
        {
            Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            ProviderConnectionId = providerId, IncidentId = blockedIncidentId, Kind = ProviderOperationKind.DispatchWorkflow,
            IdempotencyKey = "report-provider-operation", PayloadJson = "{}", PayloadHash = new string('1', 64),
            Status = ProviderOperationStatus.Completed, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-8)
        });
        await db.SaveChangesAsync();
        return new(blockedIncidentId, deploymentObservationId);
    }

    private static void AddPolicies(HealingDbContext db, TestApplication app, Guid pathId, Guid evidenceId, Guid mergeId, DateTimeOffset now)
    {
        db.PathPolicies.Add(new PathPolicy { Id = pathId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId, Name = "path", PolicyVersion = "1", PolicyHash = new string('1', 64), AllowedRootsJson = "[]", ForbiddenRootsJson = "[]", MaxFiles = 5, MaxChangedLines = 100, MaxPatchBytes = 10_000, CreatedAt = now });
        db.EvidencePolicies.Add(new EvidencePolicy { Id = evidenceId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId, Name = "evidence", PolicyVersion = "1", PolicyHash = new string('2', 64), MaximumTier = EvidenceTier.DefaultRedacted, PermittedFieldsJson = "[]", CreatedAt = now });
        db.MergePolicies.Add(new MergePolicy { Id = mergeId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId, Name = "merge", PolicyVersion = "1", PolicyHash = new string('3', 64), RequiredChecksJson = "[]", ForbiddenChangeCategoriesJson = "[]", CreatedAt = now });
    }

    private static void AddIncident(HealingDbContext db, TestApplication app, Guid id, HealingIncidentStatus status, IncidentSeverity severity, Guid? bindingId, DateTimeOffset seenAt, string fingerprintSuffix) =>
        db.HealingIncidents.Add(new HealingIncident
        {
            Id = id, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId,
            FingerprintVersion = "1", Fingerprint = new string(fingerprintSuffix[0], 64),
            RepairRepositoryKey = bindingId?.ToString("N") ?? "observation-only", Status = status,
            Severity = severity, Classification = IncidentClassification.UnhandledRequest, SelectedBindingId = bindingId,
            NeedsHumanReason = status == HealingIncidentStatus.NeedsHuman ? NeedsHumanReason.RevisionUnverified : null,
            FirstSeenAt = seenAt.AddMinutes(-10), LastSeenAt = seenAt, OccurrenceCount = status == HealingIncidentStatus.Healed ? 2 : 4
        });

    private static void AddEpisodeAndImpact(HealingDbContext db, TestApplication app, Guid incidentId, Guid episodeId, VerificationOutcome outcome, DateTimeOffset now)
    {
        db.IncidentEpisodes.Add(new IncidentEpisode { Id = episodeId, WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId, IncidentId = incidentId, OpenedAt = now.AddMinutes(-10), ClosedAt = outcome == VerificationOutcome.Healed ? now : null, ProducingRevisionsJson = "[]", Outcome = outcome == VerificationOutcome.Healed ? IncidentEpisodeOutcome.Healed : IncidentEpisodeOutcome.Active });
        db.EnvironmentImpacts.Add(new EnvironmentImpact { Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, ApplicationId = app.ApplicationId, EpisodeId = episodeId, EnvironmentId = app.EnvironmentId, FirstSeenAt = now.AddMinutes(-10), LastSeenAt = now, OccurrenceCount = 2, ProducingRevisionsJson = "[]", VerificationStatus = outcome, OccurrenceThreshold = 2, DebounceWindow = TimeSpan.FromMinutes(5), ClassificationPolicyVersion = "1", ClassificationPolicyHash = new string('c', 64) });
    }

    private static async Task AppendAuditAsync(TestApplication app, Guid aggregateId, string eventType, string reasonCode, string detailName, string detailValue)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<HealingAuditService>();
        await audit.AppendAsync(new HealingAuditWrite(app.WorkspaceId, "incident", aggregateId, eventType, reasonCode,
            "control-service", "healing-worker", aggregateId, null, "1", new string('a', 64), new string('b', 64),
            new Dictionary<string, string?> { [detailName] = detailValue }));
    }

    private static async Task<TestApplication> CreateAsync(string ownerSubject)
    {
        var factory = new ControlApiTestApplication();
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        return new(factory, owner, workspaceId, Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed record ReportingIds(Guid BlockedIncidentId, Guid DeploymentObservationId);

    private sealed record TestApplication(
        ControlApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
