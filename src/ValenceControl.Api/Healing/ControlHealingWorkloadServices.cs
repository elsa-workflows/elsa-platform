using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using ValenceControl.Api.Workspace.Healing;
using ValenceControl.Healing.Abstractions;
using ValenceControl.Healing.Agent;
using ValenceControl.Healing.Core;
using ValenceControl.Healing.Core.Configuration;
using ValenceControl.Healing.Core.Repairs;
using ValenceControl.Healing.Core.Security;
using ValenceControl.Healing.GitHub;
using ValenceControl.Healing.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Api.Healing;

public sealed class HealingGitHubOptions
{
    public const string SectionName = "Healing:GitHub";
    public string WorkloadAudience { get; set; } = "valence-control-healing";
    public string? ControlBaseUrl { get; set; }
    public TimeSpan CapabilityLifetime { get; set; } = TimeSpan.FromMinutes(35);
    public TimeSpan AttemptLeaseLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ProposalLifetime { get; set; } = TimeSpan.FromHours(2);
}

internal sealed record ControlHealingWorkloadAuthority(
    RepairAttempt Attempt,
    SourceOwnershipBinding Binding,
    Guid ProviderConnectionId);

/// <summary>
/// Resolves current authority for every workload operation. Capability tokens are deliberately not authority
/// snapshots: all control, tenant, environment, episode, provider, and binding controls remain live.
/// </summary>
public sealed class ControlHealingWorkloadAuthorityService(
    HealingDbContext dbContext,
    HealingKillSwitch killSwitch)
{
    internal async ValueTask<ControlHealingWorkloadAuthority?> ResolveAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var authority = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join episode in dbContext.IncidentEpisodes.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EpisodeId }
                equals new { episode.WorkspaceId, episode.ApplicationId, episode.Id }
            join incident in dbContext.HealingIncidents.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.IncidentId }
                equals new { incident.WorkspaceId, incident.ApplicationId, incident.Id }
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
            join provider in dbContext.ProviderConnections.AsNoTracking()
                on new { attempt.WorkspaceId, Id = binding.ProviderConnectionId }
                equals new { provider.WorkspaceId, provider.Id }
            where attempt.WorkspaceId == workspaceId && attempt.Id == attemptId &&
                  episode.Outcome == IncidentEpisodeOutcome.Active &&
                  incident.ActiveEpisodeId == attempt.EpisodeId &&
                  incident.Status == HealingIncidentStatus.Repairing &&
                  binding.Status == SourceOwnershipBindingStatus.Active &&
                  provider.Status == ProviderConnectionStatus.Active
            select new ControlHealingWorkloadAuthority(attempt, binding, provider.Id))
            .SingleOrDefaultAsync(cancellationToken);
        if (authority is null)
            return null;

        var workspace = await dbContext.HealingWorkspaceConfigurations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == authority.Attempt.WorkspaceId,
            cancellationToken);
        var application = await dbContext.HealingConfigurations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == authority.Attempt.WorkspaceId &&
                 x.ApplicationId == authority.Attempt.ApplicationId,
            cancellationToken);
        if (workspace is null || application is null)
            return null;

        var environmentIds = await dbContext.EnvironmentImpacts.AsNoTracking()
            .Where(x => x.WorkspaceId == authority.Attempt.WorkspaceId &&
                        x.ApplicationId == authority.Attempt.ApplicationId &&
                        x.EpisodeId == authority.Attempt.EpisodeId)
            .Select(x => x.EnvironmentId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (environmentIds.Length == 0)
            return null;

        var environments = await dbContext.HealingEnvironmentConfigurations.AsNoTracking()
            .Where(x => x.WorkspaceId == authority.Attempt.WorkspaceId &&
                        x.ApplicationId == authority.Attempt.ApplicationId &&
                        environmentIds.Contains(x.EnvironmentId))
            .ToArrayAsync(cancellationToken);
        var byId = environments.ToDictionary(x => x.EnvironmentId);
        if (!environmentIds.All(id => byId.TryGetValue(id, out var environment) &&
                                      killSwitch.CanDispatchRepair(workspace, application, environment).Allowed))
            return null;

        return authority;
    }

    public async ValueTask RevokeCapabilitiesAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default) =>
        await dbContext.WorkloadIdentityExchanges
            .Where(x => x.WorkspaceId == workspaceId && x.AttemptId == attemptId &&
                        (x.Status == WorkloadIdentityExchangeStatus.Pending ||
                         x.Status == WorkloadIdentityExchangeStatus.Exchanged))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, WorkloadIdentityExchangeStatus.Revoked)
                .SetProperty(x => x.CapabilityTokenHash, (string?)null)
                .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
}

/// <summary>
/// Closes managed-inference crash windows even when the original workload never retries. Recovery runs before
/// normal repair coordination (including while the control kill switch is set) and never reacquires inference.
/// </summary>
public static class ControlManagedInferenceRecovery
{
    public static async ValueTask<int> RecoverExpiredAsync(
        HealingDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var candidates = await dbContext.ManagedRepairInferenceReservations.AsNoTracking()
            .Where(x => x.Status == ManagedRepairInferenceReservationStatus.Leased && x.LeaseExpiresAt <= now)
            .OrderBy(x => x.LeaseExpiresAt)
            .ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.WorkspaceId, x.ApplicationId, x.AttemptId })
            .Take(32)
            .ToArrayAsync(cancellationToken);
        var recovered = 0;
        foreach (var candidate in candidates)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, candidate.WorkspaceId, candidate.ApplicationId, cancellationToken);
            var reservation = await dbContext.ManagedRepairInferenceReservations.SingleOrDefaultAsync(
                x => x.Id == candidate.Id &&
                     x.Status == ManagedRepairInferenceReservationStatus.Leased &&
                     x.LeaseExpiresAt <= now,
                cancellationToken);
            var attempt = reservation is null
                ? null
                : await dbContext.RepairAttempts.SingleOrDefaultAsync(
                    x => x.Id == candidate.AttemptId &&
                         x.WorkspaceId == candidate.WorkspaceId &&
                         x.ApplicationId == candidate.ApplicationId,
                    cancellationToken);
            if (reservation is null || attempt is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                continue;
            }

            reservation.Status = ManagedRepairInferenceReservationStatus.Abandoned;
            reservation.OutcomeCode = "managed-inference-outcome-indeterminate";
            reservation.UpdatedAt = now;
            reservation.CompletedAt = now;
            reservation.Version = Guid.NewGuid().ToByteArray();
            if (attempt.Status is RepairAttemptStatus.Running or RepairAttemptStatus.Dispatched ||
                attempt.Status == RepairAttemptStatus.Failed && attempt.OutcomeCode == "repair-attempt-abandoned")
            {
                attempt.Status = RepairAttemptStatus.Failed;
                attempt.OutcomeCode = "managed-inference-outcome-indeterminate";
                attempt.SafeOutcomeDetail = "managed-inference-outcome-indeterminate";
                attempt.CompletedAt = now;
                attempt.LeaseOwner = null;
                attempt.LeaseToken = null;
                attempt.LeaseExpiresAt = null;
                attempt.Version = Guid.NewGuid().ToByteArray();
            }
            var incident = await dbContext.HealingIncidents.SingleOrDefaultAsync(
                x => x.WorkspaceId == attempt.WorkspaceId &&
                     x.ApplicationId == attempt.ApplicationId &&
                     x.Id == attempt.IncidentId &&
                     x.ActiveEpisodeId == attempt.EpisodeId,
                cancellationToken);
            if (incident?.Status == HealingIncidentStatus.Repairing)
            {
                incident.TryTransitionTo(HealingIncidentStatus.NeedsHuman);
                incident.NeedsHumanReason = NeedsHumanReason.PolicyBlocked;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            await new HealingAuditService(new HealingStore(dbContext), timeProvider).AppendAsync(new(
                attempt.WorkspaceId,
                "repair-attempt",
                attempt.Id,
                "repair-inference-abandoned",
                "managed-inference-outcome-indeterminate",
                "control",
                "healing-workload-api",
                attempt.IncidentId,
                attempt.EpisodeId,
                null,
                null,
                null,
                new Dictionary<string, string?>
                {
                    ["status"] = attempt.Status.ToString().ToLowerInvariant()
                }), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            recovered++;
            dbContext.ChangeTracker.Clear();
        }

        return recovered;
    }
}

public sealed class ControlHealingWorkloadRequestAuthorizer(
    HealingDbContext dbContext,
    ControlHealingWorkloadAuthorityService authorityService,
    TimeProvider timeProvider) : IHealingWorkloadRequestAuthorizer
{
    public async ValueTask<HealingWorkloadAuthorizationResult> AuthorizeExchangeAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var authority = await authorityService.ResolveAsync(workspaceId, attemptId, cancellationToken);
        var authorized = authority is not null &&
                         authority.Attempt.Status != RepairAttemptStatus.Succeeded &&
                         authority.Attempt.Status != RepairAttemptStatus.Failed &&
                         authority.Attempt.Status != RepairAttemptStatus.Stopped &&
                         authority.Attempt.Status != RepairAttemptStatus.Expired;
        if (!authorized)
            await authorityService.RevokeCapabilitiesAsync(workspaceId, attemptId, cancellationToken);
        return authorized
            ? HealingWorkloadAuthorizationResult.Allow()
            : HealingWorkloadAuthorizationResult.Deny(
                "healing.workload.exchange.denied",
                HttpStatusCode.Forbidden);
    }

    public async ValueTask<HealingWorkloadAuthorizationResult> AuthorizeAsync(
        HealingWorkloadAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!WorkloadCapabilityScopes.All.Contains(request.RequiredScope))
            return HealingWorkloadAuthorizationResult.Deny("healing.workload.scope.denied");
        var now = timeProvider.GetUtcNow();
        var exchanges = await dbContext.WorkloadIdentityExchanges.AsNoTracking()
            .Where(x => x.WorkspaceId == request.WorkspaceId &&
                        x.AttemptId == request.AttemptId &&
                        x.Status == WorkloadIdentityExchangeStatus.Exchanged &&
                        x.ExpiresAt > now &&
                        x.CapabilityTokenHash != null)
            .OrderByDescending(x => x.ExchangedAt)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        var suppliedHash = Hash(request.CapabilityToken);
        var matches = exchanges.Where(x => FixedEquals(x.CapabilityTokenHash!, suppliedHash)).ToArray();
        var liveAuthority = matches.Length == 1
            ? await authorityService.ResolveAsync(request.WorkspaceId, request.AttemptId, cancellationToken)
            : null;
        var authorized = liveAuthority is not null &&
                         ParseScopes(matches[0].ScopesJson).Contains(request.RequiredScope);
        if (!authorized)
            await authorityService.RevokeCapabilitiesAsync(request.WorkspaceId, request.AttemptId, cancellationToken);
        return authorized
            ? HealingWorkloadAuthorizationResult.Allow()
            : HealingWorkloadAuthorizationResult.Deny(
                "healing.workload.capability.denied",
                HttpStatusCode.Unauthorized);
    }

    internal static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static bool FixedEquals(string left, string right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));

    private static IReadOnlySet<string> ParseScopes(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? []).ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}

public sealed class ControlHealingWorkloadApi(
    HealingDbContext dbContext,
    ControlHealingWorkloadAuthorityService authorityService,
    GitHubWorkloadIdentityValidator identityValidator,
    IRepairOrchestrationStore orchestrationStore,
    RepairOrchestrationService orchestrationService,
    IRepairProposalProvider proposalProvider,
    IDataProtectionProvider dataProtectionProvider,
    HealingAuditService auditService,
    TimeProvider timeProvider,
    HealingGitHubOptions options) : IHealingWorkloadApi
{
    private const int MaximumResultEnvelopeBytes = 2_097_152;
    private static readonly IReadOnlySet<string> InitialScopes = new HashSet<string>(
        [WorkloadCapabilityScopes.ReadEvidence, WorkloadCapabilityScopes.CreateProposal, WorkloadCapabilityScopes.HeartbeatAttempt],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> FinalizationScopes = new HashSet<string>(
        [WorkloadCapabilityScopes.FinalizeProposal, WorkloadCapabilityScopes.UploadResult], StringComparer.Ordinal);
    private readonly IDataProtector _proposalNonceProtector = dataProtectionProvider.CreateProtector(
        "ValenceControl.Healing.ProposalFinalizationNonce.v1");

    public async ValueTask<WorkloadCapabilityGrant> ExchangeAsync(
        WorkloadIdentityExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var attemptScope = await dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.Id == request.AttemptId)
            .Select(x => new { x.WorkspaceId, x.Status })
            .SingleOrDefaultAsync(cancellationToken);
        var authority = attemptScope is null
            ? null
            : await authorityService.ResolveAsync(attemptScope.WorkspaceId, request.AttemptId, cancellationToken);
        if (authority is null || IsTerminal(authority.Attempt.Status))
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.exchange.denied");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext,
                authority.Attempt.WorkspaceId,
                authority.Attempt.ApplicationId,
                cancellationToken))
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.exchange.denied");
        authority = await authorityService.ResolveAsync(
            authority.Attempt.WorkspaceId,
            authority.Attempt.Id,
            cancellationToken);
        if (authority is null || IsTerminal(authority.Attempt.Status))
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.exchange.denied");
        var repository = $"{authority.Binding.RepositoryOwner}/{authority.Binding.RepositoryName}";
        var expectation = new GitHubWorkloadIdentityExpectation(
            authority.Attempt.WorkspaceId,
            authority.Attempt.ApplicationId,
            authority.Attempt.Id,
            authority.Attempt.NonceHash,
            $"repo:{repository}:ref:{authority.Binding.WorkflowReference}",
            authority.Binding.RepositoryProviderId,
            authority.Binding.RepositoryOwner,
            authority.Binding.RepositoryName,
            $"{repository}/{authority.Binding.WorkflowIdentity}@{authority.Binding.WorkflowReference}",
            authority.Binding.WorkflowRevision,
            authority.Binding.WorkflowReference,
            authority.Binding.WorkflowRevision,
            "initial",
            null,
            InitialScopes);
        var validation = await identityValidator.ValidateAsync(
            request.IdentityAssertion,
            request.OneTimeNonce,
            expectation,
            cancellationToken);
        if (!validation.Succeeded || validation.Identity is null)
            throw Rejected(HttpStatusCode.Unauthorized, validation.ReasonCode);

        var lease = await orchestrationService.AcquireLeaseAsync(
            authority.Attempt.WorkspaceId,
            authority.Attempt.Id,
            $"github:{validation.Identity.RunId}:{validation.Identity.RunAttempt}",
            options.AttemptLeaseLifetime,
            cancellationToken);
        if (!lease.Succeeded || lease.ExpiresAt is null)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.attempt-lease-unavailable");

        authority = await authorityService.ResolveAsync(
            authority.Attempt.WorkspaceId,
            authority.Attempt.Id,
            cancellationToken);
        if (authority is null || authority.Attempt.Status != RepairAttemptStatus.Running)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.exchange.denied");

        var grant = await IssueCapabilityAsync(
            authority.Attempt,
            validation.Identity,
            InitialScopes,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return grant;
    }

    public async ValueTask<WorkloadEvidenceResponse> GetEvidenceAsync(
        WorkloadEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var attemptScope = await dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.Id == request.AttemptId)
            .Select(x => new { x.WorkspaceId, x.ApplicationId })
            .SingleOrDefaultAsync(cancellationToken);
        if (attemptScope is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.evidence.authority-revoked");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attemptScope.WorkspaceId, attemptScope.ApplicationId, cancellationToken) ||
            await authorityService.ResolveAsync(attemptScope.WorkspaceId, request.AttemptId, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.evidence.authority-revoked");

        var evidence = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join bundle in dbContext.EvidenceBundles.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EvidenceBundleId }
                equals new { bundle.WorkspaceId, bundle.ApplicationId, bundle.Id }
            where attempt.Id == request.AttemptId
            select new { attempt.Id, attempt.BudgetJson, Bundle = bundle }).SingleOrDefaultAsync(cancellationToken);
        if (evidence is null || evidence.Bundle.ExpiresAt <= timeProvider.GetUtcNow())
            throw Rejected(HttpStatusCode.Gone, "healing.workload.evidence.unavailable");

        var response = new WorkloadEvidenceResponse(
            HealingContractVersions.WorkloadProtocol,
            evidence.Id,
            new RepairEvidenceBundle(
                HealingContractVersions.AgentProtocol,
                evidence.Id,
                evidence.Bundle.Tier == EvidenceTier.Elevated ? "elevated" : "default-redacted",
                evidence.Bundle.CanonicalJson,
                PrefixDigest(evidence.Bundle.Digest),
                ParseStringArray(evidence.Bundle.OmissionsJson),
                evidence.Bundle.ExpiresAt),
            ParseBudget(evidence.BudgetJson));
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async ValueTask<WorkloadProposalCreateResponse> CreateProposalAsync(
        WorkloadProposalCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var proposalInput = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join bundle in dbContext.EvidenceBundles.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EvidenceBundleId }
                equals new { bundle.WorkspaceId, bundle.ApplicationId, bundle.Id }
            where attempt.Id == request.AttemptId
            select new { Attempt = attempt, Evidence = bundle }).SingleOrDefaultAsync(cancellationToken);
        var liveAuthority = proposalInput is null
            ? null
            : await authorityService.ResolveAsync(
                proposalInput.Attempt.WorkspaceId,
                proposalInput.Attempt.Id,
                cancellationToken);
        if (proposalInput is null || liveAuthority is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.proposal.authority-revoked");
        var replayCandidate = await dbContext.ManagedRepairProposals.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == request.AttemptId,
            cancellationToken);
        if (replayCandidate is not null)
            return await ReplayProposalWithAuditAsync(replayCandidate, request, cancellationToken);
        if (proposalInput.Attempt.Status != RepairAttemptStatus.Running ||
            proposalInput.Attempt.LeaseExpiresAt <= now || proposalInput.Evidence.ExpiresAt <= now)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.attempt-invalid");

        var sourceContext = new RepairSourceContextBundle(
            request.SourceContext.TargetRevision,
            request.SourceContext.Digest,
            request.SourceContext.Files.Select(x => new RepairSourceFile(x.Path, x.Content, x.Digest, x.IsTruncated)).ToArray(),
            request.SourceContext.OmittedPaths.ToArray());
        var budget = ParseBudget(proposalInput.Attempt.BudgetJson);
        var proposalRequest = new RepairProposalRequest(
            HealingContractVersions.AgentProtocol,
            proposalInput.Attempt.Id,
            BaseRevision(proposalInput.Attempt),
            proposalInput.Attempt.TargetRevision,
            proposalInput.Attempt.ProducingRevision,
            new(
                proposalInput.Evidence.Tier == EvidenceTier.Elevated ? "elevated" : "default-redacted",
                proposalInput.Evidence.CanonicalJson,
                PrefixDigest(proposalInput.Evidence.Digest),
                ParseStringArray(proposalInput.Evidence.OmissionsJson)),
            sourceContext,
            budget);
        try
        {
            RepairProposalProtocol.ValidateRequest(proposalRequest);
        }
        catch (RepairAgentProtocolException exception)
        {
            throw Rejected(HttpStatusCode.UnprocessableEntity, exception.ReasonCode);
        }

        var admission = await AcquireInferenceReservationAsync(request, sourceContext, budget, cancellationToken);
        if (admission.Replay is not null)
            return admission.Replay;

        RepairProposal proposed;
        try
        {
            proposed = await proposalProvider.ProposeAsync(proposalRequest, cancellationToken);
            RepairProposalProtocol.ValidateProposal(proposed, budget);
        }
        catch (RepairAgentProtocolException exception)
        {
            await RejectInferenceReservationAsync(
                request.AttemptId,
                admission.Lease!.LeaseToken,
                "managed-inference-response-rejected",
                cancellationToken);
            throw Rejected(HttpStatusCode.UnprocessableEntity, exception.ReasonCode);
        }

        var proposalId = Guid.NewGuid();
        var createdAt = timeProvider.GetUtcNow();
        var expiresAt = createdAt.Add(options.ProposalLifetime);
        var payload = new StoredManagedProposal(
            BaseRevision(proposalInput.Attempt),
            proposalInput.Attempt.TargetRevision,
            proposed.Classification,
            proposed.Confidence,
            proposed.CausalSummary,
            proposed.UnifiedDiff,
            RepairAgentGateway.ComputeSha256Digest(proposed.UnifiedDiff),
            proposed.ChangedPaths.ToArray(),
            proposed.RiskSuggestions.ToArray(),
            proposed.RollbackSummary,
            new(
                proposed.Usage.InputUnits,
                proposed.Usage.OutputUnits,
                proposed.Usage.InferenceDuration,
                TimeSpan.Zero,
                0));
        var proposalJson = JsonSerializer.Serialize(payload);
        var proposalDigest = RepairAgentGateway.ComputeSha256Digest(proposalJson);
        var nonceBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var finalizationNonce = Base64UrlEncode(nonceBytes);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
                x => x.Id == request.AttemptId,
                cancellationToken);
            if (attempt is null)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.attempt-invalid");
            await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken);
            var reservation = await dbContext.ManagedRepairInferenceReservations.SingleOrDefaultAsync(
                x => x.AttemptId == request.AttemptId &&
                     x.LeaseTokenHash == ControlHealingWorkloadRequestAuthorizer.Hash(admission.Lease!.LeaseToken),
                cancellationToken);
            if (reservation is null || reservation.Status != ManagedRepairInferenceReservationStatus.Leased ||
                reservation.LeaseExpiresAt <= createdAt)
            {
                if (reservation is not null && reservation.Status == ManagedRepairInferenceReservationStatus.Leased)
                    await TerminateInferenceReservationAsync(
                        reservation,
                        attempt,
                        ManagedRepairInferenceReservationStatus.Abandoned,
                        RepairAttemptStatus.Failed,
                        "managed-inference-outcome-indeterminate",
                        "repair-inference-abandoned",
                        cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(HttpStatusCode.Gone, "healing.workload.inference-outcome-indeterminate");
            }
            if (await authorityService.ResolveAsync(attempt.WorkspaceId, attempt.Id, cancellationToken) is null)
            {
                await TerminateInferenceReservationAsync(
                    reservation,
                    attempt,
                    ManagedRepairInferenceReservationStatus.Revoked,
                    RepairAttemptStatus.Stopped,
                    "managed-inference-authority-revoked",
                    "repair-inference-revoked",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(HttpStatusCode.Forbidden, "healing.workload.proposal.authority-revoked");
            }
            if (attempt.Status != RepairAttemptStatus.Running || attempt.LeaseExpiresAt <= createdAt)
            {
                await TerminateInferenceReservationAsync(
                    reservation,
                    attempt,
                    ManagedRepairInferenceReservationStatus.Abandoned,
                    RepairAttemptStatus.Failed,
                    "managed-inference-outcome-indeterminate",
                    "repair-inference-abandoned",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(HttpStatusCode.Gone, "healing.workload.inference-outcome-indeterminate");
            }

            var existing = await dbContext.ManagedRepairProposals.AsNoTracking().SingleOrDefaultAsync(
                x => x.AttemptId == request.AttemptId,
                cancellationToken);
            if (existing is not null)
            {
                await AuditAsync(attempt, "repair-proposal-created", "proposal-created", existing.ProposalDigest, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ReplayProposal(existing, request);
            }

            var entity = new ManagedRepairProposal
            {
                Id = proposalId,
                WorkspaceId = attempt.WorkspaceId,
                ApplicationId = attempt.ApplicationId,
                AttemptId = attempt.Id,
                IdempotencyKey = request.IdempotencyKey,
                SourceContextDigest = sourceContext.Digest,
                ProposalDigest = proposalDigest,
                ProposalJson = proposalJson,
                FinalizationNonceHash = ControlHealingWorkloadRequestAuthorizer.Hash(finalizationNonce),
                ProtectedFinalizationNonce = _proposalNonceProtector.Protect(finalizationNonce),
                Status = ManagedRepairProposalStatus.Ready,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt,
                Version = Guid.NewGuid().ToByteArray()
            };
            dbContext.ManagedRepairProposals.Add(entity);
            attempt.Status = RepairAttemptStatus.ProposalReady;
            attempt.LeaseOwner = null;
            attempt.LeaseToken = null;
            attempt.LeaseExpiresAt = null;
            SetUsage(attempt, payload.Usage);
            attempt.Version = Guid.NewGuid().ToByteArray();
            reservation.Status = ManagedRepairInferenceReservationStatus.Completed;
            reservation.OutcomeCode = "proposal-created";
            reservation.CompletedAt = createdAt;
            reservation.UpdatedAt = createdAt;
            reservation.Version = Guid.NewGuid().ToByteArray();
            await dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                attempt,
                "repair-proposal-created",
                "proposal-created",
                proposalDigest,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProposalResponse(entity, payload, finalizationNonce, false);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.ManagedRepairProposals.AsNoTracking().SingleOrDefaultAsync(
                x => x.AttemptId == request.AttemptId,
                cancellationToken);
            if (winner is null)
                throw;
            return await ReplayProposalWithAuditAsync(winner, request, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonceBytes);
        }
    }

    private sealed record InferenceReservationLease(string LeaseToken, DateTimeOffset ExpiresAt);

    private sealed record InferenceReservationAdmission(
        InferenceReservationLease? Lease,
        WorkloadProposalCreateResponse? Replay);

    private async ValueTask<WorkloadProposalCreateResponse> ReplayProposalWithAuditAsync(
        ManagedRepairProposal proposal,
        WorkloadProposalCreateRequest request,
        CancellationToken cancellationToken)
    {
        var replay = ReplayProposal(proposal, request);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleAsync(x => x.Id == proposal.AttemptId, cancellationToken);
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken) ||
            await authorityService.ResolveAsync(attempt.WorkspaceId, attempt.Id, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.proposal.authority-revoked");
        await AuditAsync(
            attempt,
            "repair-proposal-created",
            "proposal-created",
            proposal.ProposalDigest,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replay;
    }

    private async ValueTask<InferenceReservationAdmission> AcquireInferenceReservationAsync(
        WorkloadProposalCreateRequest request,
        RepairSourceContextBundle sourceContext,
        RepairAgentBudget budget,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
            x => x.Id == request.AttemptId,
            cancellationToken);
        if (attempt is null ||
            !await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken) ||
            await authorityService.ResolveAsync(attempt.WorkspaceId, attempt.Id, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.proposal.authority-revoked");
        if (attempt.Status != RepairAttemptStatus.Running || attempt.LeaseExpiresAt <= now)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.attempt-invalid");

        var existingProposal = await dbContext.ManagedRepairProposals.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == request.AttemptId,
            cancellationToken);
        if (existingProposal is not null)
        {
            var replay = ReplayProposal(existingProposal, request);
            await AuditAsync(
                attempt,
                "repair-proposal-created",
                "proposal-created",
                existingProposal.ProposalDigest,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(null, replay);
        }

        var existing = await dbContext.ManagedRepairInferenceReservations.SingleOrDefaultAsync(
            x => x.AttemptId == request.AttemptId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.IdempotencyKey != request.IdempotencyKey ||
                !ControlHealingWorkloadRequestAuthorizer.FixedEquals(
                    existing.SourceContextDigest,
                    sourceContext.Digest))
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.idempotency-conflict");
            if (existing.Status == ManagedRepairInferenceReservationStatus.Leased && existing.LeaseExpiresAt > now)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.inference-reservation-active");
            if (existing.Status == ManagedRepairInferenceReservationStatus.Leased)
            {
                // A provider call may have completed immediately before the process died. Without a provider-side
                // idempotency contract, reacquiring this lease could spend the budget twice, so recovery is fail-closed.
                await TerminateInferenceReservationAsync(
                    existing,
                    attempt,
                    ManagedRepairInferenceReservationStatus.Abandoned,
                    RepairAttemptStatus.Failed,
                    "managed-inference-outcome-indeterminate",
                    "repair-inference-abandoned",
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(HttpStatusCode.Gone, "healing.workload.inference-outcome-indeterminate");
            }

            throw Rejected(HttpStatusCode.Gone, existing.OutcomeCode ?? "healing.workload.inference-unavailable");
        }

        var leaseBytes = RandomNumberGenerator.GetBytes(32);
        string leaseToken;
        try
        {
            leaseToken = Base64UrlEncode(leaseBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(leaseBytes);
        }
        var budgetDeadline = now.Add(budget.TimeLimit);
        var leaseExpiresAt = attempt.LeaseExpiresAt!.Value < budgetDeadline
            ? attempt.LeaseExpiresAt.Value
            : budgetDeadline;
        var reservation = new ManagedRepairInferenceReservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = attempt.WorkspaceId,
            ApplicationId = attempt.ApplicationId,
            AttemptId = attempt.Id,
            IdempotencyKey = request.IdempotencyKey,
            SourceContextDigest = sourceContext.Digest,
            ReservedInferenceUnits = budget.InferenceUnitLimit,
            LeaseTokenHash = ControlHealingWorkloadRequestAuthorizer.Hash(leaseToken),
            LeaseExpiresAt = leaseExpiresAt,
            Status = ManagedRepairInferenceReservationStatus.Leased,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid().ToByteArray()
        };
        dbContext.ManagedRepairInferenceReservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(new(leaseToken, leaseExpiresAt), null);
    }

    private async ValueTask RejectInferenceReservationAsync(
        Guid attemptId,
        string leaseToken,
        string outcomeCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(x => x.Id == attemptId, cancellationToken);
        if (attempt is null)
            return;
        await HealingRepairAdmission.AcquireApplicationLockAsync(
            dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken);
        var reservation = await dbContext.ManagedRepairInferenceReservations.SingleOrDefaultAsync(
            x => x.AttemptId == attemptId &&
                 x.LeaseTokenHash == ControlHealingWorkloadRequestAuthorizer.Hash(leaseToken),
            cancellationToken);
        if (reservation?.Status == ManagedRepairInferenceReservationStatus.Leased)
            await TerminateInferenceReservationAsync(
                reservation,
                attempt,
                ManagedRepairInferenceReservationStatus.Rejected,
                RepairAttemptStatus.Failed,
                outcomeCode,
                "repair-inference-rejected",
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async ValueTask TerminateInferenceReservationAsync(
        ManagedRepairInferenceReservation reservation,
        RepairAttempt attempt,
        ManagedRepairInferenceReservationStatus reservationStatus,
        RepairAttemptStatus attemptStatus,
        string outcomeCode,
        string auditEventType,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        reservation.Status = reservationStatus;
        reservation.OutcomeCode = outcomeCode;
        reservation.UpdatedAt = now;
        reservation.CompletedAt = now;
        reservation.Version = Guid.NewGuid().ToByteArray();
        attempt.Status = attemptStatus;
        attempt.OutcomeCode = outcomeCode;
        attempt.SafeOutcomeDetail = outcomeCode;
        attempt.CompletedAt = now;
        attempt.LeaseOwner = null;
        attempt.LeaseToken = null;
        attempt.LeaseExpiresAt = null;
        attempt.Version = Guid.NewGuid().ToByteArray();
        var incident = await dbContext.HealingIncidents.SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.Id == attempt.IncidentId &&
                 x.ActiveEpisodeId == attempt.EpisodeId,
            cancellationToken);
        if (incident is not null)
        {
            if (incident.Status == HealingIncidentStatus.Repairing)
                incident.TryTransitionTo(HealingIncidentStatus.NeedsHuman);
            if (incident.Status == HealingIncidentStatus.NeedsHuman)
                incident.NeedsHumanReason = NeedsHumanReason.PolicyBlocked;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuditAsync(attempt, auditEventType, outcomeCode, null, cancellationToken);
    }

    public async ValueTask<WorkloadCapabilityGrant> ExchangeFinalizationAsync(
        WorkloadProposalFinalizationExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var authority = await (
            from proposal in dbContext.ManagedRepairProposals.AsNoTracking()
            join attempt in dbContext.RepairAttempts.AsNoTracking()
                on new { proposal.WorkspaceId, proposal.ApplicationId, Id = proposal.AttemptId }
                equals new { attempt.WorkspaceId, attempt.ApplicationId, attempt.Id }
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
            where proposal.Id == request.ProposalId && proposal.AttemptId == request.AttemptId &&
                  proposal.Status == ManagedRepairProposalStatus.Ready && proposal.ExpiresAt > now &&
                  attempt.Status == RepairAttemptStatus.ProposalReady &&
                  binding.Status == SourceOwnershipBindingStatus.Active
            select new { Proposal = proposal, Attempt = attempt, Binding = binding }).SingleOrDefaultAsync(cancellationToken);
        var liveAuthority = authority is null
            ? null
            : await authorityService.ResolveAsync(authority.Attempt.WorkspaceId, authority.Attempt.Id, cancellationToken);
        if (authority is null || liveAuthority is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.finalization-exchange.denied");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext,
                authority.Attempt.WorkspaceId,
                authority.Attempt.ApplicationId,
                cancellationToken))
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.finalization-exchange.denied");
        liveAuthority = await authorityService.ResolveAsync(
            authority.Attempt.WorkspaceId,
            authority.Attempt.Id,
            cancellationToken);
        if (liveAuthority is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.finalization-exchange.denied");
        var repository = $"{liveAuthority.Binding.RepositoryOwner}/{liveAuthority.Binding.RepositoryName}";
        var expectation = new GitHubWorkloadIdentityExpectation(
            authority.Attempt.WorkspaceId,
            authority.Attempt.ApplicationId,
            authority.Attempt.Id,
            authority.Proposal.FinalizationNonceHash,
            $"repo:{repository}:ref:{liveAuthority.Binding.WorkflowReference}",
            liveAuthority.Binding.RepositoryProviderId,
            liveAuthority.Binding.RepositoryOwner,
            liveAuthority.Binding.RepositoryName,
            $"{repository}/{liveAuthority.Binding.WorkflowIdentity}@{liveAuthority.Binding.WorkflowReference}",
            liveAuthority.Binding.WorkflowRevision,
            liveAuthority.Binding.WorkflowReference,
            liveAuthority.Binding.WorkflowRevision,
            "finalize",
            authority.Proposal.Id,
            FinalizationScopes);
        var validation = await identityValidator.ValidateAsync(
            request.IdentityAssertion,
            request.OneTimeNonce,
            expectation,
            cancellationToken);
        if (!validation.Succeeded || validation.Identity is null)
            throw Rejected(HttpStatusCode.Unauthorized, validation.ReasonCode);

        var lease = await orchestrationService.AcquireLeaseAsync(
            authority.Attempt.WorkspaceId,
            authority.Attempt.Id,
            $"github-finalize:{validation.Identity.RunId}:{validation.Identity.RunAttempt}",
            options.AttemptLeaseLifetime,
            cancellationToken);
        if (!lease.Succeeded || lease.ExpiresAt is null)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.attempt-lease-unavailable");

        liveAuthority = await authorityService.ResolveAsync(
            authority.Attempt.WorkspaceId,
            authority.Attempt.Id,
            cancellationToken);
        if (liveAuthority is null || liveAuthority.Attempt.Status != RepairAttemptStatus.Running)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.finalization-exchange.denied");

        var grant = await IssueCapabilityAsync(
            liveAuthority.Attempt,
            validation.Identity,
            FinalizationScopes,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return grant;
    }

    public async ValueTask<WorkloadHeartbeatReceipt> HeartbeatAsync(
        WorkloadHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (request.RequestedAt < now.AddMinutes(-5) || request.RequestedAt > now.AddMinutes(1))
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.heartbeat.timestamp-invalid");
        var attemptScope = await dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.Id == request.AttemptId)
            .Select(x => x.WorkspaceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (attemptScope == Guid.Empty ||
            await authorityService.ResolveAsync(attemptScope, request.AttemptId, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.heartbeat.authority-revoked");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var existing = await dbContext.WorkloadHeartbeats.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == request.AttemptId && x.IdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestedAt != request.RequestedAt)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.heartbeat.idempotency-conflict");
            await transaction.RollbackAsync(cancellationToken);
            return new WorkloadHeartbeatReceipt(
                HealingContractVersions.WorkloadProtocol,
                existing.AttemptId,
                existing.LeaseExpiresAt,
                true);
        }
        var attempt = await dbContext.RepairAttempts.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.AttemptId &&
                 x.Status == RepairAttemptStatus.Running &&
                 x.LeaseToken != null,
            cancellationToken);
        if (attempt is null)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.attempt-lease-lost");
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken) ||
            await authorityService.ResolveAsync(attempt.WorkspaceId, attempt.Id, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.heartbeat.authority-revoked");
        var expiresAt = now.Add(options.AttemptLeaseLifetime);
        var renewed = await orchestrationStore.TryHeartbeatLeaseAsync(
            attempt.WorkspaceId,
            attempt.Id,
            attempt.LeaseToken!,
            now,
            expiresAt,
            cancellationToken);
        if (!renewed)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.attempt-lease-lost");
        dbContext.WorkloadHeartbeats.Add(new WorkloadHeartbeat
        {
            Id = Guid.NewGuid(),
            WorkspaceId = attempt.WorkspaceId,
            ApplicationId = attempt.ApplicationId,
            AttemptId = attempt.Id,
            IdempotencyKey = request.IdempotencyKey,
            RequestedAt = request.RequestedAt,
            LeaseExpiresAt = expiresAt,
            AcceptedAt = now
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.WorkloadHeartbeats.AsNoTracking().SingleOrDefaultAsync(
                x => x.AttemptId == request.AttemptId && x.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
            if (winner is null)
                throw;
            if (winner.RequestedAt != request.RequestedAt)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.heartbeat.idempotency-conflict");
            return new WorkloadHeartbeatReceipt(
                HealingContractVersions.WorkloadProtocol,
                winner.AttemptId,
                winner.LeaseExpiresAt,
                true);
        }
        await transaction.CommitAsync(cancellationToken);
        return new WorkloadHeartbeatReceipt(
            HealingContractVersions.WorkloadProtocol,
            attempt.Id,
            expiresAt,
            false);
    }

    public async ValueTask<WorkloadResultUploadReceipt> UploadResultAsync(
        WorkloadResultUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var canonicalEnvelope = JsonSerializer.Serialize(request.Result);
        if (Encoding.UTF8.GetByteCount(canonicalEnvelope) > MaximumResultEnvelopeBytes ||
            !ControlHealingWorkloadRequestAuthorizer.FixedEquals(
                RepairAgentGateway.ComputeSha256Digest(request.Result.UnifiedDiff),
                request.Result.PatchDigest))
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.integrity-invalid");
        var requestedClassification = ParseClassification(request.Result);
        var envelopeDigest = RepairAgentGateway.ComputeSha256Digest(canonicalEnvelope);
        var now = timeProvider.GetUtcNow();
        var attemptScope = await dbContext.RepairAttempts.AsNoTracking()
            .Where(x => x.Id == request.AttemptId)
            .Select(x => x.WorkspaceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (attemptScope == Guid.Empty ||
            await authorityService.ResolveAsync(attemptScope, request.AttemptId, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.result.authority-revoked");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
            x => x.Id == request.AttemptId,
            cancellationToken);
        if (attempt is null)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.attempt-invalid");
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken) ||
            await authorityService.ResolveAsync(attempt.WorkspaceId, attempt.Id, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.result.authority-revoked");
        if (request.Result.ProposalId is not { } proposalId ||
            string.IsNullOrWhiteSpace(request.Result.ProposalDigest))
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.proposal-required");
        var existing = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == attempt.Id,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.IdempotencyKey != request.IdempotencyKey ||
                existing.ProposalId != proposalId ||
                !ControlHealingWorkloadRequestAuthorizer.FixedEquals(existing.ProposalDigest!, request.Result.ProposalDigest) ||
                !ControlHealingWorkloadRequestAuthorizer.FixedEquals(existing.EnvelopeDigest, envelopeDigest))
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.idempotency-conflict");
            await AuditAsync(
                attempt,
                "repair-result-accepted",
                "result-accepted",
                envelopeDigest,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WorkloadResultUploadReceipt(
                HealingContractVersions.WorkloadProtocol,
                attempt.Id,
                envelopeDigest,
                true,
                existing.SubmittedAt);
        }
        var proposal = await dbContext.ManagedRepairProposals.SingleOrDefaultAsync(
            x => x.Id == proposalId && x.AttemptId == attempt.Id,
            cancellationToken);
        if (proposal is null || proposal.Status != ManagedRepairProposalStatus.Ready || proposal.ExpiresAt <= now ||
            !ControlHealingWorkloadRequestAuthorizer.FixedEquals(proposal.ProposalDigest, request.Result.ProposalDigest))
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.proposal-invalid");
        StoredManagedProposal proposalPayload;
        try
        {
            proposalPayload = JsonSerializer.Deserialize<StoredManagedProposal>(proposal.ProposalJson)
                              ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.corrupt");
        }
        if (attempt.Status != RepairAttemptStatus.Running ||
            attempt.LeaseExpiresAt < now || !MatchesProposal(request.Result, proposalPayload))
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.attempt-invalid");
        ValidateUsage(request.Result.Usage, attempt, now);
        var exchange = await dbContext.WorkloadIdentityExchanges.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == attempt.WorkspaceId &&
                 x.ApplicationId == attempt.ApplicationId &&
                 x.AttemptId == attempt.Id &&
                 x.ProposalId == proposal.Id &&
                 x.Phase == "finalize" &&
                 x.Status == WorkloadIdentityExchangeStatus.Exchanged &&
                 x.ExpiresAt > now,
            cancellationToken);
        if (exchange is null ||
            exchange.WorkflowRunId != request.Result.WorkflowRunId ||
            exchange.WorkflowRunAttempt != request.Result.WorkflowRunAttempt)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.result.identity-mismatch");

        var classification = attempt.RepairClassification == RepairClassification.RevisionUnverified
            ? RepairClassification.RevisionUnverified
            : requestedClassification;
        var changedPathsJson = BoundedJson(request.Result.ChangedPaths);
        var reproductionJson = BoundedJson(request.Result.Reproduction);
        var regressionJson = BoundedJson(request.Result.Regression);
        var validationJson = BoundedJson(request.Result.Validation);
        var riskJson = BoundedJson(new
        {
            request.Result.CausalSummary,
            request.Result.RiskSuggestions,
            request.Result.RollbackSummary,
            request.Result.Usage,
            request.Result.Timing
        });
        var usageJson = BoundedJson(request.Result.Usage);

        var result = new RepairResult
        {
            Id = Guid.NewGuid(),
            WorkspaceId = attempt.WorkspaceId,
            ApplicationId = attempt.ApplicationId,
            AttemptId = attempt.Id,
            ProposalId = proposal.Id,
            ProposalDigest = proposal.ProposalDigest,
            IdempotencyKey = request.IdempotencyKey,
            WorkflowRunId = request.Result.WorkflowRunId,
            WorkflowRunAttempt = request.Result.WorkflowRunAttempt,
            BaseRevision = request.Result.BaseRevision,
            TargetRevision = request.Result.TargetRevision,
            Classification = classification,
            Confidence = request.Result.Confidence,
            UnifiedDiff = request.Result.UnifiedDiff,
            PatchDigest = request.Result.PatchDigest,
            EnvelopeDigest = envelopeDigest,
            ChangedPathsJson = changedPathsJson,
            ReproductionJson = reproductionJson,
            RegressionJson = regressionJson,
            ValidationJson = validationJson,
            RiskJson = riskJson,
            SubmittedAt = now
        };
        dbContext.RepairResults.Add(result);
        attempt.Status = RepairAttemptStatus.ResultReceived;
        attempt.RepairClassification = classification;
        SetUsage(attempt, request.Result.Usage, usageJson);
        attempt.CompletedAt = now;
        attempt.LeaseOwner = null;
        attempt.LeaseToken = null;
        attempt.LeaseExpiresAt = null;
        proposal.Status = ManagedRepairProposalStatus.Finalized;
        proposal.FinalizedAt = now;
        proposal.Version = Guid.NewGuid().ToByteArray();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                attempt,
                "repair-result-accepted",
                "result-accepted",
                envelopeDigest,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
                x => x.AttemptId == attempt.Id,
                cancellationToken);
            if (winner is null)
                throw;
            if (winner.IdempotencyKey != request.IdempotencyKey ||
                winner.ProposalId != proposalId ||
                !ControlHealingWorkloadRequestAuthorizer.FixedEquals(
                    winner.ProposalDigest!,
                    request.Result.ProposalDigest) ||
                !ControlHealingWorkloadRequestAuthorizer.FixedEquals(winner.EnvelopeDigest, envelopeDigest))
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.idempotency-conflict");
            return await ReplayResultWithAuditAsync(winner, request, envelopeDigest, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new WorkloadResultUploadReceipt(
            HealingContractVersions.WorkloadProtocol,
            attempt.Id,
            envelopeDigest,
            false,
            now);
    }

    private async ValueTask<WorkloadResultUploadReceipt> ReplayResultWithAuditAsync(
        RepairResult result,
        WorkloadResultUploadRequest request,
        string envelopeDigest,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleAsync(
            x => x.Id == request.AttemptId,
            cancellationToken);
        if (!await HealingRepairAdmission.AcquireApplicationLockAsync(
                dbContext, attempt.WorkspaceId, attempt.ApplicationId, cancellationToken) ||
            await authorityService.ResolveAsync(attempt.WorkspaceId, attempt.Id, cancellationToken) is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.result.authority-revoked");

        var current = await dbContext.RepairResults.AsNoTracking().SingleAsync(
            x => x.Id == result.Id && x.AttemptId == request.AttemptId,
            cancellationToken);
        if (current.IdempotencyKey != request.IdempotencyKey ||
            current.ProposalId != request.Result.ProposalId ||
            !ControlHealingWorkloadRequestAuthorizer.FixedEquals(
                current.ProposalDigest!,
                request.Result.ProposalDigest!) ||
            !ControlHealingWorkloadRequestAuthorizer.FixedEquals(current.EnvelopeDigest, envelopeDigest))
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.idempotency-conflict");

        await AuditAsync(
            attempt,
            "repair-result-accepted",
            "result-accepted",
            envelopeDigest,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WorkloadResultUploadReceipt(
            HealingContractVersions.WorkloadProtocol,
            attempt.Id,
            envelopeDigest,
            true,
            current.SubmittedAt);
    }

    internal static bool MatchesProposal(RepairResultEnvelope result, StoredManagedProposal proposal)
    {
        var classificationMatches = result.Classification == proposal.Classification ||
                                    (proposal.Classification == RepairAgentClassifications.InferredHighConfidence &&
                                     result.Classification == RepairAgentClassifications.Reproduced &&
                                     result.Reproduction.WasReproduced);
        return classificationMatches &&
               result.BaseRevision == proposal.BaseRevision &&
               result.TargetRevision == proposal.TargetRevision &&
               result.Confidence == proposal.Confidence &&
               result.CausalSummary == proposal.CausalSummary &&
               result.UnifiedDiff == proposal.UnifiedDiff &&
               result.PatchDigest == proposal.PatchDigest &&
               result.RollbackSummary == proposal.RollbackSummary &&
               result.ChangedPaths.SequenceEqual(proposal.ChangedPaths) &&
               result.RiskSuggestions.SequenceEqual(proposal.RiskSuggestions, StringComparer.Ordinal) &&
               result.Usage.InputUnits == proposal.Usage.InputUnits &&
               result.Usage.OutputUnits == proposal.Usage.OutputUnits &&
               result.Usage.AgentDuration == proposal.Usage.AgentDuration;
    }

    private async ValueTask<WorkloadCapabilityGrant> IssueCapabilityAsync(
        RepairAttempt attempt,
        VerifiedGitHubWorkloadIdentity identity,
        IReadOnlySet<string> scopes,
        CancellationToken cancellationToken)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        try
        {
            var capabilityToken = Base64UrlEncode(tokenBytes);
            var now = timeProvider.GetUtcNow();
            var expiresAt = now.Add(options.CapabilityLifetime);
            var updated = await dbContext.WorkloadIdentityExchanges
                .Where(x => x.WorkspaceId == attempt.WorkspaceId &&
                            x.ApplicationId == attempt.ApplicationId &&
                            x.AttemptId == attempt.Id &&
                            x.JwtId == identity.JwtId &&
                            x.Status == WorkloadIdentityExchangeStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.CapabilityTokenHash, ControlHealingWorkloadRequestAuthorizer.Hash(capabilityToken))
                    .SetProperty(x => x.ExchangedAt, now)
                    .SetProperty(x => x.ExpiresAt, expiresAt)
                    .SetProperty(x => x.Status, WorkloadIdentityExchangeStatus.Exchanged)
                    .SetProperty(x => x.Version, Guid.NewGuid().ToByteArray()), cancellationToken);
            if (updated != 1)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.exchange-state-conflict");

            return new WorkloadCapabilityGrant(
                HealingContractVersions.WorkloadProtocol,
                attempt.Id,
                capabilityToken,
                scopes,
                expiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    private WorkloadProposalCreateResponse ReplayProposal(
        ManagedRepairProposal proposal,
        WorkloadProposalCreateRequest request)
    {
        if (proposal.IdempotencyKey != request.IdempotencyKey ||
            !ControlHealingWorkloadRequestAuthorizer.FixedEquals(
                proposal.SourceContextDigest,
                request.SourceContext.Digest))
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.idempotency-conflict");
        if (proposal.Status != ManagedRepairProposalStatus.Ready || proposal.ExpiresAt <= timeProvider.GetUtcNow())
            throw Rejected(HttpStatusCode.Gone, "healing.workload.proposal.unavailable");

        StoredManagedProposal payload;
        string nonce;
        try
        {
            payload = JsonSerializer.Deserialize<StoredManagedProposal>(proposal.ProposalJson)
                      ?? throw new JsonException();
            nonce = _proposalNonceProtector.Unprotect(proposal.ProtectedFinalizationNonce);
        }
        catch (Exception exception) when (exception is JsonException or CryptographicException)
        {
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.corrupt");
        }
        return ProposalResponse(proposal, payload, nonce, true);
    }

    private static WorkloadProposalCreateResponse ProposalResponse(
        ManagedRepairProposal proposal,
        StoredManagedProposal payload,
        string nonce,
        bool isReplay) =>
        new(
            HealingContractVersions.WorkloadProtocol,
            proposal.AttemptId,
            new(
                HealingContractVersions.WorkloadProtocol,
                proposal.AttemptId,
                proposal.Id,
                proposal.ProposalDigest,
                proposal.SourceContextDigest,
                payload.BaseRevision,
                payload.TargetRevision,
                payload.Classification,
                payload.Confidence,
                payload.CausalSummary,
                payload.UnifiedDiff,
                payload.PatchDigest,
                payload.ChangedPaths,
                payload.RiskSuggestions,
                payload.RollbackSummary,
                payload.Usage,
                proposal.CreatedAt,
                proposal.ExpiresAt),
            nonce,
            isReplay);

    private async ValueTask AuditAsync(
        RepairAttempt attempt,
        string eventType,
        string reasonCode,
        string? outputHash,
        CancellationToken cancellationToken)
    {
        await auditService.AppendAsync(new(
            attempt.WorkspaceId,
            "repair-attempt",
            attempt.Id,
            eventType,
            reasonCode,
            "control",
            "healing-workload-api",
            attempt.IncidentId,
            attempt.EpisodeId,
            null,
            null,
            outputHash,
            new Dictionary<string, string?>
            {
                ["status"] = attempt.Status.ToString().ToLowerInvariant()
            }), cancellationToken);
    }

    private static string BaseRevision(RepairAttempt attempt) =>
        attempt.RepairClassification == RepairClassification.RevisionUnverified
            ? attempt.TargetRevision
            : attempt.ProducingRevision ?? attempt.TargetRevision;

    internal sealed record StoredManagedProposal(
        string BaseRevision,
        string TargetRevision,
        string Classification,
        decimal Confidence,
        string CausalSummary,
        string UnifiedDiff,
        string PatchDigest,
        IReadOnlyList<RepairChangedPathSuggestion> ChangedPaths,
        IReadOnlyList<string> RiskSuggestions,
        string RollbackSummary,
        RepairUsageSummary Usage);

    private static RepairClassification ParseClassification(RepairResultEnvelope result)
    {
        var classification = result.Classification switch
        {
            "reproduced" when result.Reproduction.WasAttempted && result.Reproduction.WasReproduced => RepairClassification.Reproduced,
            "inferred-high-confidence" when !result.Reproduction.WasReproduced && result.Confidence >= RepairOrchestrationService.HighConfidenceThreshold => RepairClassification.InferredHighConfidence,
            "revision-unverified" when !result.Reproduction.WasReproduced => RepairClassification.RevisionUnverified,
            "insufficient-confidence" => RepairClassification.InsufficientConfidence,
            _ => throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.classification-invalid")
        };
        if (classification == RepairClassification.InsufficientConfidence && result.UnifiedDiff.Length > 0)
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.patch-not-permitted");
        return classification;
    }

    private static string PrefixDigest(string digest) =>
        digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest : $"sha256:{digest}";

    private static string BoundedJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        if (Encoding.UTF8.GetByteCount(json) > 8_192)
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.projection-too-large");
        return json;
    }

    private static RepairAgentBudget ParseBudget(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var budget = new RepairAgentBudget(
                TimeSpan.FromSeconds(root.GetProperty("maxDurationSeconds").GetInt64()),
                root.GetProperty("maxTokens").GetInt64(),
                root.GetProperty("maxSteps").GetInt64());
            if (budget.TimeLimit <= TimeSpan.Zero ||
                budget.TimeLimit > RepairAgentGatewayLimits.MaximumTimeLimit ||
                budget.InferenceUnitLimit <= 0 ||
                budget.RepositoryRunLimit <= 0)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.budget.invalid");
            return budget;
        }
        catch (HealingWorkflowRequestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or OverflowException)
        {
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.budget.invalid");
        }
    }

    private static void ValidateUsage(RepairUsageSummary usage, RepairAttempt attempt, DateTimeOffset now)
    {
        var budget = ParseBudget(attempt.BudgetJson);
        var elapsed = attempt.StartedAt is null ? TimeSpan.MaxValue : now - attempt.StartedAt.Value;
        if (elapsed > budget.TimeLimit || usage.AgentDuration > budget.TimeLimit ||
            usage.RepositoryRunDuration > budget.TimeLimit ||
            usage.AgentDuration < TimeSpan.Zero || usage.RepositoryRunDuration < TimeSpan.Zero ||
            usage.RepositoryRuns < 0 || usage.RepositoryRuns > budget.RepositoryRunLimit ||
            usage.InputUnits < 0 || usage.OutputUnits < 0 ||
            usage.InputUnits > budget.InferenceUnitLimit - usage.OutputUnits)
            throw Rejected(HttpStatusCode.UnprocessableEntity, "healing.workload.budget.exceeded");
    }

    private static void SetUsage(RepairAttempt attempt, RepairUsageSummary usage, string? usageJson = null)
    {
        attempt.UsageJson = usageJson ?? BoundedJson(usage);
        attempt.InputUnits = usage.InputUnits;
        attempt.OutputUnits = usage.OutputUnits;
        attempt.AgentDurationTicks = usage.AgentDuration.Ticks;
        attempt.RepositoryRunDurationTicks = usage.RepositoryRunDuration.Ticks;
        attempt.RepositoryRuns = usage.RepositoryRuns;
    }

    private static bool IsTerminal(RepairAttemptStatus status) => status is
        RepairAttemptStatus.Succeeded or
        RepairAttemptStatus.Failed or
        RepairAttemptStatus.Stopped or
        RepairAttemptStatus.Expired;

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HealingWorkflowRequestException Rejected(HttpStatusCode statusCode, string code) => new(statusCode, code);
}
