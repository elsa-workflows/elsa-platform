using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Operations;
using ValenceControl.Healing.Core.Ownership;
using ValenceControl.Healing.Core.Providers;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Core.Security;
using Microsoft.EntityFrameworkCore;
using ComponentManifestModel = ValenceControl.Healing.Core.ComponentManifest;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingStoreTests
{
    [Fact]
    public async Task Human_command_decision_and_domain_mutation_are_committed_with_an_audit_event()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var incident = CreateIncident(workspaceId, applicationId);
        incident.Status = HealingIncidentStatus.NeedsHuman;
        var command = new HumanCommand
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            IncidentId = incident.Id,
            IdempotencyKey = $"retry:{incident.Id:D}",
            Command = ValenceControl.Healing.Abstractions.HealingHumanCommands.Retry,
            ProviderActorId = "12345",
            ProviderActorLogin = "healing-maintainer",
            Status = HumanCommandStatus.Pending,
            RequestedAt = DateTimeOffset.UtcNow
        };
        fixture.Db.AddRange(CreateConfiguration(workspaceId, applicationId), incident, command);
        await fixture.Db.SaveChangesAsync();

        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");
        var timeProvider = new FixedTimeProvider(now);
        var auditService = new HealingAuditService(new HealingStore(fixture.Db), timeProvider);
        var service = new HumanProviderCommandService(
            new HealingHumanProviderCommandStore(fixture.Db, auditService, timeProvider));
        var decision = await service.ExecuteAsync(command.Id, new HumanProviderCommandAuthorization(
            true,
            "maintain",
            actorId,
            new HashSet<string>(StringComparer.Ordinal)
            {
                ValenceControl.Healing.Abstractions.HealingPermissions.RetryRepair
            }));

        Assert.True(decision.Executed);
        Assert.Equal(HealingIncidentStatus.ReadyForRepair, (await fixture.Db.HealingIncidents.AsNoTracking().SingleAsync(x => x.Id == incident.Id)).Status);
        var completedCommand = await fixture.Db.HumanCommands.AsNoTracking().SingleAsync(x => x.Id == command.Id);
        Assert.Equal(now, completedCommand.CompletedAt);
        using var permissionSnapshot = JsonDocument.Parse(completedCommand.ProviderPermissionSnapshotJson);
        Assert.Equal(now, permissionSnapshot.RootElement.GetProperty("EvaluatedAt").GetDateTimeOffset());
        var auditEvent = await fixture.Db.Set<HealingAuditEvent>().AsNoTracking()
            .SingleAsync(x => x.AggregateType == "human-command" && x.AggregateId == command.Id);
        Assert.Equal("human-command-executed", auditEvent.EventType);
        Assert.Equal(actorId.ToString("D"), auditEvent.ActorId);
    }

    [Fact]
    public async Task Revoked_provider_history_does_not_block_a_new_validated_authorization_generation()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var first = await store.SaveProviderConnectionAsync(new ProviderConnection
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Provider = "GitHub", InstallationId = "42",
            RepositoryProviderId = "repo-42", RepositoryOwner = "acme", RepositoryName = "claims",
            CredentialReference = $"credential://{Guid.NewGuid():D}", Status = ProviderConnectionStatus.Active,
            CreatedAt = now, UpdatedAt = now
        });
        first.Status = ProviderConnectionStatus.Revoked;
        first.UpdatedAt = now.AddMinutes(1);
        await store.SaveProviderConnectionAsync(first);

        var second = await store.SaveProviderConnectionAsync(new ProviderConnection
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Provider = "GitHub", InstallationId = "84",
            RepositoryProviderId = $"pending-{Guid.NewGuid():N}", RepositoryOwner = "acme", RepositoryName = "claims",
            CredentialReference = $"credential://{Guid.NewGuid():D}", Status = ProviderConnectionStatus.PendingValidation,
            CreatedAt = now.AddMinutes(2), UpdatedAt = now.AddMinutes(2)
        });
        second.RepositoryProviderId = "repo-42";
        second.Status = ProviderConnectionStatus.Active;
        second.UpdatedAt = now.AddMinutes(3);
        var authorized = await store.SaveProviderConnectionAsync(second);

        Assert.Equal(ProviderConnectionStatus.Active, authorized.Status);
        Assert.Equal("repo-42", authorized.RepositoryProviderId);
        Assert.Equal(2, (await store.ListProviderConnectionsAsync(workspaceId)).Count());
    }

    [Fact]
    public async Task Ownership_port_scopes_manifest_reads_and_atomically_transitions_trust()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        IHealingOwnershipStore store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var manifest = CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-a");
        var entry = manifest.Entries.Single();
        entry.Assemblies =
        [
            CreateAssembly(manifest, entry, "lib/net10.0/Component.dll"),
            CreateAssembly(manifest, entry, "lib/net10.0/Component.Contracts.dll")
        ];

        var accepted = await store.AddManifestAsync(manifest);
        var conflicting = await store.AddManifestAsync(
            CreateManifest(workspaceId, applicationId, revisionId, "digest-b", "component-b"));
        var crossWorkspace = await store.GetManifestAsync(Guid.NewGuid(), applicationId, manifest.Id);
        var crossWorkspaceTransition = await store.TransitionManifestTrustAsync(
            Guid.NewGuid(), applicationId, manifest.Id, ComponentManifestTrustState.Unverified,
            ComponentManifestTrustState.Verified, "owner", "workspace-owner-verification", DateTimeOffset.UtcNow);
        var verified = await store.TransitionManifestTrustAsync(
            workspaceId, applicationId, manifest.Id, ComponentManifestTrustState.Unverified,
            ComponentManifestTrustState.Verified, "owner", "workspace-owner-verification", DateTimeOffset.UtcNow);
        var staleTransition = await store.TransitionManifestTrustAsync(
            workspaceId, applicationId, manifest.Id, ComponentManifestTrustState.Unverified,
            ComponentManifestTrustState.Revoked, "owner", "workspace-owner-verification", DateTimeOffset.UtcNow);
        var trusted = await store.ListManifestsAsync(workspaceId, applicationId, trustedOnly: true);

        Assert.False(accepted.IsReplay);
        Assert.False(conflicting.IsConsistentReplay);
        Assert.Null(crossWorkspace);
        Assert.False(crossWorkspaceTransition);
        Assert.True(verified);
        Assert.False(staleTransition);
        Assert.Equal(manifest.Id, Assert.Single(trusted).Id);
        Assert.Equal(
            new[] { "lib/net10.0/Component.dll", "lib/net10.0/Component.Contracts.dll" }.Order(),
            trusted.Single().Entries.Single().Assemblies.Select(x => x.RelativePath).Order());
    }

    [Fact]
    public async Task Manifest_registration_idempotency_is_persistent_payload_bound_and_scope_isolated()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        const string key = "delivery-42";
        const string payloadHash = "sha256:payload-a";

        var accepted = await store.ExecuteInTransactionAsync(cancellationToken => store.RegisterManifestAsync(
            CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-a"), key, payloadHash, cancellationToken));
        var replay = await store.ExecuteInTransactionAsync(cancellationToken => store.RegisterManifestAsync(
            CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-a"), key, payloadHash, cancellationToken));
        var conflict = await store.ExecuteInTransactionAsync(cancellationToken => store.RegisterManifestAsync(
            CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-a"), key, "sha256:payload-b", cancellationToken));
        var otherApplication = await store.ExecuteInTransactionAsync(cancellationToken => store.RegisterManifestAsync(
            CreateManifest(workspaceId, Guid.NewGuid(), revisionId, "digest-a", "component-a"), key, payloadHash, cancellationToken));
        var otherRevision = await store.ExecuteInTransactionAsync(cancellationToken => store.RegisterManifestAsync(
            CreateManifest(workspaceId, applicationId, Guid.NewGuid(), "digest-a", "component-a"), key, payloadHash, cancellationToken));
        var otherWorkspace = await store.ExecuteInTransactionAsync(cancellationToken => store.RegisterManifestAsync(
            CreateManifest(Guid.NewGuid(), applicationId, revisionId, "digest-a", "component-a"), key, payloadHash, cancellationToken));

        Assert.False(accepted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(accepted.Value.Id, replay.Value.Id);
        Assert.Equal(HealingOwnershipReasonCodes.IdempotencyConflict, conflict.FailureReasonCode);
        Assert.Null(otherApplication.FailureReasonCode);
        Assert.Null(otherRevision.FailureReasonCode);
        Assert.Null(otherWorkspace.FailureReasonCode);
        Assert.Equal(4, (await fixture.Db.ComponentManifestRegistrations.CountAsync()));
    }

    [Fact]
    public async Task Ownership_transaction_rolls_back_mutation_when_required_audit_step_fails()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        IHealingOwnershipStore store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var configuration = CreateConfiguration(workspaceId, applicationId);

        var operation = () => store.ExecuteInTransactionAsync<HealingConfiguration>(async cancellationToken =>
        {
            await store.SaveConfigurationAsync(configuration, cancellationToken);
            throw new InvalidOperationException("simulated-audit-failure");
        }).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(operation);
        Assert.Null((await store.GetConfigurationAsync(workspaceId, applicationId)));
    }

    [Fact]
    public async Task Binding_update_rotates_version_and_rejects_the_previous_version()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var provider = new ProviderConnection
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Provider = "github", InstallationId = "installation",
            RepositoryProviderId = "repo", RepositoryOwner = "acme", RepositoryName = "workflows",
            CredentialReference = "secret-ref", Status = ProviderConnectionStatus.Active
        };
        var path = CreatePolicy<PathPolicy>(workspaceId, applicationId, "path");
        var evidence = CreatePolicy<EvidencePolicy>(workspaceId, applicationId, "evidence");
        var merge = CreatePolicy<MergePolicy>(workspaceId, applicationId, "merge");
        var binding = new SourceOwnershipBinding
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId, Name = "binding",
            SelectorKind = SourceSelectorKind.Package, SelectorPattern = "Acme.*", ProviderConnectionId = provider.Id,
            RepositoryProviderId = provider.RepositoryProviderId, RepositoryOwner = provider.RepositoryOwner,
            RepositoryName = provider.RepositoryName, TargetBranch = "main", WorkflowIdentity = "healing.yml",
            WorkflowReference = "refs/tags/valence-control-healing-v1",
            WorkflowRevision = "abcdef1", PathPolicyId = path.Id, EvidencePolicyId = evidence.Id, MergePolicyId = merge.Id,
            Status = SourceOwnershipBindingStatus.Draft
        };
        fixture.Db.AddRange(provider, path, evidence, merge, binding);
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var candidate = (await store.GetBindingAsync(workspaceId, applicationId, binding.Id))!;
        var stale = CloneBinding(candidate);
        candidate.Name = "updated";

        var updated = await store.SaveBindingAsync(candidate);
        stale.Name = "stale";
        var staleWrite = () => store.SaveBindingAsync(stale).AsTask();

        Assert.NotEqual(stale.Version, updated.Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(staleWrite);
    }

    [Fact]
    public async Task Inbox_append_is_idempotent_for_matching_payload_and_rejects_key_reuse()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var item = CreateInboxItem("occurrence-1", "hash-a");

        var accepted = await store.AppendInboxAsync(item);
        var replay = await store.AppendInboxAsync(CreateInboxItem("occurrence-1", "hash-a"));
        var conflict = () => store.AppendInboxAsync(CreateInboxItem("occurrence-1", "hash-b")).AsTask();

        Assert.False(accepted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(accepted.Value.Id, replay.Value.Id);
        await Assert.ThrowsAsync<HealingIdempotencyConflictException>(conflict);
    }

    [Fact]
    public async Task Inbox_lease_requires_its_token_and_terminal_items_are_not_requeued()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        await store.AppendInboxAsync(CreateInboxItem("occurrence-lease", "hash-lease"));

        var lease = await store.TryLeaseNextInboxAsync("worker-1", now, TimeSpan.FromMinutes(5));
        var competingLease = await store.TryLeaseNextInboxAsync("worker-2", now, TimeSpan.FromMinutes(5));
        var wrongToken = await store.CompleteInboxAsync(lease!.Value.Id, "wrong-token", now, HealingInboxStatus.Completed, "accepted", null);
        var completed = await store.CompleteInboxAsync(lease.Value.Id, lease.LeaseToken, now, HealingInboxStatus.Completed, "accepted", null);
        var terminalLease = await store.TryLeaseNextInboxAsync("worker-2", now.AddHours(1), TimeSpan.FromMinutes(5));

        Assert.Null(competingLease);
        Assert.False(wrongToken);
        Assert.True(completed);
        Assert.Null(terminalLease);
    }

    [Fact]
    public async Task Expired_inbox_lease_cannot_be_completed_by_stale_worker()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        await store.AppendInboxAsync(CreateInboxItem("occurrence-expired", "hash-expired"));
        var lease = await store.TryLeaseNextInboxAsync("worker-1", now, TimeSpan.FromMinutes(5));

        var completed = await store.CompleteInboxAsync(
            lease!.Value.Id,
            lease.LeaseToken,
            now.AddMinutes(6),
            HealingInboxStatus.Completed,
            "accepted",
            null);

        Assert.False(completed);
    }

    [Fact]
    public async Task Provider_operation_is_idempotent_and_uses_expiring_token_bound_lease()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var operation = CreateProviderOperation("dispatch-1", "payload-a", now);
        fixture.Db.ProviderConnections.Add(new ProviderConnection
        {
            Id = operation.ProviderConnectionId,
            WorkspaceId = operation.WorkspaceId,
            Provider = "github",
            InstallationId = "installation-1",
            RepositoryProviderId = "repository-1",
            RepositoryOwner = "acme",
            RepositoryName = "workflow-app",
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await fixture.Db.SaveChangesAsync();
        var accepted = await store.AppendProviderOperationAsync(operation);
        var replay = await store.AppendProviderOperationAsync(CreateProviderOperation("dispatch-1", "payload-a", now));
        var lease = await store.TryLeaseNextProviderOperationAsync("provider-worker", now, TimeSpan.FromMinutes(5));

        var staleCompletion = await store.CompleteProviderOperationAsync(
            lease!.Value.Id,
            lease.LeaseToken,
            now.AddMinutes(6),
            ProviderOperationStatus.Completed,
            "provider-42",
            null,
            null);

        Assert.False(accepted.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.False(staleCompletion);
    }

    [Fact]
    public async Task Provider_operation_idempotency_is_scoped_by_operation_kind()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var dispatch = CreateProviderOperation("shared-key", "dispatch-payload", now);
        var publish = CreateProviderOperation("shared-key", "publish-payload", now);
        publish.Kind = ProviderOperationKind.PublishPullRequest;
        fixture.Db.ProviderConnections.Add(new ProviderConnection
        {
            Id = dispatch.ProviderConnectionId,
            WorkspaceId = dispatch.WorkspaceId,
            Provider = "GitHub",
            InstallationId = "installation-1",
            RepositoryProviderId = "repository-1",
            RepositoryOwner = "acme",
            RepositoryName = "workflow-app",
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await fixture.Db.SaveChangesAsync();

        var dispatchResult = await store.AppendProviderOperationAsync(dispatch);
        var publishResult = await store.AppendProviderOperationAsync(publish);

        Assert.False(dispatchResult.IsReplay);
        Assert.False(publishResult.IsReplay);
        Assert.Equal(2, (await fixture.Db.ProviderOperations.CountAsync()));
    }

    [Fact]
    public async Task Provider_operation_port_retries_durably_and_rejects_a_lost_lease()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var concreteStore = new HealingStore(fixture.Db);
        IProviderOperationStore store = concreteStore;
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var operation = CreateProviderOperation("dispatch-port-1", "payload-a", now);
        fixture.Db.ProviderConnections.Add(new ProviderConnection
        {
            Id = operation.ProviderConnectionId,
            WorkspaceId = operation.WorkspaceId,
            Provider = "github",
            InstallationId = "installation-1",
            RepositoryProviderId = "repository-1",
            RepositoryOwner = "acme",
            RepositoryName = "workflow-app",
            CredentialReference = "secret://github-app",
            Status = ProviderConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        });
        await fixture.Db.SaveChangesAsync();
        await store.AppendAsync(operation);
        var lease = await store.TryLeaseNextAsync("provider-worker", now, TimeSpan.FromMinutes(5));
        Assert.NotNull(lease);
        var claimed = lease!;

        await store.FinishAsync(
            claimed,
            HealingOperationOutcome.Retry("github-rate-limited"),
            now.AddMinutes(1),
            now.AddMinutes(2));
        var pending = await fixture.Db.ProviderOperations.AsNoTracking().SingleAsync(x => x.Id == operation.Id);
        var lostLease = () => store.FinishAsync(
            claimed,
            HealingOperationOutcome.Completed("late-completion"),
            now.AddMinutes(1),
            null).AsTask();
        var retryLease = await store.TryLeaseNextAsync("provider-worker", now.AddMinutes(2), TimeSpan.FromMinutes(5));

        Assert.Equal(ProviderOperationStatus.Pending, pending.Status);
        Assert.Equal(now.AddMinutes(2), pending.NextAttemptAt);
        Assert.NotNull(retryLease);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(lostLease);
    }

    [Fact]
    public async Task Repair_attempt_cap_and_lease_predicates_are_enforced_atomically()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-16T10:00:00Z");
        var incident = CreateIncident(workspaceId, applicationId);
        var episode = CreateEpisode(workspaceId, applicationId, incident.Id);
        var provider = new ProviderConnection
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Provider = "github", InstallationId = "installation",
            RepositoryProviderId = "repo", RepositoryOwner = "acme", RepositoryName = "workflows",
            CredentialReference = "secret-ref", Status = ProviderConnectionStatus.Active
        };
        var path = CreatePolicy<PathPolicy>(workspaceId, applicationId, "path");
        var evidencePolicy = CreatePolicy<EvidencePolicy>(workspaceId, applicationId, "evidence");
        var merge = CreatePolicy<MergePolicy>(workspaceId, applicationId, "merge");
        var binding = new SourceOwnershipBinding
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId, Name = "binding",
            SelectorKind = SourceSelectorKind.Package, SelectorPattern = "Acme.*", ProviderConnectionId = provider.Id,
            RepositoryProviderId = provider.RepositoryProviderId, RepositoryOwner = provider.RepositoryOwner,
            RepositoryName = provider.RepositoryName, TargetBranch = "main", WorkflowIdentity = "healing.yml",
            WorkflowReference = "refs/tags/valence-control-healing-v1",
            WorkflowRevision = "abcdef1", PathPolicyId = path.Id, EvidencePolicyId = evidencePolicy.Id,
            MergePolicyId = merge.Id, Status = SourceOwnershipBindingStatus.Active
        };
        var evidence = new EvidenceBundle
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId, IncidentId = incident.Id,
            Tier = EvidenceTier.DefaultRedacted, CanonicalJson = "{}", Digest = new string('a', 64),
            ProvenanceJson = "{}", OmissionsJson = "[]", CreatedAt = now, ExpiresAt = now.AddHours(1)
        };
        fixture.Db.AddRange(CreateConfiguration(workspaceId, applicationId), incident, episode, provider, path, evidencePolicy, merge, binding, evidence);
        await fixture.Db.SaveChangesAsync();
        IRepairOrchestrationStore store = new HealingStore(fixture.Db);
        RepairAttempt NewAttempt() => new()
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
            IncidentId = incident.Id, EpisodeId = episode.Id, BindingId = binding.Id,
            TargetRevision = "abcdef1", Status = RepairAttemptStatus.Queued, EvidenceBundleId = evidence.Id,
            RepairClassification = RepairClassification.InsufficientConfidence,
            NonceHash = Convert.ToHexStringLower(SHA256.HashData(Guid.NewGuid().ToByteArray())),
            BudgetJson = "{}", UsageJson = "{}"
        };

        var first = await store.TryCreateAttemptAsync(NewAttempt(), 2, HealingBudgetOptions.MaximumConcurrency);
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("lease-token")));
        var leased = await store.TryAcquireLeaseAsync(
            workspaceId, first.Attempt!.Id, "workflow-1", tokenHash, now, now.AddMinutes(5));
        var wrongHeartbeat = await store.TryHeartbeatLeaseAsync(
            workspaceId, first.Attempt.Id, new string('c', 64), now.AddMinutes(1), now.AddMinutes(6));
        var recorded = await store.TryRecordReproductionAsync(
            workspaceId, first.Attempt.Id, tokenHash, RepairClassification.Reproduced,
            "{\"reproduced\":true}", now.AddMinutes(1));
        await fixture.Db.RepairAttempts.Where(x => x.Id == first.Attempt.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, RepairAttemptStatus.Failed));
        var second = await store.TryCreateAttemptAsync(NewAttempt(), 2, HealingBudgetOptions.MaximumConcurrency);
        await fixture.Db.RepairAttempts.Where(x => x.Id == second.Attempt!.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, RepairAttemptStatus.Failed));
        var capped = await store.TryCreateAttemptAsync(NewAttempt(), 2, HealingBudgetOptions.MaximumConcurrency);

        Assert.Equal(RepairAttemptStoreOutcome.Created, first.Outcome);
        Assert.Equal(1, first.Attempt.AttemptNumber);
        Assert.Equal(RepairAttemptStoreOutcome.Created, second.Outcome);
        Assert.Equal(2, second.Attempt!.AttemptNumber);
        Assert.Equal(RepairAttemptStoreOutcome.AttemptLimitReached, capped.Outcome);
        Assert.True(leased);
        Assert.False(wrongHeartbeat);
        Assert.True(recorded);
        Assert.Equal(RepairClassification.Reproduced, (await store.FindAttemptAsync(workspaceId, first.Attempt.Id))!.RepairClassification);
    }

    [Fact]
    public async Task Configuration_upsert_reconciles_environment_overrides_and_rejects_stale_version()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var firstEnvironmentId = Guid.NewGuid();
        var secondEnvironmentId = Guid.NewGuid();
        var created = await store.UpsertConfigurationAsync(CreateConfiguration(workspaceId, applicationId,
            CreateEnvironment(workspaceId, applicationId, firstEnvironmentId, false)));
        var staleVersion = created.Version.ToArray();
        var update = CreateConfiguration(workspaceId, applicationId,
            CreateEnvironment(workspaceId, applicationId, secondEnvironmentId, true));
        update.Version = created.Version.ToArray();

        await store.UpsertConfigurationAsync(update);
        var persisted = await store.GetConfigurationAsync(workspaceId, applicationId);
        var stale = CreateConfiguration(workspaceId, applicationId);
        stale.Version = staleVersion;
        var staleWrite = () => store.UpsertConfigurationAsync(stale).AsTask();

        Assert.Equal(secondEnvironmentId, Assert.Single(persisted!.Environments).EnvironmentId);
        Assert.True(persisted.Environments.Single().RepairEnabled);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(staleWrite);
    }

    [Fact]
    public async Task Identical_first_configuration_create_is_an_idempotent_replay()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var environmentId = Guid.NewGuid();
        var accepted = await store.UpsertConfigurationAsync(CreateConfiguration(
            workspaceId,
            applicationId,
            CreateEnvironment(workspaceId, applicationId, environmentId, true)));
        var replay = CreateConfiguration(
            workspaceId,
            applicationId,
            CreateEnvironment(workspaceId, applicationId, environmentId, true));

        var result = await store.UpsertConfigurationAsync(replay);
        replay.RepairEnabled = true;
        var conflict = () => store.UpsertConfigurationAsync(replay).AsTask();

        Assert.Equal(accepted.Id, result.Id);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(conflict);
    }

    [Fact]
    public async Task Identical_first_verification_create_is_an_idempotent_replay()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var incident = CreateIncident(workspaceId, applicationId);
        var episode = CreateEpisode(workspaceId, applicationId, incident.Id);
        var environmentId = Guid.NewGuid();
        fixture.Db.HealingIncidents.Add(incident);
        fixture.Db.IncidentEpisodes.Add(episode);
        await fixture.Db.SaveChangesAsync();
        var store = new HealingStore(fixture.Db);
        var accepted = CreateVerification(workspaceId, applicationId, episode.Id, environmentId, VerificationOutcome.Deployed);
        await store.UpsertVerificationAsync(accepted);
        var replay = CreateVerification(workspaceId, applicationId, episode.Id, environmentId, VerificationOutcome.Deployed);

        var result = await store.UpsertVerificationAsync(replay);
        replay.Outcome = VerificationOutcome.Healed;
        var conflict = () => store.UpsertVerificationAsync(replay).AsTask();

        Assert.Equal(accepted.Id, result.Id);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(conflict);
    }

    [Fact]
    public async Task Manifest_revision_is_idempotent_and_conflicting_graph_is_fully_detached()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var accepted = CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-a");
        await store.AppendManifestAsync(accepted);
        var replay = await store.AppendManifestAsync(CreateManifest(workspaceId, applicationId, revisionId, "digest-a", "component-b"));
        var conflicting = CreateManifest(workspaceId, applicationId, revisionId, "digest-b", "component-c");

        var conflict = () => store.AppendManifestAsync(conflicting).AsTask();

        Assert.True(replay.IsReplay);
        await Assert.ThrowsAsync<HealingIdempotencyConflictException>(conflict);
        var addedGraphEntries = fixture.Db.ChangeTracker.Entries()
            .Where(x => x.State == EntityState.Added &&
                        (x.Entity == conflicting ||
                         x.Entity is ComponentManifestEntry entry && conflicting.Entries.Contains(entry)))
            .ToList();
        Assert.Empty(addedGraphEntries);
        await fixture.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Workspace_kill_switch_configuration_is_unique_and_versioned()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var workspaceId = Guid.NewGuid();
        var created = await store.UpsertWorkspaceConfigurationAsync(new HealingWorkspaceConfiguration
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        var staleVersion = created.Version.ToArray();
        var update = new HealingWorkspaceConfiguration
        {
            WorkspaceId = workspaceId,
            WorkspaceKillSwitch = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = created.Version.ToArray()
        };

        await store.UpsertWorkspaceConfigurationAsync(update);
        update.Version = staleVersion;
        var stale = () => store.UpsertWorkspaceConfigurationAsync(update).AsTask();

        Assert.True((await store.GetWorkspaceConfigurationAsync(workspaceId))!.WorkspaceKillSwitch);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(stale);
    }

    [Fact]
    public async Task Concurrent_audit_appends_allocate_unique_monotonic_aggregate_sequences()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"valence-control-healing-audit-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Default Timeout=30";
        var workspaceId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        try
        {
            await using (var setup = CreateFileContext(connectionString))
                await setup.Database.EnsureCreatedAsync();

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
            {
                await start.Task;
                await using var db = CreateFileContext(connectionString);
                await new HealingStore(db).AppendAsync(CreateAuditEvent(workspaceId, aggregateId, index));
            })).ToArray();
            start.SetResult();
            await Task.WhenAll(writes);

            await using var verification = CreateFileContext(connectionString);
            var sequences = await verification.Set<HealingAuditEvent>().AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.AggregateId == aggregateId)
                .OrderBy(x => x.Sequence)
                .Select(x => x.Sequence)
                .ToListAsync();
            Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, sequences);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static HealingSignalInboxItem CreateInboxItem(string idempotencyKey, string hash) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = new Guid("10000000-0000-0000-0000-000000000001"),
            ApplicationId = new Guid("20000000-0000-0000-0000-000000000002"),
            EnvironmentId = new Guid("30000000-0000-0000-0000-000000000003"),
            IdempotencyKey = idempotencyKey,
            Source = HealingSignalSource.OpenTelemetry,
            ProfileVersion = "1.0",
            OccurredAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
            AcceptedAt = DateTimeOffset.Parse("2026-07-16T10:00:01Z"),
            RedactedEnvelopeJson = "{}",
            EnvelopeHash = hash,
            Status = HealingInboxStatus.Pending
        };

    private static ProviderOperation CreateProviderOperation(string idempotencyKey, string payloadHash, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = new Guid("10000000-0000-0000-0000-000000000001"),
            ApplicationId = new Guid("20000000-0000-0000-0000-000000000002"),
            ProviderConnectionId = new Guid("40000000-0000-0000-0000-000000000004"),
            Kind = ProviderOperationKind.DispatchWorkflow,
            IdempotencyKey = idempotencyKey,
            PayloadJson = "{}",
            PayloadHash = payloadHash,
            Status = ProviderOperationStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static HealingConfiguration CreateConfiguration(
        Guid workspaceId,
        Guid applicationId,
        params HealingEnvironmentConfiguration[] environments) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        DiscoveryEnabled = true,
        SignalProfileVersion = "1.0",
        DefaultAttemptLimit = 2,
        VerificationWindow = TimeSpan.FromMinutes(10),
        CreatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
        Environments = [.. environments]
    };

    private static HealingEnvironmentConfiguration CreateEnvironment(
        Guid workspaceId,
        Guid applicationId,
        Guid environmentId,
        bool repairEnabled) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        EnvironmentId = environmentId,
        RepairEnabled = repairEnabled,
        CreatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z")
    };

    private static ComponentManifestModel CreateManifest(
        Guid workspaceId,
        Guid applicationId,
        Guid revisionId,
        string digest,
        string componentKey)
    {
        var manifest = new ComponentManifestModel
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            RevisionId = revisionId,
            SchemaVersion = "1.0",
            SourceRevision = "abc123",
            ManifestDigest = digest,
            CanonicalJson = "{}",
            CreatedAt = DateTimeOffset.Parse("2026-07-16T10:00:00Z")
        };
        manifest.Entries.Add(new ComponentManifestEntry
        {
            Id = Guid.NewGuid(),
            ManifestId = manifest.Id,
            WorkspaceId = workspaceId,
            ApplicationId = applicationId,
            ComponentKey = componentKey,
            Kind = ComponentKind.Package,
            Name = componentKey,
            ContentHash = "hash",
            RelativePath = $"packages/{componentKey}.dll"
        });
        return manifest;
    }

    private static ComponentManifestAssemblyArtifact CreateAssembly(
        ComponentManifestModel manifest,
        ComponentManifestEntry entry,
        string relativePath) => new()
    {
        Id = Guid.NewGuid(), ManifestId = manifest.Id, ComponentEntryId = entry.Id,
        WorkspaceId = manifest.WorkspaceId, ApplicationId = manifest.ApplicationId,
        Name = Path.GetFileNameWithoutExtension(relativePath), Version = "1.0.0.0",
        RelativePath = relativePath, ContentHash = "hash"
    };

    private static T CreatePolicy<T>(Guid workspaceId, Guid applicationId, string name)
        where T : HealingPolicyDefinition, new() => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = workspaceId, ApplicationId = applicationId,
        Name = name, PolicyVersion = "1", PolicyHash = "hash"
    };

    private static SourceOwnershipBinding CloneBinding(SourceOwnershipBinding source) => new()
    {
        Id = source.Id, WorkspaceId = source.WorkspaceId, ApplicationId = source.ApplicationId, Name = source.Name,
        SelectorKind = source.SelectorKind, SelectorPattern = source.SelectorPattern, Priority = source.Priority,
        ProviderConnectionId = source.ProviderConnectionId, RepositoryProviderId = source.RepositoryProviderId,
        RepositoryOwner = source.RepositoryOwner, RepositoryName = source.RepositoryName, TargetBranch = source.TargetBranch,
        WorkflowIdentity = source.WorkflowIdentity, WorkflowReference = source.WorkflowReference, WorkflowRevision = source.WorkflowRevision,
        PathPolicyId = source.PathPolicyId, EvidencePolicyId = source.EvidencePolicyId, MergePolicyId = source.MergePolicyId,
        Status = source.Status, ApprovedBy = source.ApprovedBy, ApprovedAt = source.ApprovedAt,
        CreatedAt = source.CreatedAt, UpdatedAt = source.UpdatedAt, Version = source.Version.ToArray()
    };

    private static HealingDbContext CreateFileContext(string connectionString) => new(
        new DbContextOptionsBuilder<HealingDbContext>().UseSqlite(connectionString).Options);

    private static HealingAuditEvent CreateAuditEvent(Guid workspaceId, Guid aggregateId, int index) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        AggregateType = "incident",
        AggregateId = aggregateId,
        EventType = "incident.observed",
        ReasonCode = $"accepted-{index}",
        ActorType = "control",
        ActorId = "healing-inbox",
        CorrelationId = Guid.NewGuid(),
        SafeDetailJson = "{}",
        OccurredAt = DateTimeOffset.UtcNow
    };

    private static HealingIncident CreateIncident(Guid workspaceId, Guid applicationId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        FingerprintVersion = "1",
        Fingerprint = Guid.NewGuid().ToString("N"),
        RepairRepositoryKey = "observation-only",
        Status = HealingIncidentStatus.Observed,
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
        OccurrenceCount = 1
    };

    private static IncidentEpisode CreateEpisode(Guid workspaceId, Guid applicationId, Guid incidentId) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        IncidentId = incidentId,
        OpenedAt = DateTimeOffset.UtcNow,
        ProducingRevisionsJson = "[]",
        Outcome = IncidentEpisodeOutcome.Active
    };

    private static VerificationResult CreateVerification(
        Guid workspaceId,
        Guid applicationId,
        Guid episodeId,
        Guid environmentId,
        VerificationOutcome outcome) => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        EpisodeId = episodeId,
        EnvironmentId = environmentId,
        RepairedRevision = "abc123",
        Outcome = outcome
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
