using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using ElsaControl.RuntimeBuilder.Abstractions.ReleaseCatalog;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class CatalogElsaInstanceLifecycleResolutionInputSourceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private CatalogDbContext _db = null!;
    private Workspace _workspace = null!;
    private ElsaInstanceLifecycleAcceptance _accepted = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _db.Database.EnsureCreatedAsync();

        var organization = new Organization { Id = Guid.NewGuid(), Name = "Resolution test organization" };
        _workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            Name = "Resolution test workspace",
            Kind = WorkspaceKind.Shared
        };
        _db.Organizations.Add(organization);
        _db.Workspaces.Add(_workspace);
        await _db.SaveChangesAsync();

        var store = new EfCoreElsaInstanceLifecycleStore(_db, EmptyResolutionInputSource.Instance);
        _accepted = await new ElsaInstanceLifecycleService(store).CreateAsync(new ElsaInstanceCreateRequest(
            organization.Id,
            _workspace.Id,
            "Managed Elsa",
            "resolution-test-elsa",
            new ElsaInstanceIntent(
                new ElsaReleaseIntent("future-runtime", "5.0", "5.0.0"),
                new ElsaApplicationIntent("combined"),
                new ElsaPlacementIntent("managed", "westeurope", "dedicated", "standard-small", "public", "managed")),
            "resolution-create",
            ActorAccountId: Guid.NewGuid()));
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Multiple_engines_for_a_managed_environment_fail_closed()
    {
        var environment = await _db.DeploymentEnvironments
            .Include(x => x.Engines)
            .SingleAsync(x => x.ElsaInstanceId == _accepted.Instance.Id);
        var now = DateTimeOffset.UtcNow;
        _db.WorkflowEngines.Add(new WorkflowEngineEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspace.Id,
            EnvironmentId = environment.Id,
            Name = "ambiguous",
            BaseUrl = "https://managed.invalid/",
            CertificateStatus = CertificateStatus.Untrusted,
            CredentialProvider = "provider-managed",
            CredentialReference = "managed-instance:ambiguous",
            CredentialAssignmentStatus = EngineCredentialAssignmentStatus.Deferred,
            CredentialVerificationStatus = CredentialVerificationStatus.NotVerifiable,
            Health = DeploymentHealth.Unreachable,
            VerificationMessage = "The managed provider has not established a healthy endpoint.",
            HostingProvider = "managed",
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync();

        var source = CreateSource("https://control.example.test");
        var result = await source.GetAsync(_accepted.Instance, _accepted.Operation);

        Assert.Null(result);
    }

    [Fact]
    public async Task Multiple_acceptance_audits_for_an_operation_fail_closed()
    {
        var accepted = await _db.ElsaInstanceAuditEvents
            .AsNoTracking()
            .SingleAsync(x => x.OperationId == _accepted.Operation.Id && x.EventType == "lifecycle.accepted");
        _db.ElsaInstanceAuditEvents.Add(new ElsaInstanceAuditEventEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = accepted.OrganizationId,
            WorkspaceId = accepted.WorkspaceId,
            InstanceId = accepted.InstanceId,
            Sequence = accepted.Sequence + 1,
            EventType = accepted.EventType,
            ActorAccountId = Guid.NewGuid(),
            OperationId = accepted.OperationId,
            PriorState = accepted.PriorState,
            NewState = accepted.NewState,
            DesiredStateRevisionId = accepted.DesiredStateRevisionId,
            PlanReference = accepted.PlanReference,
            DiagnosticCode = accepted.DiagnosticCode,
            Summary = accepted.Summary,
            RequestKeyHash = accepted.RequestKeyHash,
            OccurredAt = accepted.OccurredAt.AddSeconds(1)
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await CreateSource("https://control.example.test").GetAsync(_accepted.Instance, _accepted.Operation);

        Assert.Null(result);
    }

    [Fact]
    public async Task Missing_environment_desired_revision_uses_the_managed_shell_revision_purpose()
    {
        var environment = await _db.DeploymentEnvironments
            .SingleAsync(x => x.ElsaInstanceId == _accepted.Instance.Id);
        environment.DesiredRevisionId = null;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var result = await CreateSource("https://control.example.test").GetAsync(_accepted.Instance, _accepted.Operation);

        Assert.NotNull(result);
        Assert.Equal(
            DeterministicGuid(_accepted.Instance.Id, "desired-revision"),
            result!.DeploymentTarget.SourceRevisionId);
        Assert.NotEqual(DeterministicGuid(_accepted.Instance.Id, "revision"), result.DeploymentTarget.SourceRevisionId);
    }

    [Fact]
    public async Task Resolved_plan_uri_uses_the_configured_control_plane_origin()
    {
        var source = CreateSource("https://control.example.test/");

        var result = await source.GetAsync(_accepted.Instance, _accepted.Operation);

        Assert.NotNull(result);
        Assert.Equal(
            $"https://control.example.test/api/workspaces/{_workspace.Id:D}/instances/{_accepted.Instance.Id:D}/resolved-plans/release-cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
            result!.PlanRequest.PlanUri);
    }

    [Fact]
    public async Task Missing_control_plane_origin_fails_closed_before_resolution()
    {
        var source = CreateSource(null);

        var result = await source.GetAsync(_accepted.Instance, _accepted.Operation);

        Assert.Null(result);
    }

    [Fact]
    public async Task Catalog_entries_outside_supported_lifecycle_fail_closed()
    {
        var source = new CatalogElsaInstanceLifecycleResolutionInputSource(
            _db,
            new StaticCatalog(CreateEntry("preview")),
            new ElsaInstancePlanAuthorityOptions { Origin = "https://control.example.test" });

        var result = await source.GetAsync(_accepted.Instance, _accepted.Operation);

        Assert.Null(result);
    }

    private CatalogElsaInstanceLifecycleResolutionInputSource CreateSource(string? origin) =>
        new(_db, new StaticCatalog(CreateEntry()), new ElsaInstancePlanAuthorityOptions { Origin = origin });

    private GovernedReleaseCatalogEntry CreateEntry(string catalogLifecycle = "supported") => new(
        "2.0.0",
        "https://catalog.example.test/manifests/5.0.0.json",
        "sha256:" + new string('c', 64),
        "sha256:" + new string('d', 64),
        "https://catalog.example.test/signatures/5.0.0.sig",
        "sha256:" + new string('e', 64),
        "paid",
        new(
            "future-runtime",
            "commercial",
            "5.0",
            "5.0.0",
            "stable",
            "stable",
            "commercial",
            "https://github.com/example/runtime",
            new string('a', 40),
            "run-1"),
        new(
            "combined",
            "1",
            ["elsa.server"],
            [],
            [new GovernedReleaseComponentVersion("server", "5.0.0")],
            [new GovernedReleaseComponent(
                "server",
                "registry.example.test/elsa/server@sha256:" + new string('a', 64),
                "sha256:" + new string('a', 64),
                new Dictionary<string, string>(),
                ["server"],
                [],
                [],
                null)],
            []),
        catalogLifecycle,
        DateTimeOffset.UtcNow);

    private static Guid DeterministicGuid(Guid seed, string purpose)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"elsa-control:{purpose}:{seed:D}"));
        return new Guid(bytes[..16]);
    }

    private sealed class StaticCatalog(GovernedReleaseCatalogEntry entry) : IGovernedReleaseCatalogStore
    {
        public Task<GovernedReleaseCatalogWriteResult> StoreAsync(
            IReadOnlyList<GovernedReleaseCatalogEntry> entries,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GovernedReleaseCatalogEntry>> QueryAsync(
            GovernedReleaseCatalogQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GovernedReleaseCatalogEntry>>(
                string.Equals(query.CatalogLifecycle, entry.CatalogLifecycle, StringComparison.OrdinalIgnoreCase)
                    ? [entry]
                    : []);
    }

    private sealed class EmptyResolutionInputSource : IElsaInstanceLifecycleResolutionInputSource
    {
        public static EmptyResolutionInputSource Instance { get; } = new();

        public Task<ElsaInstanceLifecycleResolutionInput?> GetAsync(
            ElsaInstance instance,
            ElsaInstanceOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ElsaInstanceLifecycleResolutionInput?>(null);
    }
}
