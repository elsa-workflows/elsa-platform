using System.Security.Cryptography;
using System.Text;

namespace ElsaControl.Deployment.Abstractions.Instances;

public enum ElsaInstanceMigrationPhase
{
    Planned,
    Preparing,
    ProvisioningTarget,
    Validating,
    Cutover,
    RetainingSource,
    RetiringSource,
    RolledBack,
    Released,
    Failed
}

public enum ElsaInstanceMigrationSourceAccess { Running, ReadOnly, Stopped }

public sealed record ElsaInstanceMigrationReleaseReference
{
    public ElsaInstanceMigrationReleaseReference(
        string planId,
        string planUri,
        string releaseLine,
        string version,
        string manifestDigest,
        string deploymentReference)
    {
        PlanId = ElsaInstanceReferenceValue.RequireToken(planId, nameof(planId));
        PlanUri = ElsaInstanceReferenceValue.RequireAbsoluteApiUri(planUri, nameof(planUri));
        ReleaseLine = ElsaInstanceValue.Catalog(releaseLine, nameof(releaseLine));
        Version = ElsaInstanceValue.Catalog(version, nameof(version));
        if (!ElsaReleaseVersions.BelongsToLine(ReleaseLine, Version))
            throw new ArgumentException("Version must belong to its release line.", nameof(version));
        ManifestDigest = ElsaInstanceReferenceValue.RequireSha256Digest(manifestDigest, nameof(manifestDigest));
        DeploymentReference = ElsaInstanceReferenceValue.RequireToken(deploymentReference, nameof(deploymentReference));
    }

    public string PlanId { get; }
    public string PlanUri { get; }
    public string ReleaseLine { get; }
    public string Version { get; }
    public string ManifestDigest { get; }
    public string DeploymentReference { get; }
}

public sealed record ElsaInstanceMigration
{
    public static readonly TimeSpan MinimumSourceRetention = TimeSpan.FromDays(30);

    private ElsaInstanceMigration(
        Guid id,
        Guid operationId,
        Guid organizationId,
        Guid workspaceId,
        Guid instanceId,
        ElsaInstanceMigrationReleaseReference source,
        ElsaInstanceMigrationReleaseReference target,
        string startRequestHash,
        string lastRequestHash,
        ElsaInstanceMigrationPhase phase,
        ElsaInstanceMigrationSourceAccess sourceAccess,
        DateTimeOffset? cutoverAt,
        DateTimeOffset? sourceRetainUntil,
        Guid? earlyReleaseApprovedByAccountId,
        DateTimeOffset? earlyReleaseApprovedAt,
        DateTimeOffset? sourceReleasedAt,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty || operationId == Guid.Empty || organizationId == Guid.Empty || workspaceId == Guid.Empty || instanceId == Guid.Empty)
            throw new ArgumentException("Migration identity and ownership are required.");
        ValidateHash(startRequestHash, nameof(startRequestHash));
        ValidateHash(lastRequestHash, nameof(lastRequestHash));
        Id = id;
        OperationId = operationId;
        OrganizationId = organizationId;
        WorkspaceId = workspaceId;
        InstanceId = instanceId;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        if (Source == Target)
            throw new ArgumentException("Migration target must differ from source.", nameof(target));
        StartRequestHash = startRequestHash;
        LastRequestHash = lastRequestHash;
        Phase = phase;
        SourceAccess = sourceAccess;
        CutoverAt = cutoverAt;
        SourceRetainUntil = sourceRetainUntil;
        EarlyReleaseApprovedByAccountId = earlyReleaseApprovedByAccountId;
        EarlyReleaseApprovedAt = earlyReleaseApprovedAt;
        SourceReleasedAt = sourceReleasedAt;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = updatedAt.ToUniversalTime();
        ValidateState();
    }

    public Guid Id { get; }
    public Guid OperationId { get; }
    public Guid OrganizationId { get; }
    public Guid WorkspaceId { get; }
    public Guid InstanceId { get; }
    public ElsaInstanceMigrationReleaseReference Source { get; }
    public ElsaInstanceMigrationReleaseReference Target { get; }
    public string StartRequestHash { get; }
    public string LastRequestHash { get; }
    public ElsaInstanceMigrationPhase Phase { get; }
    public ElsaInstanceMigrationSourceAccess SourceAccess { get; }
    public DateTimeOffset? CutoverAt { get; }
    public DateTimeOffset? SourceRetainUntil { get; }
    public Guid? EarlyReleaseApprovedByAccountId { get; }
    public DateTimeOffset? EarlyReleaseApprovedAt { get; }
    public DateTimeOffset? SourceReleasedAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public bool IsTerminal => Phase is ElsaInstanceMigrationPhase.RolledBack or ElsaInstanceMigrationPhase.Released or ElsaInstanceMigrationPhase.Failed;

    public static ElsaInstanceMigration Plan(
        Guid id, Guid organizationId, Guid workspaceId, Guid instanceId,
        ElsaInstanceMigrationReleaseReference source, ElsaInstanceMigrationReleaseReference target,
        string startRequestHash, DateTimeOffset now) =>
        new(id, Guid.NewGuid(), organizationId, workspaceId, instanceId, source, target, startRequestHash, startRequestHash,
            ElsaInstanceMigrationPhase.Planned, ElsaInstanceMigrationSourceAccess.Running,
            null, null, null, null, null, now, now);

    public static ElsaInstanceMigration Plan(
        Guid id, Guid operationId, Guid organizationId, Guid workspaceId, Guid instanceId,
        ElsaInstanceMigrationReleaseReference source, ElsaInstanceMigrationReleaseReference target,
        string startRequestHash, DateTimeOffset now) =>
        new(id, operationId, organizationId, workspaceId, instanceId, source, target, startRequestHash, startRequestHash,
            ElsaInstanceMigrationPhase.Planned, ElsaInstanceMigrationSourceAccess.Running,
            null, null, null, null, null, now, now);

    public static ElsaInstanceMigration Hydrate(
        Guid id, Guid organizationId, Guid workspaceId, Guid instanceId,
        ElsaInstanceMigrationReleaseReference source, ElsaInstanceMigrationReleaseReference target,
        Guid operationId, string startRequestHash, string lastRequestHash,
        ElsaInstanceMigrationPhase phase, ElsaInstanceMigrationSourceAccess sourceAccess,
        DateTimeOffset? cutoverAt, DateTimeOffset? sourceRetainUntil,
        Guid? earlyReleaseApprovedByAccountId, DateTimeOffset? earlyReleaseApprovedAt,
        DateTimeOffset? sourceReleasedAt, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(id, operationId, organizationId, workspaceId, instanceId, source, target, startRequestHash, lastRequestHash, phase, sourceAccess,
            cutoverAt, sourceRetainUntil, earlyReleaseApprovedByAccountId, earlyReleaseApprovedAt,
            sourceReleasedAt, createdAt, updatedAt);

    public ElsaInstanceMigration Advance(ElsaInstanceMigrationPhase next, DateTimeOffset now)
    {
        if (!CanTransition(Phase, next))
            throw new InvalidOperationException("Migration phase transition is not allowed.");
        if (next is ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.RetiringSource or ElsaInstanceMigrationPhase.Released)
            throw new InvalidOperationException("Migration phase requires its dedicated verified path.");
        return Copy(phase: next, updatedAt: RequireLater(now));
    }

    public ElsaInstanceMigration CutOver(bool targetHealthVerified, ElsaInstanceMigrationSourceAccess sourceAccess, DateTimeOffset now)
    {
        if (!targetHealthVerified)
            throw new InvalidOperationException("Target health must be verified before cutover.");
        if (sourceAccess == ElsaInstanceMigrationSourceAccess.Running)
            throw new InvalidOperationException("Cutover must make the source read-only or stopped.");
        if (!CanTransition(Phase, ElsaInstanceMigrationPhase.Cutover))
            throw new InvalidOperationException("Migration phase transition is not allowed.");
        var cutover = RequireLater(now);
        return Copy(ElsaInstanceMigrationPhase.Cutover, sourceAccess, cutover,
            cutover.Add(MinimumSourceRetention), updatedAt: cutover);
    }

    public ElsaInstanceMigration RetainSource(DateTimeOffset now)
    {
        if (Phase != ElsaInstanceMigrationPhase.Cutover)
            throw new InvalidOperationException("Only a cutover migration can enter source retention.");
        return Copy(phase: ElsaInstanceMigrationPhase.RetainingSource, updatedAt: RequireLater(now));
    }

    public ElsaInstanceMigration ApproveEarlyRelease(Guid accountId, DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Approving account is required.", nameof(accountId));
        if (Phase is not (ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.RetainingSource))
            throw new InvalidOperationException("Early release approval requires a retained source.");
        var approvedAt = RequireLater(now);
        return Copy(earlyReleaseApprovedByAccountId: accountId, earlyReleaseApprovedAt: approvedAt, updatedAt: approvedAt);
    }

    public ElsaInstanceMigration BeginSourceRetirement(DateTimeOffset now)
    {
        if (Phase is not (ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.RetainingSource or ElsaInstanceMigrationPhase.RetiringSource))
            throw new InvalidOperationException("Migration source is not releasable.");
        var releasedAt = RequireLater(now);
        if (SourceRetainUntil is null)
            throw new InvalidOperationException("Migration source has no retention deadline.");
        if (releasedAt < SourceRetainUntil &&
            (EarlyReleaseApprovedByAccountId is null || EarlyReleaseApprovedAt is null || EarlyReleaseApprovedAt > releasedAt))
            throw new InvalidOperationException("Source retention has not expired and no prior early-release approval exists.");
        return Copy(ElsaInstanceMigrationPhase.RetiringSource, ElsaInstanceMigrationSourceAccess.Stopped,
            updatedAt: releasedAt);
    }

    public ElsaInstanceMigration ConfirmSourceReleased(DateTimeOffset now)
    {
        if (Phase != ElsaInstanceMigrationPhase.RetiringSource)
            throw new InvalidOperationException("Only a retiring source can be confirmed released.");
        var releasedAt = RequireLater(now);
        return Copy(ElsaInstanceMigrationPhase.Released, ElsaInstanceMigrationSourceAccess.Stopped,
            sourceReleasedAt: releasedAt, updatedAt: releasedAt);
    }

    public ElsaInstanceMigration RecordRequest(string requestHash) =>
        Copy(lastRequestHash: ValidateHash(requestHash, nameof(requestHash)));

    public static string HashRequestKey(string requestKey)
    {
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 256 || requestKey.Any(char.IsControl))
            throw new ArgumentException("Migration request key is invalid.", nameof(requestKey));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestKey.Trim())));
    }

    private DateTimeOffset RequireLater(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        if (utc <= UpdatedAt)
            throw new InvalidOperationException("Migration update timestamp must advance.");
        return utc;
    }

    private ElsaInstanceMigration Copy(
        ElsaInstanceMigrationPhase? phase = null,
        ElsaInstanceMigrationSourceAccess? sourceAccess = null,
        DateTimeOffset? cutoverAt = null,
        DateTimeOffset? sourceRetainUntil = null,
        Guid? earlyReleaseApprovedByAccountId = null,
        DateTimeOffset? earlyReleaseApprovedAt = null,
        DateTimeOffset? sourceReleasedAt = null,
        DateTimeOffset? updatedAt = null,
        string? lastRequestHash = null) =>
        new(Id, OperationId, OrganizationId, WorkspaceId, InstanceId, Source, Target, StartRequestHash,
            lastRequestHash ?? LastRequestHash,
            phase ?? Phase, sourceAccess ?? SourceAccess, cutoverAt ?? CutoverAt,
            sourceRetainUntil ?? SourceRetainUntil,
            earlyReleaseApprovedByAccountId ?? EarlyReleaseApprovedByAccountId,
            earlyReleaseApprovedAt ?? EarlyReleaseApprovedAt,
            sourceReleasedAt ?? SourceReleasedAt, CreatedAt, updatedAt ?? UpdatedAt);

    private void ValidateState()
    {
        if (!Enum.IsDefined(Phase) || !Enum.IsDefined(SourceAccess))
            throw new ArgumentException("Migration phase or source access mode is invalid.");
        if (CreatedAt == default || UpdatedAt < CreatedAt)
            throw new ArgumentException("Migration timestamps are invalid.");
        if ((CutoverAt is null) != (SourceRetainUntil is null) ||
            CutoverAt is not null && SourceRetainUntil < CutoverAt.Value.Add(MinimumSourceRetention))
            throw new ArgumentException("Cutover requires at least 30 days of source retention.");
        if (CutoverAt is not null && SourceAccess == ElsaInstanceMigrationSourceAccess.Running)
            throw new ArgumentException("A cutover source cannot remain writable.");
        if ((EarlyReleaseApprovedByAccountId is null) != (EarlyReleaseApprovedAt is null) ||
            EarlyReleaseApprovedByAccountId == Guid.Empty)
            throw new ArgumentException("Early-release approval is incomplete.");
        if (SourceReleasedAt is not null && (CutoverAt is null || SourceRetainUntil is null || Phase != ElsaInstanceMigrationPhase.Released))
            throw new ArgumentException("Source release state is invalid.");
        if (Phase is (ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.RetainingSource or
                ElsaInstanceMigrationPhase.RetiringSource or ElsaInstanceMigrationPhase.Released) && CutoverAt is null)
            throw new ArgumentException("Migration phase requires a completed cutover.");
        if (Phase == ElsaInstanceMigrationPhase.Released && SourceReleasedAt is null)
            throw new ArgumentException("Released migration requires a source release timestamp.");
    }

    private static string ValidateHash(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64 ||
            value.Any(x => !(char.IsAsciiDigit(x) || x is >= 'a' and <= 'f')))
            throw new ArgumentException("Migration request hash must be lowercase SHA-256 hex.", name);
        return value;
    }

    private static bool CanTransition(ElsaInstanceMigrationPhase current, ElsaInstanceMigrationPhase next) =>
        current == next || (current, next) switch
        {
            (ElsaInstanceMigrationPhase.Planned, ElsaInstanceMigrationPhase.Preparing or ElsaInstanceMigrationPhase.ProvisioningTarget or ElsaInstanceMigrationPhase.Failed) => true,
            (ElsaInstanceMigrationPhase.Preparing, ElsaInstanceMigrationPhase.ProvisioningTarget or ElsaInstanceMigrationPhase.Validating or ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.Failed) => true,
            (ElsaInstanceMigrationPhase.ProvisioningTarget, ElsaInstanceMigrationPhase.Validating or ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.Failed) => true,
            (ElsaInstanceMigrationPhase.Validating, ElsaInstanceMigrationPhase.Cutover or ElsaInstanceMigrationPhase.Failed) => true,
            (ElsaInstanceMigrationPhase.Cutover, ElsaInstanceMigrationPhase.RetainingSource or ElsaInstanceMigrationPhase.RetiringSource or ElsaInstanceMigrationPhase.RolledBack or ElsaInstanceMigrationPhase.Failed) => true,
            (ElsaInstanceMigrationPhase.RetainingSource, ElsaInstanceMigrationPhase.RetiringSource or ElsaInstanceMigrationPhase.RolledBack or ElsaInstanceMigrationPhase.Failed) => true,
            (ElsaInstanceMigrationPhase.RetiringSource, ElsaInstanceMigrationPhase.Released or ElsaInstanceMigrationPhase.RolledBack or ElsaInstanceMigrationPhase.Failed) => true,
            _ => false
        };
}
