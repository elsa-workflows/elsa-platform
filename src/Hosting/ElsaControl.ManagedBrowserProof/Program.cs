using System.Text.Json;
using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Instances;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

const string FixtureKey = "managed-browser-proof-v1";
var instanceId = Guid.Parse("00000000-0000-0000-0000-000000000185");

if (args.Length != 3 || args[0] is not ("seed" or "revoke" or "restore" or "unavailable"))
    return Fail("Usage: ElsaControl.ManagedBrowserProof <seed|revoke|restore|unavailable> <sqlite-db-path> <runtime-origin>.");

var command = args[0];
var databasePath = Path.GetFullPath(args[1]);
if (!File.Exists(databasePath))
    return Fail("The fixture database does not exist.");
if (!ElsaManagedEndpointOrigin.TryCreate(args[2], out var runtimeOrigin))
    return Fail("The runtime origin must be an absolute HTTPS origin without credentials, path, query, or fragment.");

var options = new DbContextOptionsBuilder<CatalogDbContext>()
    .UseSqlite($"Data Source={databasePath}", sqlite =>
        sqlite.MigrationsAssembly("ElsaControl.PackageCatalog.Persistence.SqliteMigrations"))
    .Options;
await using var database = new CatalogDbContext(options);
await database.Database.MigrateAsync();

var workspaces = await database.Workspaces
    .AsNoTracking()
    .Where(x => x.SoftDeletedAt == null)
    .Select(x => new { x.Id, x.OrganizationId })
    .Take(2)
    .ToListAsync();
if (workspaces.Count != 1)
    return Fail("The isolated proof database must contain exactly one active workspace.");

var workspaceId = workspaces[0].Id;
var organizationId = workspaces[0].OrganizationId;
var accountIds = await database.WorkspaceMemberships
    .AsNoTracking()
    .Where(x => x.WorkspaceId == workspaceId && x.Role == WorkspaceRole.Owner)
    .Select(x => x.AccountId)
    .Distinct()
    .Take(2)
    .ToListAsync();
if (accountIds.Count != 1)
    return Fail("The isolated proof database must contain exactly one workspace owner.");
var accountId = accountIds[0];

switch (command)
{
    case "seed":
        await SeedAsync(database, organizationId, workspaceId, accountId, instanceId, runtimeOrigin);
        break;
    case "revoke":
        await SetMembershipAsync(database, organizationId, accountId, disabled: true);
        break;
    case "restore":
        await SetMembershipAsync(database, organizationId, accountId, disabled: false);
        await SetAvailabilityAsync(database, instanceId, available: true, runtimeOrigin);
        break;
    case "unavailable":
        await SetAvailabilityAsync(database, instanceId, available: false, runtimeOrigin);
        break;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    command,
    organizationId,
    workspaceId,
    instanceId,
    runtimeOrigin = runtimeOrigin.Value,
    status = "succeeded"
}));
return 0;

static async Task SeedAsync(
    CatalogDbContext database,
    Guid organizationId,
    Guid workspaceId,
    Guid accountId,
    Guid instanceId,
    ElsaManagedEndpointOrigin runtimeOrigin)
{
    var permissions = new WorkspacePermissionService(
        new DeploymentWorkspaceStore(database),
        [new ManagedElsaInstancePermissionContribution()]);
    await permissions.BootstrapOwnerPermissionsAsync(workspaceId, accountId);

    var entitlement = await database.OrganizationEntitlementSnapshots
        .OrderByDescending(x => x.SyncedAt)
        .ThenByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync(x => x.OrganizationId == organizationId);
    if (entitlement is null)
    {
        database.OrganizationEntitlementSnapshots.Add(new OrganizationEntitlementSnapshot
        {
            OrganizationId = organizationId,
            ManagedHostingEnabled = true,
            MaxSources = 5,
            MaxWorkspaces = 5
        });
        await database.SaveChangesAsync();
    }
    else if (!entitlement.ManagedHostingEnabled)
    {
        entitlement.ManagedHostingEnabled = true;
        await database.SaveChangesAsync();
    }

    var lifecycle = new ElsaInstanceLifecycleService(
        new EfCoreElsaInstanceLifecycleStore(database, new UnavailableElsaInstanceLifecycleResolutionInputSource()));
    await lifecycle.CreateAsync(new ElsaInstanceCreateRequest(
        organizationId,
        workspaceId,
        "Managed Elsa browser proof",
        "managed-elsa-browser-proof",
        new ElsaInstanceIntent(
            new ElsaReleaseIntent("valence-runtime", "3.8", "3.8.0-preview.5413", channel: "preview"),
            new ElsaApplicationIntent("combined"),
            new ElsaPlacementIntent("managed", "local", "dedicated", "proof-single", "public", "managed")),
        FixtureKey,
        instanceId,
        accountId));

    database.ChangeTracker.Clear();

    var identities = new EfCoreManagedElsaInstanceIdentityStore(database);
    var existing = await identities.FindAsync(organizationId, instanceId);
    if (existing is not null &&
        (!string.Equals(existing.CallbackUri.GetLeftPart(UriPartial.Authority), runtimeOrigin.Value, StringComparison.Ordinal) ||
         !string.Equals(existing.Audience, ElsaInstanceIdentityBinding.AudienceFor(instanceId), StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The existing proof identity binding conflicts with the requested runtime origin.");
    }

    await SetAvailabilityAsync(database, instanceId, available: true, runtimeOrigin);
    if (existing is null)
    {
        var binding = await identities.BindAsync(
            organizationId,
            workspaceId,
            instanceId,
            runtimeOrigin.Value,
            expectedBindingVersion: null,
            DateTimeOffset.UtcNow);
        if (!binding.Succeeded)
            throw new InvalidOperationException("The proof identity binding could not be created.");
    }
}

static async Task SetMembershipAsync(CatalogDbContext database, Guid organizationId, Guid accountId, bool disabled)
{
    var membership = await database.OrganizationMemberships
        .SingleAsync(x => x.OrganizationId == organizationId && x.AccountId == accountId);
    membership.DisabledAt = disabled ? DateTimeOffset.UtcNow : null;
    membership.UpdatedAt = DateTimeOffset.UtcNow;
    await database.SaveChangesAsync();
}

static async Task SetAvailabilityAsync(
    CatalogDbContext database,
    Guid instanceId,
    bool available,
    ElsaManagedEndpointOrigin runtimeOrigin)
{
    var endpoint = runtimeOrigin.Value;
    var observedLifecycle = available ? ElsaObservedLifecycle.Ready : ElsaObservedLifecycle.Unknown;
    var health = available ? ElsaInstanceHealth.Healthy : ElsaInstanceHealth.Unknown;
    var affected = await database.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE ElsaInstances SET CurrentDeploymentId = {"deployment-managed-browser-proof"}, CurrentDeploymentEndpointUri = {endpoint}, DesiredLifecycle = {ElsaDesiredLifecycle.Running.ToString()}, ObservedLifecycle = {observedLifecycle.ToString()}, Health = {health.ToString()} WHERE Id = {instanceId}");
    if (affected != 1)
        throw new InvalidOperationException("The proof instance was not found.");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}
