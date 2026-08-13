using ValenceControl.PackageCatalog.Core.Manifests;
using ValenceControl.PackageCatalog.Core.Packages;

namespace ValenceControl.PackageCatalog.Testing;

public static class PublicCatalogSeedData
{
    public static PackageSource CreatePackageSource() => new()
    {
        Name = "Test NuGet",
        Url = "https://example.test/v3/index.json",
        IncludePatterns = ["Elsa."],
        ApprovalPolicy = PackageSourceApprovalPolicy.Manual
    };

    public static Package CreatePackage(
        PackageSource source,
        string packageId = "Elsa.Email",
        bool approved = true,
        bool listed = true)
    {
        var package = new Package
        {
            PackageId = packageId,
            DisplayName = PackageDisplayNamePolicy.DefaultForPackageId(packageId),
            Source = source,
            SourceId = source.Id,
            Approved = approved,
            Listed = listed,
            LatestVersion = "1.0.0"
        };

        source.Packages.Add(package);
        return package;
    }

    public static PackageVersion AddVersion(
        Package package,
        string version = "1.0.0",
        ValidationStatus validationStatus = ValidationStatus.Valid,
        PackageApprovalStatus approvalStatus = PackageApprovalStatus.Approved,
        bool listed = true,
        bool suspicious = false,
        string? manifestHash = null,
        string? suspiciousManifestHash = null,
        string[]? runtimeKinds = null)
    {
        var packageVersion = new PackageVersion
        {
            Package = package,
            PackageId = package.Id,
            Version = version,
            ManifestJson = ApplyRuntimeKinds(new ManifestFixtureBuilder().WithPackage(package.PackageId, version).WithFeature(), runtimeKinds).BuildJson(),
            ManifestHash = manifestHash ?? $"{package.PackageId}-{version}-hash",
            SuspiciousManifestHash = suspiciousManifestHash,
            SchemaVersion = "1.0",
            ValidationStatus = validationStatus,
            ApprovalStatus = approvalStatus,
            IsListed = listed,
            SuspiciousChangeDetected = suspicious,
            PublishedAt = DateTimeOffset.UtcNow
        };

        package.Versions.Add(packageVersion);
        return packageVersion;
    }

    public static Package CreatePackageWithoutVersions(PackageSource source, string packageId = "Elsa.Empty")
    {
        var package = CreatePackage(source, packageId);
        package.LatestVersion = null;
        return package;
    }

    public static Package CreateMultiVersionPackage(PackageSource source, string packageId = "Elsa.Persistence.PostgreSql")
    {
        var package = CreatePackage(source, packageId, approved: false);
        AddVersion(package, "1.0.1", approvalStatus: PackageApprovalStatus.Approved);
        AddVersion(package, "1.0.2", approvalStatus: PackageApprovalStatus.Pending);
        package.LatestVersion = "1.0.2";
        return package;
    }

    public static FeatureRecord AddFeature(
        PackageVersion version,
        string featureId = "email",
        string displayName = "Email",
        string dependenciesJson = "[]",
        string conflictsJson = "[]",
        string infrastructureJson = "[]")
    {
        var feature = new FeatureRecord
        {
            PackageVersion = version,
            PackageVersionId = version.Id,
            FeatureId = featureId,
            TypeName = $"Elsa.Features.{featureId}.Feature",
            DisplayName = displayName,
            Description = $"{displayName} feature.",
            Category = "Communication",
            DependenciesJson = dependenciesJson,
            ConflictsJson = conflictsJson,
            InfrastructureJson = infrastructureJson
        };

        feature.Settings.Add(new FeatureSettingRecord
        {
            FeatureRecord = feature,
            FeatureRecordId = feature.Id,
            Name = "smtpHost",
            ClrType = "System.String",
            JsonType = "string",
            Required = true,
            DisplayName = "SMTP host",
            Category = "Connection"
        });

        version.Features.Add(feature);
        return feature;
    }

    private static ManifestFixtureBuilder ApplyRuntimeKinds(ManifestFixtureBuilder builder, string[]? runtimeKinds) =>
        runtimeKinds is null ? builder : builder.WithRuntimeKinds(runtimeKinds);
}
