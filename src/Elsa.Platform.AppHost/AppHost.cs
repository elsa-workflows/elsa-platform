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

var api = builder.AddProject<Projects.Elsa_Platform_Api>("api")
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
    const string keycloakAuthority = "http://127.0.0.1:8080/realms/elsa-platform";
    const string keycloakClientId = "elsa-platform-console";
    const string keycloakClientSecret = "local-dev-secret";
    var keycloakAdminUsername = builder.AddParameter("keycloak-admin-username", "admin");
    var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", "admin", secret: true);
    var keycloakRealmImport = Path.GetFullPath(
        Path.Combine(builder.AppHostDirectory, "../../dev/keycloak/elsa-platform-realm.json"));
    var keycloak = builder.AddKeycloak(
            "keycloak",
            port: 8080,
            adminUsername: keycloakAdminUsername,
            adminPassword: keycloakAdminPassword)
        .WithDataVolume("elsa-platform-keycloak-data")
        .WithRealmImport(keycloakRealmImport);
    var console = builder.AddViteApp("console", "../Elsa.Platform.Console")
        .WithReference(api)
        .WithEnvironment("CATALOG_API_PROXY_TARGET", api.GetEndpoint("http"))
        .WaitFor(api);

    api.WithEnvironment("Database__Provider", "Sqlite")
        .WithEnvironment("Console__DevelopmentUrl", console.GetEndpoint("http"))
        .WithEnvironment("ConnectionStrings__Catalog", "Data Source=elsa-catalog-dev.db")
        .WithEnvironment("Authentication__PlatformIdentity__Provider", "Keycloak")
        .WithEnvironment("Authentication__PlatformIdentity__Authority", keycloakAuthority)
        .WithEnvironment("Authentication__PlatformIdentity__Issuer", keycloakAuthority)
        .WithEnvironment("Authentication__PlatformIdentity__Audience", keycloakClientId)
        .WithEnvironment("Authentication__PlatformIdentity__ClientId", keycloakClientId)
        .WithEnvironment("Authentication__PlatformIdentity__ClientSecret", keycloakClientSecret)
        .WithEnvironment("Authentication__PlatformIdentity__RedirectUri", "/api/auth/callback")
        .WithEnvironment("Authentication__PlatformIdentity__PostLogoutRedirectUri", "/admin")
        .WithEnvironment("Authentication__PlatformIdentity__RequireHttpsMetadata", "false")
        .WithEnvironment("Authentication__Admin__AllowAuthenticatedCustomerSession", "true")
        .WithEnvironment("Authentication__WorkspaceTrustedHeaders__Enabled", "false")
        .WaitFor(keycloak);
}

builder.Build().Run();
