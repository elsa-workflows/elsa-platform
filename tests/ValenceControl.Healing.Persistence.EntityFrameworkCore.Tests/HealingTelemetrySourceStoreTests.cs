using ValenceControl.Healing.Core;
using Microsoft.EntityFrameworkCore;

namespace ValenceControl.Healing.Persistence.EntityFrameworkCore.Tests;

public sealed class HealingTelemetrySourceStoreTests
{
    [Fact]
    public async Task Stale_concurrent_rotation_cannot_return_a_second_unpersisted_credential()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"valence-control-healing-source-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<HealingDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        try
        {
            await using (var setup = new HealingDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                await new HealingStore(setup).AddTelemetrySourceAsync(CreateSource());
            }

            await using var firstContext = new HealingDbContext(options);
            await using var secondContext = new HealingDbContext(options);
            var firstStore = new HealingStore(firstContext);
            var secondStore = new HealingStore(secondContext);
            var identity = await firstContext.HealingTelemetrySources.AsNoTracking().SingleAsync();
            var firstView = (await firstStore.GetTelemetrySourceAsync(
                identity.WorkspaceId, identity.ApplicationId, identity.EnvironmentId, identity.Id))!;
            var staleView = (await secondStore.GetTelemetrySourceAsync(
                identity.WorkspaceId, identity.ApplicationId, identity.EnvironmentId, identity.Id))!;

            var accepted = await firstStore.RotateTelemetrySourceAsync(
                identity.WorkspaceId, identity.ApplicationId, identity.EnvironmentId, identity.Id, firstView.Version,
                Enumerable.Repeat((byte)7, 32).ToArray(), Enumerable.Repeat((byte)8, 32).ToArray(), DateTimeOffset.UtcNow);
            var staleRotation = () => secondStore.RotateTelemetrySourceAsync(
                identity.WorkspaceId, identity.ApplicationId, identity.EnvironmentId, identity.Id, staleView.Version,
                Enumerable.Repeat((byte)9, 32).ToArray(), Enumerable.Repeat((byte)10, 32).ToArray(), DateTimeOffset.UtcNow).AsTask();

            Assert.NotNull(accepted);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(staleRotation);
            await using var verificationContext = new HealingDbContext(options);
            var persisted = await verificationContext.HealingTelemetrySources.AsNoTracking().SingleAsync();
            Assert.Equal(2, persisted.CredentialVersion);
            Assert.All(persisted.CredentialSalt, item => Assert.True(item == 7));
            Assert.All(persisted.CredentialHash, item => Assert.True(item == 8));
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Source_lifecycle_is_scope_isolated_and_revocation_removes_authentication_authority()
    {
        await using var fixture = await HealingPersistenceFixture.CreateAsync();
        var store = new HealingStore(fixture.Db);
        var source = CreateSource();

        var created = await store.AddTelemetrySourceAsync(source);
        var crossWorkspace = await store.GetTelemetrySourceAsync(
            Guid.NewGuid(), source.ApplicationId, source.EnvironmentId, source.Id);
        var crossApplication = await store.GetTelemetrySourceAsync(
            source.WorkspaceId, Guid.NewGuid(), source.EnvironmentId, source.Id);
        var crossEnvironment = await store.GetTelemetrySourceAsync(
            source.WorkspaceId, source.ApplicationId, Guid.NewGuid(), source.Id);
        var rotated = await store.RotateTelemetrySourceAsync(
            source.WorkspaceId, source.ApplicationId, source.EnvironmentId, source.Id,
            created.Version,
            Enumerable.Repeat((byte)3, 32).ToArray(),
            Enumerable.Repeat((byte)4, 32).ToArray(),
            source.CreatedAt.AddMinutes(1));
        var wrongScopeRotation = await store.RotateTelemetrySourceAsync(
            source.WorkspaceId, source.ApplicationId, Guid.NewGuid(), source.Id,
            rotated!.Version,
            Enumerable.Repeat((byte)5, 32).ToArray(),
            Enumerable.Repeat((byte)6, 32).ToArray(),
            source.CreatedAt.AddMinutes(2));
        var revoked = await store.RevokeTelemetrySourceAsync(
            source.WorkspaceId, source.ApplicationId, source.EnvironmentId, source.Id,
            rotated.Version,
            source.CreatedAt.AddMinutes(3));
        var authenticationLookup = await store.GetActiveTelemetrySourceForAuthenticationAsync(source.Id);

        Assert.NotEmpty(created.Version);
        Assert.Null(crossWorkspace);
        Assert.Null(crossApplication);
        Assert.Null(crossEnvironment);
        Assert.NotNull(rotated);
        Assert.Equal(2, rotated.CredentialVersion);
        Assert.All(rotated.CredentialSalt, item => Assert.True(item == 3));
        Assert.All(rotated.CredentialHash, item => Assert.True(item == 4));
        Assert.Equal(source.CreatedAt.AddMinutes(1), rotated.RotatedAt);
        Assert.Null(wrongScopeRotation);
        Assert.Equal(HealingTelemetrySourceStatus.Revoked, revoked!.Status);
        Assert.Equal(source.CreatedAt.AddMinutes(3), revoked.RevokedAt);
        Assert.Null(authenticationLookup);
    }

    private static HealingTelemetrySource CreateSource() => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        ApplicationId = Guid.NewGuid(),
        EnvironmentId = Guid.NewGuid(),
        Name = "Orders production",
        CredentialSalt = Enumerable.Repeat((byte)1, 32).ToArray(),
        CredentialHash = Enumerable.Repeat((byte)2, 32).ToArray(),
        CredentialVersion = 1,
        Status = HealingTelemetrySourceStatus.Active,
        CreatedAt = DateTimeOffset.Parse("2026-07-16T12:00:00Z")
    };
}
