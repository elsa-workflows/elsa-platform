using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Platform.Api.Healing;
using Elsa.Platform.Api.Workspace;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Elsa.Platform.PackageCatalog.Core.Accounts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Platform.Api.Tests.Healing;

public sealed class WorkspaceHealingIncidentApiTests
{
    [Fact]
    public async Task List_returns_safe_application_and_environment_scoped_summaries()
    {
        await using var app = await CreateApplicationAsync("healing-incident-list");
        var incidentId = await SeedIncidentAsync(app);

        var response = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/incidents?applicationId={app.ApplicationId:D}&environmentId={app.EnvironmentId:D}&status=ThresholdPending&severity=Error&repairable=false&take=20");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = body.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("id").GetGuid().Should().Be(incidentId);
        item.GetProperty("applicationId").GetGuid().Should().Be(app.ApplicationId);
        item.GetProperty("occurrenceCount").GetInt64().Should().Be(2);
        item.GetProperty("environmentImpacts").EnumerateArray().Should().ContainSingle();
        item.TryGetProperty("fingerprint", out _).Should().BeFalse("safe list responses do not expose correlation keys");
    }

    [Fact]
    public async Task Detail_returns_episode_and_safe_occurrence_metadata_without_stack_evidence()
    {
        await using var app = await CreateApplicationAsync("healing-incident-detail");
        var incidentId = await SeedIncidentAsync(app);

        var response = await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/incidents/{incidentId:D}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.GetProperty("id").GetGuid().Should().Be(incidentId);
        body.GetProperty("episodes").EnumerateArray().Should().ContainSingle();
        var occurrence = body.GetProperty("occurrences").EnumerateArray().Should().ContainSingle().Subject;
        occurrence.GetProperty("operationName").GetString().Should().Be("GET /orders/{id}");
        occurrence.TryGetProperty("normalizedStackJson", out _).Should().BeFalse();
        occurrence.TryGetProperty("evidenceDigest", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Incident_queries_enforce_permission_workspace_and_identifier_isolation()
    {
        await using var app = await CreateApplicationAsync("healing-incident-isolation");
        var incidentId = await SeedIncidentAsync(app);
        const string readerSubject = "healing-incident-reader";
        var readerId = await app.Factory.AddWorkspaceMemberAsync(app.WorkspaceId, readerSubject, WorkspaceRole.Reader);
        var reader = app.Factory.CreateTrustedWorkspaceClient(readerSubject);
        var detailUri = $"/api/workspaces/{app.WorkspaceId:D}/healing/incidents/{incidentId:D}";

        (await reader.GetAsync(detailUri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await app.Factory.GrantWorkspaceDeploymentPermissionAsync(app.WorkspaceId, readerId, HealingPermissions.Read);
        (await reader.GetAsync(detailUri)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/workspaces/{app.WorkspaceId:D}/healing/incidents/{Guid.NewGuid():D}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var outsider = app.Factory.CreateTrustedWorkspaceClient("healing-incident-outsider");
        (await outsider.GetAsync(detailUri)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await app.Owner.GetAsync(
            $"/api/workspaces/{app.WorkspaceId:D}/healing/incidents?applicationId={Guid.NewGuid():D}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> SeedIncidentAsync(TestApplication app)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HealingDbContext>();
        var inboxId = Guid.NewGuid();
        var incidentId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        db.HealingSignalInboxItems.Add(new HealingSignalInboxItem
        {
            Id = inboxId,
            WorkspaceId = app.WorkspaceId,
            ApplicationId = app.ApplicationId,
            EnvironmentId = app.EnvironmentId,
            IdempotencyKey = "api-detail-occurrence",
            Source = HealingSignalSource.ExplicitIncident,
            ProfileVersion = HealingContractVersions.SignalProfile,
            OccurredAt = occurredAt,
            AcceptedAt = occurredAt,
            RedactedEnvelopeJson = "{}",
            EnvelopeHash = new string('a', 64),
            Status = HealingInboxStatus.Completed
        });
        var incident = new HealingIncident
        {
            Id = incidentId,
            WorkspaceId = app.WorkspaceId,
            ApplicationId = app.ApplicationId,
            FingerprintVersion = "1",
            Fingerprint = new string('b', 64),
            RepairRepositoryKey = "observation-only",
            Status = HealingIncidentStatus.ThresholdPending,
            Severity = IncidentSeverity.Error,
            Classification = IncidentClassification.UnhandledRequest,
            FirstSeenAt = occurredAt,
            LastSeenAt = occurredAt.AddMinutes(1),
            OccurrenceCount = 2,
            ReadyAfter = occurredAt.AddMinutes(5)
        };
        db.HealingIncidents.Add(incident);
        await db.SaveChangesAsync();

        db.IncidentEpisodes.Add(new IncidentEpisode
        {
            Id = episodeId,
            WorkspaceId = app.WorkspaceId,
            ApplicationId = app.ApplicationId,
            IncidentId = incidentId,
            OpenedAt = occurredAt,
            ProducingRevisionsJson = "[]",
            Outcome = IncidentEpisodeOutcome.Active
        });
        db.EnvironmentImpacts.Add(new EnvironmentImpact
        {
            Id = Guid.NewGuid(),
            WorkspaceId = app.WorkspaceId,
            ApplicationId = app.ApplicationId,
            EpisodeId = episodeId,
            EnvironmentId = app.EnvironmentId,
            FirstSeenAt = occurredAt,
            LastSeenAt = occurredAt.AddMinutes(1),
            OccurrenceCount = 2,
            ProducingRevisionsJson = "[]",
            VerificationStatus = VerificationOutcome.PendingDeployment,
            OccurrenceThreshold = 3,
            DebounceWindow = TimeSpan.FromMinutes(5),
            ClassificationPolicyVersion = "1",
            ClassificationPolicyHash = new string('c', 64)
        });
        db.IncidentOccurrences.Add(new IncidentOccurrence
        {
            Id = Guid.NewGuid(),
            InboxItemId = inboxId,
            IncidentId = incidentId,
            EpisodeId = episodeId,
            WorkspaceId = app.WorkspaceId,
            ApplicationId = app.ApplicationId,
            EnvironmentId = app.EnvironmentId,
            OccurrenceKey = "api-detail-occurrence",
            OccurredAt = occurredAt,
            AcceptedAt = occurredAt,
            Classification = IncidentClassification.UnhandledRequest,
            Severity = IncidentSeverity.Error,
            ExceptionType = "System.InvalidOperationException",
            OperationName = "GET /orders/{id}",
            NormalizedStackJson = "[{\"type\":\"Acme.Orders.Api\"}]",
            RetryState = IncidentRetryState.None,
            FingerprintVersion = "1",
            Fingerprint = new string('b', 64),
            EvidenceTier = EvidenceTier.DefaultRedacted,
            EvidenceDigest = new string('d', 64)
        });
        await db.SaveChangesAsync();
        incident.ActiveEpisodeId = episodeId;
        await db.SaveChangesAsync();
        return incidentId;
    }

    private static async Task<TestApplication> CreateApplicationAsync(string ownerSubject)
    {
        var factory = new PlatformApiTestApplication();
        await factory.SeedAsync(_ => Task.CompletedTask);
        await factory.SeedHealingAsync();
        var owner = factory.CreateTrustedWorkspaceClient(ownerSubject);
        var workspaceId = await owner.GetDefaultWorkspaceIdAsync();
        var applicationResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications",
            new WorkspaceDeploymentApplicationRequest("Orders API", null));
        applicationResponse.EnsureSuccessStatusCode();
        var application = await applicationResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentApplication>();
        var environmentResponse = await owner.PostPlatformJsonAsync(
            $"/api/workspaces/{workspaceId:D}/deployments/applications/{application!.Id:D}/environments",
            new WorkspaceDeploymentEnvironmentRequest("Production", EnvironmentTier.Production));
        environmentResponse.EnsureSuccessStatusCode();
        var environment = await environmentResponse.Content.ReadPlatformJsonAsync<WorkspaceDeploymentEnvironment>();
        return new TestApplication(factory, owner, workspaceId, application.Id, environment!.Id);
    }

    private sealed record TestApplication(
        PlatformApiTestApplication Factory,
        HttpClient Owner,
        Guid WorkspaceId,
        Guid ApplicationId,
        Guid EnvironmentId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }
}
