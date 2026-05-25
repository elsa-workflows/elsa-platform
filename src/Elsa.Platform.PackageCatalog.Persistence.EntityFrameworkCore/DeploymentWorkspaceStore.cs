using Elsa.Platform.Deployment.Core.Cockpit;
using Elsa.Platform.Deployment.Core.Workspace;
using Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Platform.PackageCatalog.Persistence.EntityFrameworkCore;

public sealed class DeploymentWorkspaceStore(CatalogDbContext dbContext) : IWorkspaceDeploymentStore
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
            [],
            [],
            [],
            []);
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

    public Task<WorkspaceDeploymentEnvironment> CreateEnvironmentAsync(
        Guid workspaceId,
        CreateDeploymentEnvironmentRequest request,
        CancellationToken cancellationToken = default)
    {
        return CreateEnvironmentCoreAsync(workspaceId, request, cancellationToken);
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
        var entity = new DesiredStateRevisionEntity
        {
            Id = Guid.NewGuid(),
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
            CreatedByAccountId = request.ActorAccountId
        };

        await dbContext.DesiredStateRevisions.AddAsync(entity, cancellationToken);
        environment.DesiredRevisionId = entity.Id;
        environment.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToWorkspaceDesiredStateRevision(entity);
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
                ? new DesiredStateRevision(0, "", "No desired revision", environment.CreatedAt)
                : new DesiredStateRevision(desiredRevision.RevisionNumber, desiredRevision.Commit ?? "", desiredRevision.Label, desiredRevision.AuthoredAt),
            deployedRevision?.RevisionNumber,
            environment.DeploymentStatus,
            environment.DriftStatus,
            engines.Where(x => x.EnvironmentId == environment.Id).Select(x => x.Id.ToString("D")).ToList());
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
