using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Platform.Api.Workspace.Healing;
using Elsa.Platform.Healing.Abstractions;
using Elsa.Platform.Healing.Agent;
using Elsa.Platform.Healing.Core;
using Elsa.Platform.Healing.Core.Repairs;
using Elsa.Platform.Healing.Core.Security;
using Elsa.Platform.Healing.GitHub;
using Elsa.Platform.Healing.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.Api.Healing;

public sealed class HealingGitHubOptions
{
    public const string SectionName = "Healing:GitHub";
    public string WorkloadAudience { get; set; } = "elsa-platform-healing";
    public string? PlatformBaseUrl { get; set; }
    public TimeSpan CapabilityLifetime { get; set; } = TimeSpan.FromMinutes(35);
    public TimeSpan AttemptLeaseLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ProposalLifetime { get; set; } = TimeSpan.FromHours(2);
}

public sealed class PlatformHealingWorkloadRequestAuthorizer(
    HealingDbContext dbContext,
    TimeProvider timeProvider) : IHealingWorkloadRequestAuthorizer
{
    public async ValueTask<HealingWorkloadAuthorizationResult> AuthorizeExchangeAsync(
        Guid workspaceId,
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var authorized = await dbContext.RepairAttempts.AsNoTracking().AnyAsync(
            x => x.WorkspaceId == workspaceId &&
                 x.Id == attemptId &&
                 x.Status != RepairAttemptStatus.Succeeded &&
                 x.Status != RepairAttemptStatus.Failed &&
                 x.Status != RepairAttemptStatus.Stopped &&
                 x.Status != RepairAttemptStatus.Expired,
            cancellationToken);
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
        var authorized = matches.Length == 1 && ParseScopes(matches[0].ScopesJson).Contains(request.RequiredScope);
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

public sealed class PlatformHealingWorkloadApi(
    HealingDbContext dbContext,
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
        [WorkloadCapabilityScopes.ReadEvidence, WorkloadCapabilityScopes.CreateProposal], StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> FinalizationScopes = new HashSet<string>(
        [WorkloadCapabilityScopes.FinalizeProposal, WorkloadCapabilityScopes.UploadResult], StringComparer.Ordinal);
    private readonly IDataProtector _proposalNonceProtector = dataProtectionProvider.CreateProtector(
        "Elsa.Platform.Healing.ProposalFinalizationNonce.v1");

    public async ValueTask<WorkloadCapabilityGrant> ExchangeAsync(
        WorkloadIdentityExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var authority = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join binding in dbContext.SourceOwnershipBindings.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.BindingId }
                equals new { binding.WorkspaceId, binding.ApplicationId, binding.Id }
            where attempt.Id == request.AttemptId && binding.Status == SourceOwnershipBindingStatus.Active
            select new { Attempt = attempt, Binding = binding }).SingleOrDefaultAsync(cancellationToken);
        if (authority is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.exchange.denied");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
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
        var evidence = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join bundle in dbContext.EvidenceBundles.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EvidenceBundleId }
                equals new { bundle.WorkspaceId, bundle.ApplicationId, bundle.Id }
            where attempt.Id == request.AttemptId
            select new { attempt.Id, attempt.BudgetJson, Bundle = bundle }).SingleOrDefaultAsync(cancellationToken);
        if (evidence is null || evidence.Bundle.ExpiresAt <= timeProvider.GetUtcNow())
            throw Rejected(HttpStatusCode.Gone, "healing.workload.evidence.unavailable");

        return new WorkloadEvidenceResponse(
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
    }

    public async ValueTask<WorkloadProposalCreateResponse> CreateProposalAsync(
        WorkloadProposalCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var authority = await (
            from attempt in dbContext.RepairAttempts.AsNoTracking()
            join bundle in dbContext.EvidenceBundles.AsNoTracking()
                on new { attempt.WorkspaceId, attempt.ApplicationId, Id = attempt.EvidenceBundleId }
                equals new { bundle.WorkspaceId, bundle.ApplicationId, bundle.Id }
            where attempt.Id == request.AttemptId
            select new { Attempt = attempt, Evidence = bundle }).SingleOrDefaultAsync(cancellationToken);
        if (authority is null || authority.Attempt.Status != RepairAttemptStatus.Running ||
            authority.Attempt.LeaseExpiresAt <= now || authority.Evidence.ExpiresAt <= now)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.attempt-invalid");

        var existing = await dbContext.ManagedRepairProposals.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == request.AttemptId,
            cancellationToken);
        if (existing is not null)
            return ReplayProposal(existing, request);

        var sourceContext = new RepairSourceContextBundle(
            request.SourceContext.TargetRevision,
            request.SourceContext.Digest,
            request.SourceContext.Files.Select(x => new RepairSourceFile(x.Path, x.Content, x.Digest, x.IsTruncated)).ToArray(),
            request.SourceContext.OmittedPaths.ToArray());
        var budget = ParseBudget(authority.Attempt.BudgetJson);
        var proposalRequest = new RepairProposalRequest(
            HealingContractVersions.AgentProtocol,
            authority.Attempt.Id,
            BaseRevision(authority.Attempt),
            authority.Attempt.TargetRevision,
            authority.Attempt.ProducingRevision,
            new(
                authority.Evidence.Tier == EvidenceTier.Elevated ? "elevated" : "default-redacted",
                authority.Evidence.CanonicalJson,
                PrefixDigest(authority.Evidence.Digest),
                ParseStringArray(authority.Evidence.OmissionsJson)),
            sourceContext,
            budget);
        RepairProposal proposed;
        try
        {
            RepairProposalProtocol.ValidateRequest(proposalRequest);
            proposed = await proposalProvider.ProposeAsync(proposalRequest, cancellationToken);
            RepairProposalProtocol.ValidateProposal(proposed, budget);
        }
        catch (RepairAgentProtocolException exception)
        {
            throw Rejected(HttpStatusCode.UnprocessableEntity, exception.ReasonCode);
        }

        var proposalId = Guid.NewGuid();
        var createdAt = timeProvider.GetUtcNow();
        var expiresAt = createdAt.Add(options.ProposalLifetime);
        var payload = new StoredManagedProposal(
            BaseRevision(authority.Attempt),
            authority.Attempt.TargetRevision,
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
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
                x => x.Id == request.AttemptId,
                cancellationToken);
            if (attempt is null || attempt.Status != RepairAttemptStatus.Running || attempt.LeaseExpiresAt <= createdAt)
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.proposal.attempt-invalid");

            existing = await dbContext.ManagedRepairProposals.AsNoTracking().SingleOrDefaultAsync(
                x => x.AttemptId == request.AttemptId,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
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
                FinalizationNonceHash = PlatformHealingWorkloadRequestAuthorizer.Hash(finalizationNonce),
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
            attempt.UsageJson = BoundedJson(payload.Usage);
            attempt.Version = Guid.NewGuid().ToByteArray();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await AuditAsync(
                attempt,
                "repair-proposal-created",
                "proposal-created",
                proposalDigest,
                cancellationToken);
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
            return ReplayProposal(winner, request);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonceBytes);
        }
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
        if (authority is null)
            throw Rejected(HttpStatusCode.Forbidden, "healing.workload.finalization-exchange.denied");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var repository = $"{authority.Binding.RepositoryOwner}/{authority.Binding.RepositoryName}";
        var expectation = new GitHubWorkloadIdentityExpectation(
            authority.Attempt.WorkspaceId,
            authority.Attempt.ApplicationId,
            authority.Attempt.Id,
            authority.Proposal.FinalizationNonceHash,
            $"repo:{repository}:ref:{authority.Binding.WorkflowReference}",
            authority.Binding.RepositoryProviderId,
            authority.Binding.RepositoryOwner,
            authority.Binding.RepositoryName,
            $"{repository}/{authority.Binding.WorkflowIdentity}@{authority.Binding.WorkflowReference}",
            authority.Binding.WorkflowRevision,
            authority.Binding.WorkflowReference,
            authority.Binding.WorkflowRevision,
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

        var grant = await IssueCapabilityAsync(
            authority.Attempt,
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
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
            !PlatformHealingWorkloadRequestAuthorizer.FixedEquals(
                RepairAgentGateway.ComputeSha256Digest(request.Result.UnifiedDiff),
                request.Result.PatchDigest))
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.integrity-invalid");
        var requestedClassification = ParseClassification(request.Result);
        var envelopeDigest = RepairAgentGateway.ComputeSha256Digest(canonicalEnvelope);
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var attempt = await dbContext.RepairAttempts.SingleOrDefaultAsync(
            x => x.Id == request.AttemptId,
            cancellationToken);
        if (attempt is null)
            throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.attempt-invalid");
        if (request.Result.ProposalId is not { } proposalId ||
            string.IsNullOrWhiteSpace(request.Result.ProposalDigest))
            throw Rejected(HttpStatusCode.BadRequest, "healing.workload.result.proposal-required");
        var existing = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
            x => x.AttemptId == attempt.Id,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (existing.IdempotencyKey != request.IdempotencyKey ||
                existing.ProposalId != proposalId ||
                !PlatformHealingWorkloadRequestAuthorizer.FixedEquals(existing.ProposalDigest!, request.Result.ProposalDigest) ||
                !PlatformHealingWorkloadRequestAuthorizer.FixedEquals(existing.EnvelopeDigest, envelopeDigest))
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.idempotency-conflict");
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
            !PlatformHealingWorkloadRequestAuthorizer.FixedEquals(proposal.ProposalDigest, request.Result.ProposalDigest))
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
        attempt.UsageJson = usageJson;
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
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.RepairResults.AsNoTracking().SingleOrDefaultAsync(
                x => x.AttemptId == attempt.Id,
                cancellationToken);
            if (winner is null)
                throw;
            if (winner.IdempotencyKey != request.IdempotencyKey ||
                !PlatformHealingWorkloadRequestAuthorizer.FixedEquals(winner.EnvelopeDigest, envelopeDigest))
                throw Rejected(HttpStatusCode.Conflict, "healing.workload.result.idempotency-conflict");
            return new WorkloadResultUploadReceipt(
                HealingContractVersions.WorkloadProtocol,
                attempt.Id,
                envelopeDigest,
                true,
                winner.SubmittedAt);
        }
        await transaction.CommitAsync(cancellationToken);
        await AuditAsync(
            attempt,
            "repair-result-accepted",
            "result-accepted",
            envelopeDigest,
            cancellationToken);
        return new WorkloadResultUploadReceipt(
            HealingContractVersions.WorkloadProtocol,
            attempt.Id,
            envelopeDigest,
            false,
            now);
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
                    .SetProperty(x => x.CapabilityTokenHash, PlatformHealingWorkloadRequestAuthorizer.Hash(capabilityToken))
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
            !PlatformHealingWorkloadRequestAuthorizer.FixedEquals(
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
            "platform",
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

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HealingWorkflowRequestException Rejected(HttpStatusCode statusCode, string code) => new(statusCode, code);
}
