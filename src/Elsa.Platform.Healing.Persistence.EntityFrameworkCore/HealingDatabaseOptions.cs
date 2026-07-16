namespace Elsa.Platform.Healing.Persistence.EntityFrameworkCore;

public sealed class HealingDatabaseOptions
{
    public const string SectionName = "Healing:Database";

    public HealingDatabaseProvider Provider { get; set; } = HealingDatabaseProvider.Sqlite;
}

public enum HealingDatabaseProvider
{
    Sqlite,
    SqlServer
}
