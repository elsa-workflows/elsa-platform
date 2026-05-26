using System.Text.Json;
using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class DeploymentWorkspaceStore(CatalogDbContext dbContext) : IWorkspaceDeploymentStore, IWorkspacePermissionStore, IWorkspaceDeploymentMutationStore
{
    public async Task<DeploymentCockpit> GetCockpitAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspaceName = await dbContext.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == workspaceId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? $"Workspace {workspaceId:N}";

        var applications = await dbContext.DeploymentApplications
            .AsNoTracking()
            .Include(x => x.Environments)
                .ThenInclude(x => x.Revisions)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var engines = await dbContext.WorkflowEngines
            .AsNoTracking()
            .Include(x => x.Capabilities)
            .Include(x => x.Controls)
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var observabilityBindings = await dbContext.ObservabilityBindings
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Provider)
            .ThenBy(x => x.Kind)
            .ToListAsync(cancellationToken);

        var driftReport = await dbContext.DriftReportItems
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(cancellationToken);

        var deploymentRuns = await dbContext.DeploymentRuns
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);

        var runRevisionIds = deploymentRuns
            .SelectMany(x => new[] { x.SourceRevisionId, x.PreviousDeployedRevisionId })
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var runRevisions = runRevisionIds.Count == 0
            ? new Dictionary<Guid, DesiredStateRevisionEntity>()
            : await dbContext.DesiredStateRevisions
                .AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && runRevisionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var cockpitApplications = applications
            .Select(application => new WorkflowApplication(
                application.Id.ToString("D"),
                application.Name,
                workspaceName,
                application.Environments
                    .OrderBy(x => x.Tier)
                    .ThenBy(x => x.Name)
                    .Select(environment => ToEnvironmentSummary(environment, engines))
                    .ToList()))
            .ToList();

        return new DeploymentCockpit(
            cockpitApplications,
            engines.Select(ToEngineRegistration).ToList(),
            [],
            observabilityBindings.Select(ToObservabilityBinding).ToList(),
            deploymentRuns.Select(run => ToDeploymentHistoryEvent(run, runRevisions)).ToList(),
            driftReport.Select(ToDriftReportItem).ToList(),
            []);
    }

    public async Task<IReadOnlyList<WorkspacePermissionGrant>> GetPermissionGrantsAsync(
        Guid workspaceId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.WorkspacePermissionGrants
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.AccountId == accountId)
            .OrderBy(x => x.Permission)
            .Select(x => new WorkspacePermissionGrant(
                x.Id,
                x.WorkspaceId,
                x.AccountId,
                x.Permission,
                x.GrantedByAccountId,
                x.CreatedAt,
                x.UpdatedAt,
                x.RevokedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspacePermissionGrant> GrantPermissionAsync(
        Guid workspaceId,
        GrantWorkspacePermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.WorkspacePermissionGrants
            .SingleOrDefaultAsync(
                x => x.WorkspaceId == workspaceId
                    && x.AccountId == request.AccountId
                    && x.Permission == request.Permission
                    && x.RevokedAt == null,
                cancellationToken);

        if (existing is not null)
            return ToPermissionGrant(existing);

        var entity = new WorkspacePermissionGrantEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            AccountId = request.AccountId,
            Permission = request.Permission,
            GrantedByAccountId = request.GrantedByAccountId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await dbContext.WorkspacePermissionGrants.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToPermissionGrant(entity);
    }

    public async Task<ActionConfirmation> CreateConfirmationAsync(
        Guid workspaceId,
        CreateActionConfirmationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var entity = new ActionConfirmationEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ActionType = request.ActionType,
            TargetId = request.TargetId,
            ConfirmedByAccountId = request.ConfirmedByAccountId,
            ConfirmedAt = now,
            ExpiresAt = now.Add(request.Lifetime ?? TimeSpan.FromMinutes(5))
        };

        await dbContext.ActionConfirmations.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToActionConfirmation(entity);
    }

    public async Task<ActionConfirmation?> GetConfirmationAsync(
        Guid workspaceId,
        Guid confirmationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ActionConfirmations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == confirmationId, cancellationToken);
        return entity is null ? null : ToActionConfirmation(entity);
    }

    public async Task<ActionConfirmation> MarkConfirmationUsedAsync(
        Guid workspaceId,
        Guid confirmationId,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ActionConfirmations
            .SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == confirmationId, cancellationToken);
        if (entity.UsedAt is null)
            entity.UsedAt = usedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToActionConfirmation(entity);
    }

    public Task<bool> HasActiveRunAsync(
        Guid workspaceId,
        Guid environmentId,
        CancellationToken cancellationToken = default) =>
        dbContext.DeploymentRuns.AnyAsync(
            x => x.WorkspaceId == workspaceId
                && x.EnvironmentId == environmentId
                && (x.Status == WorkspaceDeploymentRunStatus.Queued || x.Status == WorkspaceDeploymentRunStatus.Running),
            cancellationToken);

    public async Task<WorkspaceDeploymentRun> CreateRunAsync(
        Guid workspaceId,
        QueueWorkspaceDeploymentRunRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var sourceRevision = await dbContext.DesiredStateRevisions
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == request.SourceRevisionId, cancellationToken);
        if (sourceRevision is null)
            throw new InvalidOperationException("Source revision does not exist in the workspace.");

        var environment = await dbContext.DeploymentEnvironments
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == request.TargetEnvironmentId, cancellationToken);
        if (environment is null)
            throw new InvalidOperationException("Target environment does not exist in the workspace.");

        var engineExists = await dbContext.WorkflowEngines
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == request.TargetEngineId && x.EnvironmentId == request.TargetEnvironmentId, cancellationToken);
        if (!engineExists)
            throw new InvalidOperationException("Target engine does not exist in the target environment.");

        var runId = Guid.NewGuid();
        var run = new DeploymentRunEntity
        {
            Id = runId,
            WorkspaceId = workspaceId,
            ApplicationId = sourceRevision.ApplicationId,
            EnvironmentId = request.TargetEnvironmentId,
            EngineId = request.TargetEngineId,
            SourceRevisionId = request.SourceRevisionId,
            PreviousDeployedRevisionId = environment.DeployedRevisionId,
            RollbackSourceRunId = request.RollbackSourceRunId,
            Status = WorkspaceDeploymentRunStatus.Queued,
            ValidationOutcome = DeploymentValidationOutcome.Passed,
            ConfirmationId = request.ConfirmationId,
            ActorAccountId = request.ActorAccountId,
            QueuedAt = now,
            CreatedAt = now,
            AttemptNumber = 1,
            History =
            [
                new DeploymentRunHistoryEventEntity
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    RunId = runId,
                    Status = WorkspaceDeploymentRunStatus.Queued,
                    Message = request.RollbackSourceRunId is null ? "Deployment run queued." : "Rollback run queued.",
                    CreatedAt = now
                }
            ]
        };

        environment.DeploymentStatus = DeploymentStatus.Running;
        environment.UpdatedAt = now;
        await dbContext.DeploymentRuns.AddAsync(run, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceDeploymentRun(run);
    }

    public async Task<WorkspaceDeploymentRun?> GetRunAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.DeploymentRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == runId, cancellationToken);
        return entity is null ? null : ToWorkspaceDeploymentRun(entity);
    }

    public async Task<IReadOnlyList<DeploymentRunHistoryEvent>> GetRunHistoryAsync(
        Guid workspaceId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DeploymentRunHistoryEvents
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.RunId == runId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => ToDeploymentRunHistoryEvent(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspaceDeploymentRun?> ClaimNextQueuedRunAsync(
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var run = await dbContext.DeploymentRuns
            .Where(x => x.Status == WorkspaceDeploymentRunStatus.Queued)
            .OrderBy(x => x.QueuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;

        run.Status = WorkspaceDeploymentRunStatus.Running;
        run.StartedAt = now;
        run.WorkerId = workerId;
        run.WorkerHeartbeatAt = now;
        await dbContext.DeploymentRunHistoryEvents.AddAsync(new DeploymentRunHistoryEventEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = run.WorkspaceId,
            RunId = run.Id,
            Status = WorkspaceDeploymentRunStatus.Running,
            Message = "Deployment run claimed by worker.",
            CreatedAt = now
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceDeploymentRun(run);
    }

    public async Task<WorkspaceDeploymentRun> UpdateRunStatusAsync(
        Guid workspaceId,
        Guid runId,
        WorkspaceDeploymentRunStatus status,
        string message,
        DateTimeOffset now,
        string? failureMessage = null,
        CancellationToken cancellationToken = default)
    {
        var run = await dbContext.DeploymentRuns
            .Include(x => x.Environment)
            .SingleAsync(x => x.WorkspaceId == workspaceId && x.Id == runId, cancellationToken);
        run.Status = status;
        run.FailureMessage = failureMessage;
        if (status is WorkspaceDeploymentRunStatus.Succeeded or WorkspaceDeploymentRunStatus.Failed or WorkspaceDeploymentRunStatus.Blocked or WorkspaceDeploymentRunStatus.Cancelled or WorkspaceDeploymentRunStatus.RolledBack or WorkspaceDeploymentRunStatus.RecoveryRequired)
            run.CompletedAt = now;

        if (run.Environment is not null)
        {
            run.Environment.UpdatedAt = now;
            run.Environment.DeploymentStatus = status is WorkspaceDeploymentRunStatus.Succeeded or WorkspaceDeploymentRunStatus.RolledBack
                ? DeploymentStatus.Succeeded
                : status == WorkspaceDeploymentRunStatus.Running
                    ? DeploymentStatus.Running
                    : DeploymentStatus.Blocked;
            if (status is WorkspaceDeploymentRunStatus.Succeeded or WorkspaceDeploymentRunStatus.RolledBack)
                run.Environment.DeployedRevisionId = run.SourceRevisionId;
        }

        await dbContext.DeploymentRunHistoryEvents.AddAsync(new DeploymentRunHistoryEventEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            RunId = run.Id,
            Status = status,
            Message = message,
            CreatedAt = now
        }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceDeploymentRun(run);
    }

    public async Task<int> MarkStaleRunningRunsRecoveryRequiredAsync(
        DateTimeOffset now,
        TimeSpan staleAfter,
        CancellationToken cancellationToken = default)
    {
        var staleBefore = now.Subtract(staleAfter);
        var runs = await dbContext.DeploymentRuns
            .Where(x => x.Status == WorkspaceDeploymentRunStatus.Running
                && (x.WorkerHeartbeatAt ?? x.StartedAt ?? x.QueuedAt) < staleBefore)
            .ToListAsync(cancellationToken);

        foreach (var run in runs)
        {
            run.Status = WorkspaceDeploymentRunStatus.RecoveryRequired;
            run.CompletedAt = now;
            run.RecoveryReason = "Worker heartbeat became stale.";
            await dbContext.DeploymentRunHistoryEvents.AddAsync(new DeploymentRunHistoryEventEntity
            {
                Id = Guid.NewGuid(),
                WorkspaceId = run.WorkspaceId,
                RunId = run.Id,
                Status = WorkspaceDeploymentRunStatus.RecoveryRequired,
                Message = "Deployment run requires recovery after stale worker heartbeat.",
                CreatedAt = now
            }, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return runs.Count;
    }

    public async Task<RuntimeControlExecution> RecordRuntimeControlExecutionAsync(
        Guid workspaceId,
        RuntimeControlExecution execution,
        CancellationToken cancellationToken = default)
    {
        if (execution.WorkspaceId != workspaceId)
            throw new InvalidOperationException("Runtime control execution workspace does not match the request workspace.");

        var entity = new RuntimeControlExecutionEntity
        {
            Id = execution.Id,
            WorkspaceId = execution.WorkspaceId,
            EngineId = execution.EngineId,
            EnvironmentId = execution.EnvironmentId,
            ControlId = execution.ControlId,
            ControlLabel = execution.ControlLabel,
            Boundary = execution.Boundary,
            RequiredCapabilityId = execution.RequiredCapabilityId,
            ConfirmationId = execution.ConfirmationId,
            ActorAccountId = execution.ActorAccountId,
            Status = execution.Status,
            CreatedAt = execution.CreatedAt,
            Message = execution.Message
        };

        await dbContext.RuntimeControlExecutions.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToRuntimeControlExecution(entity);
    }

    public async Task<WorkspaceDeploymentApplication> CreateApplicationAsync(
        Guid workspaceId,
        CreateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new DeploymentApplicationEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = request.Name,
            Description = request.Description,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByAccountId = request.ActorAccountId,
            UpdatedByAccountId = request.ActorAccountId
        };

        await dbContext.DeploymentApplications.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new WorkspaceDeploymentApplication(entity.Id, entity.WorkspaceId, entity.Name, entity.Description, entity.CreatedAt, entity.UpdatedAt, entity.CreatedByAccountId, entity.UpdatedByAccountId);
    }

    public async Task<WorkspaceDeploymentApplication> UpdateApplicationAsync(
        Guid workspaceId,
        Guid applicationId,
        UpdateWorkflowApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.DeploymentApplications
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == applicationId, cancellationToken);
        if (entity is null)
            throw new KeyNotFoundException("Deployment application does not exist in the workspace.");

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedByAccountId = request.ActorAccountId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new WorkspaceDeploymentApplication(entity.Id, entity.WorkspaceId, entity.Name, entity.Description, entity.CreatedAt, entity.UpdatedAt, entity.CreatedByAccountId, entity.UpdatedByAccountId);
    }

    public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        return CreateEnvironmentCoreAsync(workspaceId, request, cancellationToken);
    }

    public async Task<WorkspaceDeploymentEnvironment> UpdateEnvironmentAsync(
        Guid workspaceId,
        Guid environmentId,
        UpdateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.DeploymentEnvironments
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == environmentId && x.ApplicationId == request.ApplicationId, cancellationToken);
        if (entity is null)
            throw new KeyNotFoundException("Deployment environment does not exist in the workspace.");

        entity.Name = request.Name;
        entity.Tier = request.Tier;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new WorkspaceDeploymentEnvironment(entity.Id, entity.WorkspaceId, entity.ApplicationId, entity.Name, entity.Tier, entity.DesiredRevisionId, entity.DeployedRevisionId, entity.DeploymentStatus, entity.DriftStatus, entity.CreatedAt, entity.UpdatedAt);
    }

    public async Task<WorkspaceWorkflowEngine> RegisterEngineAsync(
        Guid workspaceId,
        RegisterWorkflowEngineRequest request,
        CancellationToken cancellationToken = default)
    {
        var environmentExists = await dbContext.DeploymentEnvironments
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == request.EnvironmentId, cancellationToken);
        if (!environmentExists)
            throw new InvalidOperationException("Deployment environment does not exist in the workspace.");

        var now = DateTimeOffset.UtcNow;
        var engine = new WorkflowEngineEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            EnvironmentId = request.EnvironmentId,
            Name = request.Name,
            BaseUrl = request.BaseUrl,
            Region = request.Region,
            Version = "",
            CertificateStatus = CertificateStatus.Trusted,
            CredentialProvider = request.CredentialProvider,
            CredentialReference = request.CredentialReference,
            CredentialVerificationStatus = CredentialVerificationStatus.Unverified,
            Health = DeploymentHealth.Unreachable,
            HostingProvider = request.HostingProvider,
            CreatedAt = now,
            UpdatedAt = now,
            Capabilities = request.Capabilities.Select(capability => new EngineCapabilityEntity
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                CapabilityId = capability.Id,
                Label = capability.Label,
                Boundary = capability.Boundary
            }).ToList(),
            Controls = request.Controls.Select(control => new RuntimeControlEntity
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                ControlId = control.Id,
                Label = control.Label,
                Boundary = control.Boundary,
                RequiredCapabilityId = control.CapabilityId,
                Description = control.Description
            }).ToList()
        };

        await dbContext.WorkflowEngines.AddAsync(engine, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceWorkflowEngine(engine);
    }

    public async Task<WorkspaceWorkflowEngine> UpdateEngineAsync(
        Guid workspaceId,
        Guid engineId,
        UpdateWorkflowEngineRequest request,
        CancellationToken cancellationToken = default)
    {
        var engine = await dbContext.WorkflowEngines
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == engineId, cancellationToken);
        if (engine is null)
            throw new KeyNotFoundException("Workflow engine does not exist in the workspace.");

        engine.Name = request.Name;
        engine.BaseUrl = request.BaseUrl;
        engine.Region = request.Region;
        engine.CredentialProvider = request.CredentialProvider;
        engine.CredentialReference = request.CredentialReference;
        engine.HostingProvider = request.HostingProvider;
        engine.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.EngineCapabilities
            .Where(x => x.WorkspaceId == workspaceId && x.EngineId == engine.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RuntimeControls
            .Where(x => x.WorkspaceId == workspaceId && x.EngineId == engine.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.EngineCapabilities.AddRangeAsync(request.Capabilities.Select(capability => new EngineCapabilityEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            EngineId = engine.Id,
            CapabilityId = capability.Id,
            Label = capability.Label,
            Boundary = capability.Boundary
        }), cancellationToken);
        await dbContext.RuntimeControls.AddRangeAsync(request.Controls.Select(control => new RuntimeControlEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            EngineId = engine.Id,
            ControlId = control.Id,
            Label = control.Label,
            Boundary = control.Boundary,
            RequiredCapabilityId = control.CapabilityId,
            Description = control.Description
        }), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceWorkflowEngine(engine);
    }

    public async Task<WorkspaceDesiredStateRevision> CreateRevisionAsync(
        Guid workspaceId,
        CreateDesiredStateRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        var environment = await dbContext.DeploymentEnvironments
            .SingleOrDefaultAsync(x =>
                x.WorkspaceId == workspaceId
                && x.Id == request.EnvironmentId
                && x.ApplicationId == request.ApplicationId,
                cancellationToken);
        if (environment is null)
            throw new InvalidOperationException("Deployment environment does not exist in the workspace.");

        var nextRevision = await dbContext.DesiredStateRevisions
            .Where(x => x.WorkspaceId == workspaceId && x.EnvironmentId == request.EnvironmentId)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(cancellationToken) + 1 ?? 1;
        var now = DateTimeOffset.UtcNow;
        var revisionId = Guid.NewGuid();
        var entity = new DesiredStateRevisionEntity
        {
            Id = revisionId,
            WorkspaceId = workspaceId,
            ApplicationId = request.ApplicationId,
            EnvironmentId = request.EnvironmentId,
            RevisionNumber = nextRevision,
            Label = request.Label,
            Commit = request.Commit,
            ContentHash = WorkspaceDeploymentService.ComputeDesiredStateHash(request.DesiredStateJson),
            DesiredStateJson = request.DesiredStateJson,
            AuthoredAt = now,
            CreatedAt = now,
            CreatedByAccountId = request.ActorAccountId,
            Records = ParseStructuredRecords(workspaceId, revisionId, request.DesiredStateJson)
        };

        await dbContext.DesiredStateRevisions.AddAsync(entity, cancellationToken);
        environment.DesiredRevisionId = entity.Id;
        environment.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceDesiredStateRevision(entity);
    }

    public async Task<WorkspaceDesiredStateRevision?> GetRevisionAsync(
        Guid workspaceId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.DesiredStateRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == revisionId, cancellationToken);
        return entity is null ? null : ToWorkspaceDesiredStateRevision(entity);
    }

    public async Task<WorkspaceDesiredStateRevision?> GetLatestRevisionAsync(
        Guid workspaceId,
        Guid environmentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.DesiredStateRevisions
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.EnvironmentId == environmentId)
            .OrderByDescending(x => x.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ToWorkspaceDesiredStateRevision(entity);
    }

    public async Task<WorkspaceWorkflowEngine?> GetEngineAsync(
        Guid workspaceId,
        Guid engineId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.WorkflowEngines
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == engineId, cancellationToken);
        return entity is null ? null : ToWorkspaceWorkflowEngine(entity);
    }

    private async Task<WorkspaceDeploymentEnvironment> CreateEnvironmentCoreAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken)
    {
        var applicationExists = await dbContext.DeploymentApplications
            .AnyAsync(x => x.WorkspaceId == workspaceId && x.Id == request.ApplicationId, cancellationToken);
        if (!applicationExists)
            throw new InvalidOperationException("Deployment application does not exist in the workspace.");

        var now = DateTimeOffset.UtcNow;
        var entity = new DeploymentEnvironmentEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ApplicationId = request.ApplicationId,
            Name = request.Name,
            Tier = request.Tier,
            DeploymentStatus = DeploymentStatus.Blocked,
            DriftStatus = DriftStatus.Unknown,
            CreatedAt = now,
            UpdatedAt = now
        };

        await dbContext.DeploymentEnvironments.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new WorkspaceDeploymentEnvironment(entity.Id, entity.WorkspaceId, entity.ApplicationId, entity.Name, entity.Tier, entity.DesiredRevisionId, entity.DeployedRevisionId, entity.DeploymentStatus, entity.DriftStatus, entity.CreatedAt, entity.UpdatedAt);
    }

    private static EnvironmentSummary ToEnvironmentSummary(DeploymentEnvironmentEntity environment, IReadOnlyList<WorkflowEngineEntity> engines)
    {
        var desiredRevision = environment.Revisions
            .SingleOrDefault(x => x.Id == environment.DesiredRevisionId)
            ?? environment.Revisions.OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
        var deployedRevision = environment.Revisions.SingleOrDefault(x => x.Id == environment.DeployedRevisionId);

        return new EnvironmentSummary(
            environment.Id.ToString("D"),
            environment.Name,
            environment.Tier,
            EnvironmentHealth(environment, engines),
            desiredRevision is null
                ? new DesiredStateRevision("", 0, "", "No desired revision", environment.CreatedAt)
                : new DesiredStateRevision(desiredRevision.Id.ToString("D"), desiredRevision.RevisionNumber, desiredRevision.Commit ?? "", desiredRevision.Label, desiredRevision.AuthoredAt),
            deployedRevision?.RevisionNumber,
            environment.DeploymentStatus,
            environment.DriftStatus,
            engines.Where(x => x.EnvironmentId == environment.Id).Select(x => x.Id.ToString("D")).ToList());
    }

    private static DeploymentHistoryEvent ToDeploymentHistoryEvent(
        DeploymentRunEntity run,
        IReadOnlyDictionary<Guid, DesiredStateRevisionEntity> revisions)
    {
        revisions.TryGetValue(run.SourceRevisionId, out var sourceRevision);
        DesiredStateRevisionEntity? rollbackSourceRevision = null;
        if (run.PreviousDeployedRevisionId.HasValue)
            revisions.TryGetValue(run.PreviousDeployedRevisionId.Value, out rollbackSourceRevision);

        return new DeploymentHistoryEvent(
            run.Id.ToString("D"),
            run.Status.ToString(),
            sourceRevision?.RevisionNumber ?? 0,
            run.ActorAccountId.ToString("N")[..8],
            run.EnvironmentId.ToString("D"),
            run.EngineId.ToString("D"),
            run.ValidationOutcome,
            run.CompletedAt ?? run.StartedAt ?? run.QueuedAt,
            rollbackSourceRevision?.RevisionNumber);
    }

    private static WorkflowEngineRegistration ToEngineRegistration(WorkflowEngineEntity engine) =>
        new(
            engine.Id.ToString("D"),
            engine.Name,
            engine.EnvironmentId.ToString("D"),
            new EngineEndpointMetadata(engine.BaseUrl, engine.Region ?? "", engine.Version ?? "", engine.CertificateStatus),
            new EngineCredentialReference(engine.CredentialProvider, engine.CredentialReference, engine.CredentialVerificationStatus, engine.CredentialLastVerifiedAt),
            engine.Health,
            engine.LastHeartbeatAt,
            engine.Capabilities.OrderBy(x => x.CapabilityId).Select(x => new EngineCapability(x.CapabilityId, x.Label, x.Boundary)).ToList(),
            engine.Controls.OrderBy(x => x.ControlId).Select(x => new RuntimeControl(x.ControlId, x.Label, x.Boundary, x.RequiredCapabilityId, x.Description)).ToList(),
            engine.HostingProvider);

    private static ObservabilityBinding ToObservabilityBinding(ObservabilityBindingEntity binding) =>
        new(
            binding.Id.ToString("D"),
            binding.Kind,
            binding.Provider,
            binding.Status,
            binding.Scope,
            0,
            binding.Sample ?? "");

    private static DriftReportItem ToDriftReportItem(DriftReportItemEntity item) =>
        new(
            item.Id.ToString("D"),
            item.EnvironmentId.ToString("D"),
            item.EngineId.ToString("D"),
            item.Area,
            item.Desired,
            item.Observed,
            item.Action);

    private static WorkspaceWorkflowEngine ToWorkspaceWorkflowEngine(WorkflowEngineEntity engine) =>
        new(
            engine.Id,
            engine.WorkspaceId,
            engine.EnvironmentId,
            engine.Name,
            engine.BaseUrl,
            engine.Region,
            engine.Version,
            engine.CertificateStatus,
            engine.CredentialProvider,
            engine.CredentialReference,
            engine.CredentialVerificationStatus,
            engine.CredentialLastVerifiedAt,
            engine.Health,
            engine.LastHeartbeatAt,
            engine.HostingProvider,
            engine.CreatedAt,
            engine.UpdatedAt);

    private static WorkspaceDesiredStateRevision ToWorkspaceDesiredStateRevision(DesiredStateRevisionEntity entity) =>
        new(
            entity.Id,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.EnvironmentId,
            entity.RevisionNumber,
            entity.Label,
            entity.Commit,
            entity.ContentHash,
            entity.DesiredStateJson,
            entity.AuthoredAt,
            entity.CreatedAt,
            entity.CreatedByAccountId);

    private static List<StructuredDesiredStateRecordEntity> ParseStructuredRecords(Guid workspaceId, Guid revisionId, string desiredStateJson)
    {
        using var document = JsonDocument.Parse(desiredStateJson);
        if (!document.RootElement.TryGetProperty("records", out var recordsElement) || recordsElement.ValueKind != JsonValueKind.Array)
            return [];

        return recordsElement.EnumerateArray()
            .Select(record =>
            {
                var kindName = record.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;
                var name = record.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (!Enum.TryParse<DesiredStateRecordKind>(kindName, true, out var kind) || string.IsNullOrWhiteSpace(name))
                    return null;

                var payloadJson = record.TryGetProperty("payload", out var payloadElement) ? payloadElement.GetRawText() : "{}";
                return new StructuredDesiredStateRecordEntity
                {
                    Id = Guid.NewGuid(),
                    WorkspaceId = workspaceId,
                    RevisionId = revisionId,
                    Kind = kind,
                    Name = name,
                    PayloadJson = payloadJson,
                    ContentHash = WorkspaceDeploymentService.ComputeDesiredStateHash(payloadJson)
                };
            })
            .Where(record => record is not null)
            .Cast<StructuredDesiredStateRecordEntity>()
            .ToList();
    }

    private static WorkspacePermissionGrant ToPermissionGrant(WorkspacePermissionGrantEntity entity) =>
        new(
            entity.Id,
            entity.WorkspaceId,
            entity.AccountId,
            entity.Permission,
            entity.GrantedByAccountId,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.RevokedAt);

    private static ActionConfirmation ToActionConfirmation(ActionConfirmationEntity entity) =>
        new(
            entity.Id,
            entity.WorkspaceId,
            entity.ActionType,
            entity.TargetId,
            entity.ConfirmedByAccountId,
            entity.ConfirmedAt,
            entity.ExpiresAt,
            entity.UsedAt);

    private static WorkspaceDeploymentRun ToWorkspaceDeploymentRun(DeploymentRunEntity entity) =>
        new(
            entity.Id,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.EnvironmentId,
            entity.EngineId,
            entity.SourceRevisionId,
            entity.PreviousDeployedRevisionId,
            entity.RollbackSourceRunId,
            entity.Status,
            entity.ValidationOutcome,
            entity.ConfirmationId,
            entity.ActorAccountId,
            entity.QueuedAt,
            entity.StartedAt,
            entity.CompletedAt,
            entity.CreatedAt,
            entity.WorkerId,
            entity.WorkerHeartbeatAt,
            entity.AttemptNumber,
            entity.RecoveryReason,
            entity.FailureMessage);

    private static DeploymentRunHistoryEvent ToDeploymentRunHistoryEvent(DeploymentRunHistoryEventEntity entity) =>
        new(
            entity.Id,
            entity.WorkspaceId,
            entity.RunId,
            entity.Status,
            entity.Message,
            entity.CreatedAt);

    private static RuntimeControlExecution ToRuntimeControlExecution(RuntimeControlExecutionEntity entity) =>
        new(
            entity.Id,
            entity.WorkspaceId,
            entity.EngineId,
            entity.EnvironmentId,
            entity.ControlId,
            entity.ControlLabel,
            entity.Boundary,
            entity.RequiredCapabilityId,
            entity.ConfirmationId,
            entity.ActorAccountId,
            entity.Status,
            entity.CreatedAt,
            entity.Message);

    private static DeploymentHealth EnvironmentHealth(DeploymentEnvironmentEntity environment, IReadOnlyList<WorkflowEngineEntity> engines)
    {
        var environmentEngines = engines.Where(x => x.EnvironmentId == environment.Id).ToList();
        if (environmentEngines.Count == 0)
            return DeploymentHealth.Unreachable;
        if (environmentEngines.Any(x => x.Health == DeploymentHealth.Unreachable))
            return DeploymentHealth.Unreachable;
        if (environmentEngines.Any(x => x.Health == DeploymentHealth.Degraded))
            return DeploymentHealth.Degraded;
        return DeploymentHealth.Healthy;
    }
}
