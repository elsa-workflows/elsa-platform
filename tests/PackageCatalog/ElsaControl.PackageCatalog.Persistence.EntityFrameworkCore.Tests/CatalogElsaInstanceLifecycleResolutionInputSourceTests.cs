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

    private CatalogElsaInstanceLifecycleResolutionInputSource CreateSource(string? origin) =>
        new(_db, new StaticCatalog(CreateEntry()), new ElsaInstancePlanAuthorityOptions { Origin = origin });

    private GovernedReleaseCatalogEntry CreateEntry() => new(
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
        "supported",
        DateTimeOffset.UtcNow);

    private sealed class StaticCatalog(GovernedReleaseCatalogEntry entry) : IGovernedReleaseCatalogStore
    {
        public Task<GovernedReleaseCatalogWriteResult> StoreAsync(
            IReadOnlyList<GovernedReleaseCatalogEntry> entries,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GovernedReleaseCatalogEntry>> QueryAsync(
            GovernedReleaseCatalogQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GovernedReleaseCatalogEntry>>([entry]);
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
