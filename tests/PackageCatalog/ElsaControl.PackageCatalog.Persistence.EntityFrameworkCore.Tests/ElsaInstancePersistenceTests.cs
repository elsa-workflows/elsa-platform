using ElsaControl.Deployment.Abstractions.Instances;
using ElsaControl.Deployment.Core.Cockpit;
using ElsaControl.Deployment.Core.Workspace;
using ElsaControl.PackageCatalog.Core.Accounts;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore;
using ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ElsaControl.PackageCatalog.Persistence.EntityFrameworkCore.Tests;

public sealed class ElsaInstancePersistenceTests
{
    private const string PreviousMigration = "20260716031322_AddWorkspacePermissionLifecycle";

    [Fact]
    public async Task Additive_migration_preserves_existing_environment_as_unbound()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        Guid environmentId;
        await using (var db = CreateMigratedContext(connection))
        {
            await db.Database.MigrateAsync(PreviousMigration);
            var workspace = new Workspace { Name = "Legacy workspace" };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync();
            var applicationId = Guid.NewGuid();
            environmentId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow.UtcTicks;
            // The current EF model contains ElsaInstanceId, so it cannot query the
            // pre-change schema. Seed through the old column set after migrating
            // only to the historical boundary, then apply the additive migration.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO DeploymentApplications
                    (Id, WorkspaceId, Name, Description, CreatedAt, UpdatedAt, CreatedByAccountId, UpdatedByAccountId)
                VALUES ({applicationId}, {workspace.Id}, {"Legacy application"}, {null}, {now}, {now}, {null}, {null});
                INSERT INTO DeploymentEnvironments
                    (Id, WorkspaceId, ApplicationId, Name, Tier, TierRequiresReview, DesiredRevisionId,
                     DeployedRevisionId, DeploymentStatus, DriftStatus, CreatedAt, UpdatedAt)
                VALUES ({environmentId}, {workspace.Id}, {applicationId}, {"Legacy environment"}, {"Production"}, {false},
                        {null}, {null}, {"Blocked"}, {"Unknown"}, {now}, {now});
                """);

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateMigratedContext(connection))
        {
            var unbound = await db.Database.SqlQuery<Guid?>(
                    $"SELECT ElsaInstanceId AS Value FROM DeploymentEnvironments WHERE Id = {environmentId}")
                .SingleAsync();
            Assert.Null(unbound);

            var tables = await ReadScalarStringsAsync(db, "SELECT name FROM sqlite_master WHERE type = 'table'");
            Assert.Contains("ElsaInstances", tables);
            Assert.Contains("ElsaInstanceOperations", tables);
            Assert.Contains("ElsaInstanceAuditEvents", tables);
            Assert.Contains("ElsaInstanceIdentityBindings", tables);
            Assert.Contains("ElsaInstanceMigrations", tables);
        }
    }

    [Fact]
    public async Task Environment_binding_is_explicit_and_globally_unique()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Managed workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();

        var application = new DeploymentApplicationEntity
        {
            WorkspaceId = workspace.Id,
            Name = "Managed app",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.DeploymentApplications.Add(application);
        db.DeploymentEnvironments.AddRange(
            NewEnvironment(workspace.Id, application.Id, instance.Id, "Managed one"),
            NewEnvironment(workspace.Id, application.Id, instance.Id, "Managed two"));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Model_contains_provider_correct_reservation_filters()
    {
        using var db = CreateEnsureCreatedContext();
        var operation = db.Model.FindEntityType(typeof(ElsaInstanceOperationEntity))!;
        var operationFilters = operation.GetIndexes().Select(x => x.GetFilter()).Where(x => x is not null).ToArray();
        Assert.Contains(operationFilters, x => x!.Contains("Accepted", StringComparison.Ordinal) && x.Contains("RecoveryRequired", StringComparison.Ordinal));
        Assert.Contains(operationFilters, x => x!.Contains("WaitingForPriorOperation", StringComparison.Ordinal));

        var run = db.Model.FindEntityType(typeof(DeploymentRunEntity))!;
        Assert.Contains(run.GetIndexes().Select(x => x.GetFilter()), x => x == "ElsaInstanceId IS NOT NULL AND Status IN ('Queued', 'Running', 'RecoveryRequired')");
    }

    [Fact]
    public async Task Active_and_waiting_operation_reservations_are_independently_unique()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Operation workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();

        db.ElsaInstanceOperations.AddRange(
            NewOperation(workspace, instance, ElsaInstanceOperationState.Queued, "active-1"),
            NewOperation(workspace, instance, ElsaInstanceOperationState.Running, "active-2"));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.ElsaInstanceOperations.AddRange(
            NewOperation(workspace, instance, ElsaInstanceOperationState.WaitingForPriorOperation, "waiting-1"),
            NewOperation(workspace, instance, ElsaInstanceOperationState.WaitingForPriorOperation, "waiting-2"));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Idempotency_key_collision_is_rejected_even_for_completed_operations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Idempotency workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var first = NewOperation(workspace, null, ElsaInstanceOperationState.Succeeded, "same-key");
        var second = NewOperation(workspace, null, ElsaInstanceOperationState.Failed, "different-id");
        second.IdempotencyScope = first.IdempotencyScope;
        second.IdempotencyKey = first.IdempotencyKey;
        db.ElsaInstanceOperations.AddRange(first, second);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cross_workspace_application_environment_and_instance_links_fail_closed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var first = new Workspace { Name = "First workspace" };
        var second = new Workspace { Name = "Second workspace" };
        db.Workspaces.AddRange(first, second);
        await db.SaveChangesAsync();
        var application = new DeploymentApplicationEntity
        {
            WorkspaceId = first.Id,
            Name = "First app",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.DeploymentApplications.Add(application);
        await db.SaveChangesAsync();
        db.DeploymentEnvironments.Add(NewEnvironment(second.Id, application.Id, null, "Cross-owned environment"));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var instance = NewInstance(first.OrganizationId, first.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();
        db.ElsaInstanceOperations.Add(NewOperation(second, instance, ElsaInstanceOperationState.Succeeded, "cross-operation"));
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.ElsaInstanceAuditEvents.Add(new ElsaInstanceAuditEventEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = second.OrganizationId,
            WorkspaceId = second.Id,
            InstanceId = instance.Id,
            Sequence = 1,
            EventType = "instance.updated",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Identity_binding_audience_and_callback_are_unique()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Binding workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var first = NewInstance(workspace.OrganizationId, workspace.Id);
        var second = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.AddRange(first, second);
        await db.SaveChangesAsync();
        var firstAudience = $"urn:elsa:instance:{first.Id:D}".ToLowerInvariant();
        var secondAudience = $"urn:elsa:instance:{second.Id:D}".ToLowerInvariant();
        db.ElsaInstanceIdentityBindings.AddRange(
            NewBinding(first.Id, firstAudience, "https://control.example/managed-elsa/handoff/callback"),
            NewBinding(second.Id, secondAudience, "https://control.example/managed-elsa/handoff/callback"));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Identity_binding_callback_must_match_the_verified_endpoint_origin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Callback workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();

        db.ElsaInstanceIdentityBindings.Add(NewBinding(
            instance.Id,
            $"urn:elsa:instance:{instance.Id:D}",
            "https://other.example/managed-elsa/handoff/callback"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.ElsaInstanceIdentityBindings.Add(new ElsaInstanceIdentityBindingEntity
        {
            InstanceId = instance.Id,
            Audience = $"urn:elsa:instance:{instance.Id:D}",
            CanonicalCallbackUri = "https://control.example/managed-elsa/handoff/callback",
            VerifiedEndpointOrigin = "https://control.example/runtime",
            BindingVersion = 1,
            ChangedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.ElsaInstanceIdentityBindings.Add(new ElsaInstanceIdentityBindingEntity
        {
            InstanceId = instance.Id,
            Audience = $"urn:elsa:instance:{instance.Id:D}",
            CanonicalCallbackUri = "http://localhost/managed-elsa/handoff/callback",
            VerifiedEndpointOrigin = "http://localhost",
            BindingVersion = 1,
            ChangedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Instance_tenant_audience_must_be_derived_from_instance_id()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Tenant audience workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        instance.ElsaTenantAudience = "urn:elsa:tenant:caller-controlled";
        db.ElsaInstances.Add(instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Instance_version_is_optimistic_concurrency_token()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateMigratedContext(connection);
        await setup.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Concurrency workspace" };
        setup.Workspaces.Add(workspace);
        await setup.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        setup.ElsaInstances.Add(instance);
        await setup.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        await using var first = new CatalogDbContext(options);
        await using var second = new CatalogDbContext(options);
        var firstCopy = await first.ElsaInstances.SingleAsync(x => x.Id == instance.Id);
        var secondCopy = await second.ElsaInstances.SingleAsync(x => x.Id == instance.Id);
        firstCopy.Name = "First writer";
        secondCopy.Name = "Second writer";
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal(2, firstCopy.Version);
    }

    [Fact]
    public async Task Instance_slug_is_immutable_after_creation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Immutable slug workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();

        instance.Slug = "replacement-slug";

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Active_and_recovery_required_deployment_runs_share_one_reservation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Run workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var application = new DeploymentApplicationEntity
        {
            WorkspaceId = workspace.Id,
            Name = "Run app",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.DeploymentApplications.Add(application);
        await db.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();
        var environment = NewEnvironment(workspace.Id, application.Id, instance.Id, "Run environment");
        db.DeploymentEnvironments.Add(environment);
        await db.SaveChangesAsync();
        db.DeploymentRuns.Add(NewRun(workspace.Id, application.Id, environment.Id, WorkspaceDeploymentRunStatus.Queued, instance.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.DeploymentRuns.Add(NewRun(workspace.Id, application.Id, environment.Id, WorkspaceDeploymentRunStatus.RecoveryRequired, instance.Id));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Audit_is_sanitized_and_append_only()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Audit workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();

        db.ElsaInstanceAuditEvents.Add(new ElsaInstanceAuditEventEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = workspace.OrganizationId,
            WorkspaceId = workspace.Id,
            InstanceId = instance.Id,
            Sequence = 1,
            EventType = "OperationFailed",
            DiagnosticCode = "instance.failed",
            Summary = "secret token should never persist",
            OperatorSubject = "operator-secret-subject",
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.ElsaInstanceAuditEvents.SingleAsync();
        Assert.Equal("instance.failed", stored.Summary);
        Assert.StartsWith("sha256:", stored.OperatorSubject);
        Assert.DoesNotContain("operator-secret-subject", stored.OperatorSubject, StringComparison.Ordinal);

        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE ElsaInstanceAuditEvents SET Summary = 'tampered' WHERE Sequence = 1"));
        db.ChangeTracker.Clear();
        var tombstoneTarget = await db.ElsaInstances.SingleAsync(x => x.Id == instance.Id);
        db.ElsaInstances.Remove(tombstoneTarget);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Save_validation_requires_complete_plan_and_thirty_day_retention()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Invariant workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var incompletePlan = NewInstance(workspace.OrganizationId, workspace.Id);
        incompletePlan.ResolvedPlanId = "plan-1";
        db.ElsaInstances.Add(incompletePlan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var incompleteCurrent = NewInstance(workspace.OrganizationId, workspace.Id);
        incompleteCurrent.ResolvedPlanId = "plan-2";
        incompleteCurrent.ResolvedPlanSchemaVersion = 1;
        incompleteCurrent.ResolvedPlanContentHash = "sha256:" + new string('b', 64);
        incompleteCurrent.ResolvedPlanUri = PlanUri(workspace.Id, incompleteCurrent.Id, "plan-2");
        incompleteCurrent.CurrentReleaseLine = "3.10";
        db.ElsaInstances.Add(incompleteCurrent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var instance = NewInstance(workspace.OrganizationId, workspace.Id);
        db.ElsaInstances.Add(instance);
        await db.SaveChangesAsync();
        var cutover = DateTimeOffset.UtcNow;
        var shortRetention = new ElsaInstanceMigrationEntity
        {
            MigrationId = Guid.NewGuid(),
            InstanceId = instance.Id,
            Phase = "Cutover",
            SourceAccessMode = "Stopped",
            CutoverAt = cutover,
            SourceRetainUntil = cutover.AddDays(29),
            CreatedAt = cutover,
            UpdatedAt = cutover
        };
        SetMigrationReferences(shortRetention, workspace.Id, instance.Id);
        db.ElsaInstanceMigrations.Add(shortRetention);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var noRetention = new ElsaInstanceMigrationEntity
        {
            MigrationId = Guid.NewGuid(),
            InstanceId = instance.Id,
            Phase = "Cutover",
            SourceAccessMode = "Stopped",
            CutoverAt = cutover,
            CreatedAt = cutover,
            UpdatedAt = cutover
        };
        SetMigrationReferences(noRetention, workspace.Id, instance.Id);
        db.ElsaInstanceMigrations.Add(noRetention);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var partialTuple = new ElsaInstanceMigrationEntity
        {
            MigrationId = Guid.NewGuid(),
            InstanceId = instance.Id,
            SourcePlanId = "plan-source",
            Phase = "Preparing",
            SourceAccessMode = "Running",
            CreatedAt = cutover,
            UpdatedAt = cutover
        };
        db.ElsaInstanceMigrations.Add(partialTuple);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var validMigration = new ElsaInstanceMigrationEntity
        {
            MigrationId = Guid.NewGuid(),
            InstanceId = instance.Id,
            SourcePlanId = "source-plan",
            SourcePlanUri = PlanUri(workspace.Id, instance.Id, "source-plan"),
            SourceReleaseLine = "3.10",
            SourceVersion = "3.10.1",
            SourceManifestDigest = "sha256:" + new string('c', 64),
            SourceDeploymentId = "source-deployment",
            TargetPlanId = "target-plan",
            TargetPlanUri = PlanUri(workspace.Id, instance.Id, "target-plan"),
            TargetReleaseLine = "4.0",
            TargetVersion = "4.0.1",
            TargetManifestDigest = "sha256:" + new string('d', 64),
            TargetDeploymentId = "target-deployment",
            Phase = "Cutover",
            SourceAccessMode = "Stopped",
            CutoverAt = cutover,
            SourceRetainUntil = cutover.AddDays(30),
            CreatedAt = cutover,
            UpdatedAt = cutover
        };
        db.ElsaInstanceMigrations.Add(validMigration);
        await db.SaveChangesAsync();

        var earlyRelease = new ElsaInstanceMigrationEntity
        {
            MigrationId = Guid.NewGuid(),
            InstanceId = instance.Id,
            Phase = "RetiringSource",
            SourceAccessMode = "Stopped",
            CutoverAt = cutover,
            SourceRetainUntil = cutover.AddDays(30),
            SourceReleasedAt = cutover.AddDays(1),
            CreatedAt = cutover,
            UpdatedAt = cutover
        };
        SetMigrationReferences(earlyRelease, workspace.Id, instance.Id);
        db.ElsaInstanceMigrations.Add(earlyRelease);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        earlyRelease.EarlyReleaseApprovedByAccountId = Guid.NewGuid();
        earlyRelease.EarlyReleaseApprovedAt = cutover.AddHours(1);
        db.ElsaInstanceMigrations.Add(earlyRelease);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Persistence_rejects_hostile_json_uri_digest_and_bearer_values()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateMigratedContext(connection);
        await db.Database.MigrateAsync();
        var workspace = new Workspace { Name = "Boundary workspace" };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        var secretJson = NewInstance(workspace.OrganizationId, workspace.Id);
        secretJson.FeatureOverridesJson = "{\"api\":{\"kind\":\"catalog\",\"value\":\"secret-token\"}}";
        db.ElsaInstances.Add(secretJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var oversizedJson = NewInstance(workspace.OrganizationId, workspace.Id);
        oversizedJson.FeatureOverridesJson = new string('x', 32_769);
        db.ElsaInstances.Add(oversizedJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var invalidPlan = NewInstance(workspace.OrganizationId, workspace.Id);
        invalidPlan.ResolvedPlanId = "plan-1";
        invalidPlan.ResolvedPlanSchemaVersion = 1;
        invalidPlan.ResolvedPlanContentHash = "sha256:" + new string('a', 64);
        invalidPlan.ResolvedPlanUri = "https://control.example/api/plans/1?token=leaked";
        db.ElsaInstances.Add(invalidPlan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var trailingPlan = NewInstance(workspace.OrganizationId, workspace.Id);
        trailingPlan.ResolvedPlanId = "plan-2";
        trailingPlan.ResolvedPlanSchemaVersion = 1;
        trailingPlan.ResolvedPlanContentHash = "sha256:" + new string('a', 64);
        trailingPlan.ResolvedPlanUri = PlanUri(workspace.Id, trailingPlan.Id, "plan-2") + "/";
        db.ElsaInstances.Add(trailingPlan);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var operation = NewOperation(workspace, null, ElsaInstanceOperationState.Succeeded, "unsafe-lease");
        operation.LeaseTokenHash = "bearer-token";
        operation.FailureSummary = "provider secret and stack trace";
        db.ElsaInstanceOperations.Add(operation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        var safeOperation = NewOperation(workspace, null, ElsaInstanceOperationState.Failed, "safe-failure");
        safeOperation.FailureCode = "operation.failed";
        safeOperation.FailureSummary = "provider token must collapse to the stable code";
        db.ElsaInstanceOperations.Add(safeOperation);
        await db.SaveChangesAsync();
        var stored = await db.ElsaInstanceOperations.SingleAsync(x => x.Id == safeOperation.Id);
        Assert.Equal("operation.failed", stored.FailureSummary);
    }

    private static CatalogDbContext CreateMigratedContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly(CatalogDatabaseServiceCollectionExtensions.SqliteMigrationsAssembly))
            .Options;
        return new CatalogDbContext(options);
    }

    private static CatalogDbContext CreateEnsureCreatedContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        return new CatalogDbContext(options);
    }

    private static ElsaInstanceEntity NewInstance(Guid organizationId, Guid workspaceId)
    {
        var now = DateTimeOffset.UtcNow;
        return new ElsaInstanceEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            WorkspaceId = workspaceId,
            Name = "Managed Elsa",
            Slug = "managed-elsa-" + Guid.NewGuid().ToString("N")[..8],
            DistributionId = "valence-runtime",
            ReleaseLine = "3.10",
            Channel = "stable",
            PatchUpdates = "automatic-within-minor",
            MinorUpdates = "explicit-approval",
            MajorMigrations = "explicit-migration",
            TopologyId = "combined",
            FeatureOverridesJson = "{}",
            TargetMode = "managed",
            RegionCode = "westeurope",
            IsolationProfile = "dedicated",
            CapacityProfile = "standard-small",
            NetworkOutcome = "public",
            DomainOutcome = "managed",
            DesiredLifecycle = ElsaDesiredLifecycle.Running,
            ObservedLifecycle = ElsaObservedLifecycle.Pending,
            Health = ElsaInstanceHealth.Unknown,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static DeploymentEnvironmentEntity NewEnvironment(Guid workspaceId, Guid applicationId, Guid? instanceId, string name) => new()
    {
        WorkspaceId = workspaceId,
        ApplicationId = applicationId,
        ElsaInstanceId = instanceId,
        Name = name,
        Tier = EnvironmentTier.Production,
        DeploymentStatus = DeploymentStatus.Blocked,
        DriftStatus = DriftStatus.Unknown,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static ElsaInstanceOperationEntity NewOperation(
        Workspace workspace,
        ElsaInstanceEntity? instance,
        ElsaInstanceOperationState state,
        string key)
    {
        var now = DateTimeOffset.UtcNow;
        return new ElsaInstanceOperationEntity
        {
            Id = Guid.NewGuid(),
            InstanceId = instance?.Id,
            OrganizationId = workspace.OrganizationId,
            WorkspaceId = workspace.Id,
            Action = instance is null
                ? ElsaInstanceOperationAction.Create
                : state == ElsaInstanceOperationState.WaitingForPriorOperation
                ? ElsaInstanceOperationAction.Delete
                : ElsaInstanceOperationAction.Reconcile,
            IdempotencyScope = $"instance/{instance?.Id.ToString("N") ?? "workspace"}",
            IdempotencyKey = key,
            RequestHash = new string('a', 64),
            ExpectedVersion = 1,
            State = state,
            AttemptNumber = 1,
            AcceptedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static ElsaInstanceIdentityBindingEntity NewBinding(Guid instanceId, string audience, string callback) => new()
    {
        InstanceId = instanceId,
        Audience = audience,
        CanonicalCallbackUri = callback,
        VerifiedEndpointOrigin = "https://control.example",
        BindingVersion = 1,
        ChangedAt = DateTimeOffset.UtcNow
    };

    private static DeploymentRunEntity NewRun(Guid workspaceId, Guid applicationId, Guid environmentId, WorkspaceDeploymentRunStatus status, Guid? elsaInstanceId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new DeploymentRunEntity
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ElsaInstanceId = elsaInstanceId,
            ApplicationId = applicationId,
            EnvironmentId = environmentId,
            EngineId = Guid.NewGuid(),
            SourceRevisionId = Guid.NewGuid(),
            Status = status,
            ValidationOutcome = DeploymentValidationOutcome.Passed,
            ConfirmationId = Guid.NewGuid(),
            ActorAccountId = Guid.NewGuid(),
            QueuedAt = now,
            CreatedAt = now,
            AttemptNumber = 1
        };
    }

    private static string PlanUri(Guid workspaceId, Guid instanceId, string planId) =>
        $"https://control.example/api/workspaces/{workspaceId:D}/instances/{instanceId:D}/resolved-plans/{planId}";

    private static void SetMigrationReferences(ElsaInstanceMigrationEntity migration, Guid workspaceId, Guid instanceId)
    {
        migration.WorkspaceId = workspaceId;
        migration.SourcePlanId = "source-plan";
        migration.SourcePlanUri = PlanUri(workspaceId, instanceId, migration.SourcePlanId);
        migration.SourceReleaseLine = "3.10";
        migration.SourceVersion = "3.10.1";
        migration.SourceManifestDigest = "sha256:" + new string('c', 64);
        migration.SourceDeploymentId = "source-deployment";
        migration.TargetPlanId = "target-plan";
        migration.TargetPlanUri = PlanUri(workspaceId, instanceId, migration.TargetPlanId);
        migration.TargetReleaseLine = "4.0";
        migration.TargetVersion = "4.0.1";
        migration.TargetManifestDigest = "sha256:" + new string('d', 64);
        migration.TargetDeploymentId = "target-deployment";
    }

    private static async Task<string[]> ReadScalarStringsAsync(CatalogDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values.ToArray();
    }
}
