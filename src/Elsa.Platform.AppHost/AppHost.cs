using Azure.Provisioning.AppService;
using Azure.Provisioning.Sql;

var builder = DistributedApplication.CreateBuilder(args);
var adminApiKey = builder.AddParameter("adminApiKey", secret: true);
var applicationBuildNumber = Environment.GetEnvironmentVariable("APPLICATION_BUILD_NUMBER")
    ?? Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");

builder.AddAzureAppServiceEnvironment("elsa-platform")
    .ConfigureInfrastructure(infrastructure =>
    {
        var plan = infrastructure.GetProvisionableResources().OfType<AppServicePlan>().Single();
        plan.Sku = new AppServiceSkuDescription
        {
            Name = "B1",
            Tier = "Basic",
            Capacity = 1
        };
    });

var api = builder.AddProject<Projects.Elsa_Platform_PackageCatalog_Api>("api")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Authentication__ApiKey", adminApiKey);

if (!string.IsNullOrWhiteSpace(applicationBuildNumber))
{
    api.WithEnvironment("Application__BuildNumber", applicationBuildNumber);
}

if (builder.ExecutionContext.IsPublishMode)
{
    var sql = builder.AddAzureSqlServer("platform-sql")
        .ConfigureInfrastructure(infrastructure =>
        {
            var sqlDatabase = infrastructure.GetProvisionableResources().OfType<SqlDatabase>().Single();
            sqlDatabase.Sku = new SqlSku
            {
                Name = "GP_S_Gen5",
                Tier = "GeneralPurpose",
                Family = "Gen5",
                Capacity = 1
            };
            sqlDatabase.MinCapacity = 0.5;
            sqlDatabase.AutoPauseDelay = 60;
            sqlDatabase.RequestedBackupStorageRedundancy = SqlBackupStorageRedundancy.Zone;
            sqlDatabase.IsZoneRedundant = false;
        });
    var database = sql.AddDatabase("Catalog");

    api.WithReference(database)
        .WithEnvironment("AZURE_TOKEN_CREDENTIALS", "prod")
        .WithEnvironment("Database__Provider", "SqlServer");
}
else
{
    api.WithEnvironment("Database__Provider", "Sqlite")
        .WithEnvironment("ConnectionStrings__Catalog", "Data Source=elsa-catalog-dev.db");

    builder.AddViteApp("console", "../Elsa.Platform.Console")
        .WithReference(api)
        .WithEnvironment("CATALOG_API_PROXY_TARGET", api.GetEndpoint("http"))
        .WaitFor(api);
}

builder.Build().Run();
