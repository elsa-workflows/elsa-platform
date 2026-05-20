using Elsa.Platform.PackageCatalog.Core.Packages;

namespace Elsa.Platform.PackageCatalog.Core.Manifests;

public sealed class FeatureRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageVersionId { get; set; }
    public PackageVersion? PackageVersion { get; set; }
    public string FeatureId { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string RequiredCapabilitiesJson { get; set; } = "[]";
    public string DependenciesJson { get; set; } = "[]";
    public string ConflictsJson { get; set; } = "[]";
    public string InfrastructureJson { get; set; } = "[]";
    public bool Advanced { get; set; }
    public bool Experimental { get; set; }
    public string ExtensionsJson { get; set; } = "{}";
    public List<FeatureSettingRecord> Settings { get; set; } = [];
}

public sealed class FeatureSettingRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FeatureRecordId { get; set; }
    public FeatureRecord? FeatureRecord { get; set; }
    public string Name { get; set; } = "";
    public string? ClrType { get; set; }
    public string JsonType { get; set; } = "";
    public bool Required { get; set; }
    public string? DefaultValueJson { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string ValidationJson { get; set; } = "{}";
    public bool Secret { get; set; }
    public bool RestartRequired { get; set; }
    public string? EnvironmentVariable { get; set; }
    public string UiJson { get; set; } = "{}";
    public string ExtensionsJson { get; set; } = "{}";
}
