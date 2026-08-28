namespace ElsaControl.RuntimeBuilder.Core.Builder;

public sealed class InfrastructureProviderCatalog
{
    private static readonly IReadOnlyList<InfrastructureProvider> Providers =
    [
        new("postgres-compose", "PostgreSQL", "database", "compose-sidecar", "postgres", ["relational", "transactions"], ["connectionString"]),
        new("sqlserver-compose", "SQL Server", "database", "compose-sidecar", "sqlserver", ["relational", "transactions"], ["connectionString"]),
        new("rabbitmq-compose", "RabbitMQ", "message-broker", "compose-sidecar", "rabbitmq", ["amqp", "queues"], ["connectionString"]),
        new("azure-service-bus-external", "Azure Service Bus", "message-broker", "external-service", "azure-service-bus", ["queues", "topics"], ["connectionString"]),
        new("redis-compose", "Redis", "cache", "compose-sidecar", "redis", ["distributed-cache", "backplane"], ["connectionString"]),
        new("azurite-compose", "Azurite", "blob-storage", "compose-sidecar", "azurite", ["blob-storage", "local-development"], ["connectionString"]),
        new("mailpit-compose", "Mailpit", "smtp", "compose-sidecar", "mailpit", ["smtp", "local-development"], ["host", "port"])
    ];

    public IReadOnlyList<InfrastructureProvider> ListProviders() => Providers;
}

public sealed record InfrastructureProvider(
    string Id,
    string DisplayName,
    string Kind,
    string Strategy,
    string Provider,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Outputs);
