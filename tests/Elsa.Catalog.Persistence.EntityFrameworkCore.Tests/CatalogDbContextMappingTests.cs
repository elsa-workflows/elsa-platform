using Elsa.Catalog.Core.Packages;
using Elsa.Catalog.Persistence.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Catalog.Persistence.EntityFrameworkCore.Tests;

public sealed class CatalogDbContextMappingTests
{
    [Fact]
    public async Task Can_create_schema_and_store_package_source()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        db.PackageSources.Add(new PackageSource
        {
            Name = "NuGet",
            Url = "https://api.nuget.org/v3/index.json",
            IncludePatterns = ["Elsa.*"],
            VersionDiscoveryPolicy = PackageSourceVersionDiscoveryPolicy.LatestPreview
        });
        await db.SaveChangesAsync();

        var saved = await db.PackageSources.SingleAsync();
        saved.VersionDiscoveryPolicy.Should().Be(PackageSourceVersionDiscoveryPolicy.LatestPreview);
    }

    [Fact]
    public async Task Tracks_in_place_package_source_pattern_changes()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        await using var db = new CatalogDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();

        db.PackageSources.Add(new PackageSource
        {
            Name = "NuGet",
            Url = "https://api.nuget.org/v3/index.json",
            IncludePatterns = ["Elsa.*"]
        });
        await db.SaveChangesAsync();

        var source = await db.PackageSources.SingleAsync();
        source.IncludePatterns.Add("Elsa.Workflows.*");
        source.ExcludePatterns.Add("Elsa.Experimental.*");
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var saved = await db.PackageSources.SingleAsync();
        saved.IncludePatterns.Should().Contain("Elsa.Workflows.*");
        saved.ExcludePatterns.Should().Contain("Elsa.Experimental.*");
    }

    [Fact]
    public void String_list_comparer_handles_null_values()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new CatalogDbContext(options);
        var comparer = db.Model
            .FindEntityType(typeof(PackageSource))!
            .FindProperty(nameof(PackageSource.IncludePatterns))!
            .GetValueComparer();

        comparer.Should().NotBeNull();
        comparer!.Equals(null, null).Should().BeTrue();
        comparer.GetHashCode(null).Should().Be(0);
        var snapshot = () => comparer.Snapshot(null);
        snapshot.Should().NotThrow();
    }
}
