using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Workspace;

namespace ElsaControl.Deployment.Core.Instances;

public sealed record ElsaInstanceMigrationStartRequest(
    Guid OrganizationId, Guid WorkspaceId, Guid InstanceId, int ExpectedInstanceVersion,
    ElsaInstanceMigrationReleaseReference Source, ElsaInstanceMigrationReleaseReference Target,
    string IdempotencyKey, Guid ActorAccountId);

public sealed record ElsaInstanceMigrationChangeRequest(
    Guid WorkspaceId, Guid MigrationId, DateTimeOffset ExpectedUpdatedAt,
    string IdempotencyKey, Guid ActorAccountId);

public sealed record ElsaInstanceMigrationStartEnvelope(
    ElsaInstanceMigration Migration, int ExpectedInstanceVersion, string IdempotencyKey);

public enum ElsaInstanceMigrationWriteOutcome { Applied, Replayed, Conflict, NotFound }

public sealed record ElsaInstanceMigrationWriteResult(
    ElsaInstanceMigrationWriteOutcome Outcome, ElsaInstanceMigration? Migration, string DiagnosticCode);

public sealed record ElsaInstanceMigrationAudit(
    Guid MigrationId, Guid OperationId, string EventType, string? PriorState, string NewState,
    Guid ActorAccountId, string RequestHash, DateTimeOffset OccurredAt);

public interface IElsaInstanceMigrationStore
{
    Task<ElsaInstanceMigration?> GetAsync(Guid workspaceId, Guid migrationId, CancellationToken cancellationToken = default);
    Task<ElsaInstanceMigrationWriteResult> CreateAsync(
        ElsaInstanceMigrationStartEnvelope envelope, ElsaInstanceMigrationAudit audit,
        CancellationToken cancellationToken = default);
    Task<ElsaInstanceMigrationWriteResult> SaveAsync(
        ElsaInstanceMigration migration, DateTimeOffset expectedUpdatedAt, ElsaInstanceMigrationAudit audit,
        CancellationToken cancellationToken = default);
}

public interface IElsaInstanceMigrationAuthorizer
{
    Task RequireExecutionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default);
    Task RequireEarlyReleaseAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceElsaInstanceMigrationAuthorizer(WorkspacePermissionService permissions) : IElsaInstanceMigrationAuthorizer
{
    public Task RequireExecutionAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
        permissions.RequireAsync(workspaceId, accountId, WorkspaceDeploymentPermissions.ExecuteDeployment, cancellationToken);

    public Task RequireEarlyReleaseAsync(Guid workspaceId, Guid accountId, CancellationToken cancellationToken = default) =>
        permissions.RequireAsync(workspaceId, accountId, WorkspaceDeploymentPermissions.ExecuteControls, cancellationToken);
}

public sealed class ElsaInstanceMigrationService(
    IElsaInstanceMigrationStore store,
    IElsaInstanceMigrationAuthorizer authorizer,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ElsaInstanceMigrationWriteResult> StartAsync(
        ElsaInstanceMigrationStartRequest request, CancellationToken cancellationToken = default)
    {
        ValidateStart(request);
        await authorizer.RequireExecutionAsync(request.WorkspaceId, request.ActorAccountId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var requestHash = StartHash(request);
        var migration = ElsaInstanceMigration.Plan(Guid.NewGuid(), Guid.NewGuid(), request.OrganizationId,
            request.WorkspaceId, request.InstanceId, request.Source, request.Target, requestHash, now);
        return await store.CreateAsync(new(migration, request.ExpectedInstanceVersion, request.IdempotencyKey),
            Audit(migration, "MajorMigrationStarted", null, migration.Phase, request.ActorAccountId, requestHash, now),
            cancellationToken);
    }

    public Task<ElsaInstanceMigrationWriteResult> AdvanceAsync(
        ElsaInstanceMigrationChangeRequest request, ElsaInstanceMigrationPhase next,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(request, "advance:" + next, migration => migration.Advance(next, _timeProvider.GetUtcNow()),
            "MajorMigrationAdvanced", next, false, cancellationToken);

    public Task<ElsaInstanceMigrationWriteResult> CutOverAsync(
        ElsaInstanceMigrationChangeRequest request, bool targetHealthVerified,
        ElsaInstanceMigrationSourceAccess sourceAccess, CancellationToken cancellationToken = default) =>
        ChangeAsync(request, $"cutover:{targetHealthVerified}:{sourceAccess}",
            migration => migration.CutOver(targetHealthVerified, sourceAccess, _timeProvider.GetUtcNow()),
            "MigrationCutover", ElsaInstanceMigrationPhase.Cutover, false, cancellationToken);

    public Task<ElsaInstanceMigrationWriteResult> RetainSourceAsync(
        ElsaInstanceMigrationChangeRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(request, "retain-source", migration => migration.RetainSource(_timeProvider.GetUtcNow()),
            "MigrationSourceRetained", ElsaInstanceMigrationPhase.RetainingSource, false, cancellationToken);

    public Task<ElsaInstanceMigrationWriteResult> ApproveEarlyReleaseAsync(
        ElsaInstanceMigrationChangeRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(request, "approve-early-release",
            migration => migration.ApproveEarlyRelease(request.ActorAccountId, _timeProvider.GetUtcNow()),
            "MigrationEarlyReleaseApproved", null, true, cancellationToken);

    public Task<ElsaInstanceMigrationWriteResult> ReleaseSourceAsync(
        ElsaInstanceMigrationChangeRequest request, CancellationToken cancellationToken = default) =>
        ChangeAsync(request, "request-source-release", migration => migration.BeginSourceRetirement(_timeProvider.GetUtcNow()),
            "MigrationSourceReleaseRequested", ElsaInstanceMigrationPhase.RetiringSource, false, cancellationToken);

    private async Task<ElsaInstanceMigrationWriteResult> ChangeAsync(
        ElsaInstanceMigrationChangeRequest request, string command,
        Func<ElsaInstanceMigration, ElsaInstanceMigration> change, string eventType,
        ElsaInstanceMigrationPhase? expectedPhase, bool earlyRelease, CancellationToken cancellationToken)
    {
        ValidateChange(request);
        if (earlyRelease)
            await authorizer.RequireEarlyReleaseAsync(request.WorkspaceId, request.ActorAccountId, cancellationToken);
        else
            await authorizer.RequireExecutionAsync(request.WorkspaceId, request.ActorAccountId, cancellationToken);
        var requestHash = ChangeHash(request, command);
        var current = await store.GetAsync(request.WorkspaceId, request.MigrationId, cancellationToken);
        if (current is null)
            return new(ElsaInstanceMigrationWriteOutcome.NotFound, null, "migration.not-found");
        if (current.LastRequestHash == requestHash)
            return new(ElsaInstanceMigrationWriteOutcome.Replayed, current, "migration.replayed");
        if (current.UpdatedAt != request.ExpectedUpdatedAt.ToUniversalTime())
            return new(ElsaInstanceMigrationWriteOutcome.Conflict, current, "migration.version.conflict");
        if (expectedPhase is not null && current.Phase == expectedPhase)
            return new(ElsaInstanceMigrationWriteOutcome.Conflict, current, "migration.idempotency.conflict");

        var updated = change(current).RecordRequest(requestHash);
        return await store.SaveAsync(updated, current.UpdatedAt,
            Audit(updated, eventType, current.Phase.ToString(), updated.Phase,
                request.ActorAccountId, requestHash, updated.UpdatedAt), cancellationToken);
    }

    private static ElsaInstanceMigrationAudit Audit(
        ElsaInstanceMigration migration, string eventType, string? priorState,
        ElsaInstanceMigrationPhase newState, Guid actorAccountId, string requestHash, DateTimeOffset occurredAt) =>
        new(migration.Id, migration.OperationId, eventType, priorState, newState.ToString(), actorAccountId,
            requestHash, occurredAt);

    private static string StartHash(ElsaInstanceMigrationStartRequest request) => HashCanonical(
        "start", request.OrganizationId.ToString("D"), request.WorkspaceId.ToString("D"), request.InstanceId.ToString("D"),
        request.ExpectedInstanceVersion.ToString(CultureInfo.InvariantCulture), request.IdempotencyKey.Trim(),
        Canonical(request.Source), Canonical(request.Target));

    private static string ChangeHash(ElsaInstanceMigrationChangeRequest request, string command) => HashCanonical(
        command, request.WorkspaceId.ToString("D"), request.MigrationId.ToString("D"),
        request.ExpectedUpdatedAt.ToUniversalTime().UtcTicks.ToString(CultureInfo.InvariantCulture),
        request.IdempotencyKey.Trim());

    private static string Canonical(ElsaInstanceMigrationReleaseReference reference) => string.Join('\n',
        reference.PlanId, reference.PlanUri, reference.ReleaseLine, reference.Version,
        reference.ManifestDigest, reference.DeploymentReference);

    private static string HashCanonical(params string[] values) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values) + "\n")));

    private static void ValidateStart(ElsaInstanceMigrationStartRequest request)
    {
        if (request.OrganizationId == Guid.Empty || request.WorkspaceId == Guid.Empty || request.InstanceId == Guid.Empty ||
            request.ExpectedInstanceVersion < 1 || request.Source is null || request.Target is null || request.ActorAccountId == Guid.Empty)
            throw new ArgumentException("Migration start request is invalid.", nameof(request));
        _ = ElsaInstanceMigration.RequireRequestKey(request.IdempotencyKey);
    }

    private static void ValidateChange(ElsaInstanceMigrationChangeRequest request)
    {
        if (request.WorkspaceId == Guid.Empty || request.MigrationId == Guid.Empty || request.ExpectedUpdatedAt == default ||
            request.ActorAccountId == Guid.Empty)
            throw new ArgumentException("Migration change request is invalid.", nameof(request));
        _ = ElsaInstanceMigration.RequireRequestKey(request.IdempotencyKey);
    }
}
