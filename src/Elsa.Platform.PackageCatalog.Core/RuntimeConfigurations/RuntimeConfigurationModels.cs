namespace Elsa.Platform.PackageCatalog.Core.RuntimeConfigurations;

public sealed class RuntimeConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string IntentJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SoftDeletedAt { get; set; }
    public List<RuntimeConfigurationVersion> Versions { get; set; } = [];
}

public sealed class RuntimeConfigurationVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuntimeConfigurationId { get; set; }
    public RuntimeConfiguration? RuntimeConfiguration { get; set; }
    public int VersionNumber { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string IntentJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record RuntimeConfigurationMutation(
    string Name,
    string? Description,
    string IntentJson);
