using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.RuntimeBuilder.Abstractions.Plans;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;

/// <summary>
/// Safe read projections for the managed-instance API. This adapter deliberately
/// does not return serialized plans, desired-state JSON, command payloads, or any
/// provider-owned identifiers.
/// </summary>
public sealed class EfCoreManagedElsaInstanceApiStore(CatalogDbContext dbContext) : IManagedElsaInstanceApiStore
{
    public async Task<ElsaInstancePage> ListInstancesAsync(
        Guid workspaceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            return new ElsaInstancePage([], 0);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (long)(page - 1) * pageSize;
        var query = dbContext.ElsaInstances
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.DeletedAt == null);
        var totalCount = await query.CountAsync(cancellationToken);
        if (offset >= totalCount)
            return new ElsaInstancePage([], totalCount);

        var entities = await query
            .Include(x => x.IdentityBinding)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = entities
            .Select(TryMapInstance)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
        return new ElsaInstancePage(items, totalCount);
    }

    public Task<bool> SlugExistsAsync(
        Guid workspaceId,
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || string.IsNullOrWhiteSpace(slug))
            return Task.FromResult(false);

        return dbContext.ElsaInstances
            .AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.DeletedAt == null && x.Slug == slug, cancellationToken);
    }

    public async Task<ElsaInstanceOperationSummary?> GetOperationAsync(
        Guid workspaceId,
        Guid instanceId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || instanceId == Guid.Empty || operationId == Guid.Empty)
            return null;

        var operation = await dbContext.ElsaInstanceOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
                                       x.InstanceId == instanceId &&
                                       x.Id == operationId, cancellationToken);
        return operation is null || operation.InstanceId is null ? null : new ElsaInstanceOperationSummary(
            operation.Id,
            operation.InstanceId.Value,
            operation.Action,
            operation.State,
            operation.ExpectedVersion,
            operation.AttemptNumber,
            operation.AcceptedAt,
            operation.StartedAt,
            operation.CompletedAt,
            operation.DesiredStateRevisionId,
            operation.ResolvedPlanId,
            operation.DeploymentRunId,
            operation.FailureCode,
            operation.ReconciledObservedLifecycle,
            operation.ReconciledHealth);
    }

    public async Task<IReadOnlyList<ElsaInstanceIntentRevisionSummary>> ListRevisionsAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || instanceId == Guid.Empty)
            return [];

        var revisions = await dbContext.ElsaInstanceIntentRevisions
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.InstanceId == instanceId)
            .OrderByDescending(x => x.RevisionNumber)
            .ToListAsync(cancellationToken);
        return revisions.Select(x => new ElsaInstanceIntentRevisionSummary(
            x.Id,
            x.InstanceId,
            x.RevisionNumber,
            x.ContentHash,
            x.DistributionId,
            x.ReleaseLine,
            x.RequestedVersion,
            x.Channel,
            x.TopologyId,
            x.FeaturePresetId,
            x.PackagePolicy,
            x.ConfigurationShapeRevisionId,
            x.TargetMode,
            x.RegionCode,
            x.IsolationProfile,
            x.CapacityProfile,
            x.NetworkOutcome,
            x.DomainOutcome,
            x.DesiredLifecycle,
            x.AuthoredAt,
            x.CreatedByAccountId)).ToList();
    }

    public async Task<ElsaInstanceResolvedPlanSummary?> GetResolvedPlanAsync(
        Guid workspaceId,
        Guid instanceId,
        string planId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || instanceId == Guid.Empty || string.IsNullOrWhiteSpace(planId))
            return null;

        var entity = await dbContext.ElsaInstanceResolvedPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
                                       x.InstanceId == instanceId &&
                                       x.PlanId == planId, cancellationToken);
        if (entity is null)
            return null;

        try
        {
            var plan = ResolvedElsaApplicationPlanSerialization.Deserialize(entity.SerializedPlan).Normalize();
            if (ResolvedElsaApplicationPlanValidator.Validate(plan).Count != 0 ||
                !string.Equals(ResolvedElsaApplicationPlanSerialization.ComputeContentHash(plan), entity.ContentHash, StringComparison.Ordinal))
                return null;

            var reference = new ElsaResolvedPlanReference(entity.PlanId, entity.SchemaVersion, entity.ContentHash, entity.PlanUri);
            var release = new ElsaCurrentResolvedRelease(
                reference,
                plan.Release.DistributionId,
                plan.Release.ReleaseLine,
                plan.Release.Version,
                plan.Release.ReleaseManifestDigest,
                plan.Topology.Components.Select(x => new ElsaComponentDigest(x.Id, x.Image.Digest)));
            return new ElsaInstanceResolvedPlanSummary(
                reference,
                release,
                plan.Topology.Id,
                plan.Topology.Components.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                plan.Evidence.Select(x => new ElsaInstancePlanEvidenceSummary(x.Kind, x.Reference, x.Digest, x.Description)).ToList());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // A malformed immutable row is not a reason to expose its raw contents.
            return null;
        }
    }

    public async Task<IReadOnlyList<ElsaInstanceDeploymentSummary>> ListDeploymentsAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || instanceId == Guid.Empty)
            return [];

        var runs = await dbContext.DeploymentRuns
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ElsaInstanceId == instanceId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        var runIds = runs.Select(x => (Guid?)x.Id).ToList();
        var failureCodes = await dbContext.ElsaInstanceOperations
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.InstanceId == instanceId &&
                        x.DeploymentRunId != null && runIds.Contains(x.DeploymentRunId))
            .GroupBy(x => x.DeploymentRunId!.Value)
            .Select(x => new { DeploymentRunId = x.Key, FailureCode = x
                .OrderByDescending(operation => operation.AcceptedAt)
                .Select(operation => operation.FailureCode)
                .FirstOrDefault() })
            .ToDictionaryAsync(x => x.DeploymentRunId, x => x.FailureCode, cancellationToken);

        return runs.Select(x => new ElsaInstanceDeploymentSummary(
            x.Id,
            x.SourceRevisionId,
            x.Status,
            x.ValidationOutcome,
            x.QueuedAt,
            x.StartedAt,
            x.CompletedAt,
            x.AttemptNumber,
            // RecoveryReason is a free-form provider/deployment message in the
            // legacy run table. Do not copy it into the customer projection.
            null,
            failureCodes.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<IReadOnlyList<ElsaInstanceAuditEventSummary>> ListAuditAsync(
        Guid workspaceId,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty || instanceId == Guid.Empty)
            return [];

        var events = await dbContext.ElsaInstanceAuditEvents
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.InstanceId == instanceId)
            .OrderByDescending(x => x.Sequence)
            .ThenByDescending(x => x.OccurredAt)
            .ToListAsync(cancellationToken);
        return events.Select(x => new ElsaInstanceAuditEventSummary(
            x.Id,
            x.Sequence,
            x.EventType,
            x.ActorAccountId,
            x.OperatorSubject,
            x.OperationId,
            x.MigrationId,
            x.DeploymentRunId,
            x.PriorState,
            x.NewState,
            x.DesiredStateRevisionId,
            x.PlanReference,
            x.DiagnosticCode,
            x.Summary,
            x.RequestKeyHash,
            x.OccurredAt)).ToList();
    }

    private static ElsaInstance? TryMapInstance(Models.ElsaInstanceEntity entity)
    {
        try
        {
            return EfCoreElsaInstanceLifecycleStore.MapInstance(entity);
        }
        catch (InvalidOperationException)
        {
            // Corrupt or stale rows are not customer-visible diagnostics. Keep
            // them out of the projection and let the normal inaccessible/not-found
            // behavior apply to direct lookups.
            return null;
        }
    }
}
