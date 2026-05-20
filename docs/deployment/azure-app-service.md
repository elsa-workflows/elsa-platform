# Aspire Deployment to Azure App Service

## Recommendation

Use Aspire and Azure Developer CLI (`azd`) as the deployment path. The AppHost
declares the API, Azure App Service environment, and Azure SQL database;
Aspire/azd provisions the resources, builds the app container image, pushes it
to ACR, and deploys the Web App.

The previous manually provisioned Web App can be deleted once anything important
has been backed up.

## Local Tooling

Install the current .NET 10 SDK before deploying. As of May 15, 2026, the
current .NET 10 SDK line is `10.0.300`, released May 12, 2026.

The official Aspire templates are installed with:

```bash
dotnet new install Aspire.ProjectTemplates
dotnet tool install -g Aspire.Cli
```

On this machine, `~/.dotnet/tools/aspire` is `13.3.2`. If `aspire --version`
prints an older version, move `~/.dotnet/tools` before `~/.aspire/bin` in
`PATH`, or run `~/.dotnet/tools/aspire` explicitly.

## Deploy

```bash
azd auth login
azd init
azd env set adminApiKey <strong-secret>
azd up
```

When `azd init` asks how to initialize the app, scan the current directory and
confirm the detected Aspire AppHost.

## GitHub Actions Deployment

The `Azure API Deploy` workflow runs on pushes to `main` and can also be run
manually from GitHub Actions. Normal `main` deployments build the admin
dashboard, build the API container with the dashboard static assets mounted
under `/admin`, push it to ACR, and update the existing App Service site
container:

```bash
docker build --file src/Elsa.Platform.PackageCatalog.Api/Dockerfile --tag <acr>/<repo>:<sha> .
docker push <acr>/<repo>:<sha>
az webapp sitecontainers update ...
```

The Dockerfile uses a Node build stage for `src/Elsa.Platform.PackageCatalog.AdminUi` and copies
the Vite `dist` output into `src/Elsa.Platform.PackageCatalog.Api/wwwroot/admin` before
`dotnet publish`. ASP.NET Core serves `/admin` as the dashboard SPA and keeps the
admin API endpoints under `/api/admin`.

This is the fast path for application-only updates because the Azure resources
are expected to already exist. It also avoids reapplying the App Service Bicep
module on every code change. If the AppHost infrastructure shape changes, run
the same workflow manually and choose `deploy_mode: infra`; that path runs:

```bash
azd up --no-prompt
```

`azd up` provisions infrastructure incrementally before deploying. Keep it as a
manual choice so routine code changes do not spend time checking and updating
Azure resources on every push.

Configure the workflow in a GitHub environment named `production` unless you
change the workflow environment name. With OIDC, the Microsoft Entra federated
credential should trust this repository and environment. If using the default
GitHub environment subject, it is:

```text
repo:<owner>/<repo>:environment:production
```

Required GitHub Actions variables:

- `AZURE_CLIENT_ID`: application/client ID for the federated identity.
- `AZURE_TENANT_ID`: Microsoft Entra tenant ID.
- `AZURE_SUBSCRIPTION_ID`: target Azure subscription ID.
- `AZURE_ENV_NAME`: existing or desired `azd` environment name, for example
  `elsa-package-catalog`.
- `AZURE_LOCATION`: Azure region for the `azd` environment, for example
  `westeurope`.
- `AZURE_RESOURCE_GROUP`: resource group containing the deployed App Service,
  for example `rg-elsa-package-catalog`.
- `AZURE_WEBAPP_NAME`: API App Service name, for example `api-k35qdj734hds2`.
- `AZURE_APP_SERVICE_DASHBOARD_URI`: Aspire dashboard URL emitted by `azd up`.
- `AZURE_CONTAINER_REGISTRY_ENDPOINT`: ACR login server for app image pushes,
  for example `elsapackagecatalogacrk35qdj734hds2.azurecr.io`.
- `API_IDENTITY_CLIENTID` and `API_IDENTITY_ID`: managed identity values emitted
  by `azd up` for the API Web App.
- `CATALOG_SQL_SQLSERVERFQDN`: Azure SQL server FQDN emitted by `azd up`.
- `ELSA_PACKAGE_CATALOG_AZURE_APP_SERVICE_DASHBOARD_URI`: Aspire dashboard URL
  emitted by `azd up`.
- `ELSA_PACKAGE_CATALOG_AZURE_CONTAINER_REGISTRY_ENDPOINT`: ACR login server
  emitted by `azd up`.
- `ELSA_PACKAGE_CATALOG_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_CLIENT_ID` and
  `ELSA_PACKAGE_CATALOG_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID`: managed
  identity values emitted by `azd up` for ACR image pushes.
- `ELSA_PACKAGE_CATALOG_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_ID` and
  `ELSA_PACKAGE_CATALOG_AZURE_WEBSITE_CONTRIBUTOR_MANAGED_IDENTITY_PRINCIPAL_ID`:
  managed identity values emitted by `azd up` for Web App updates.
- `ELSA_PACKAGE_CATALOG_PLANID`: App Service plan resource ID emitted by
  `azd up`.

Required GitHub Actions secrets:

- `ADMIN_API_KEY`: strong API key passed to the AppHost `adminApiKey` parameter
  and surfaced to the API as `Authentication__ApiKey`.

The workflow validates the configuration, restores the solution, builds the
Aspire AppHost, runs the API test project, signs in to Azure with GitHub
federated credentials, creates the local CI `azd` environment metadata, sets the
secured `infra.parameters.adminApiKey` parameter and required azd environment
outputs for the run, then deploys either the application container or the full
infrastructure path.

## GitHub/Azure Bootstrap

Use the bootstrap script to recreate or refresh the GitHub `production`
environment wiring from the selected `azd` environment:

```bash
scripts/bootstrap-github-azure.sh --azd-environment elsa-package-catalog
```

The script:

- creates the GitHub environment if needed;
- optionally runs `azd pipeline config --provider github --auth-type federated`
  to configure the Microsoft Entra federated credential;
- reads deployment outputs from `azd env get-value`;
- sets the required GitHub environment variables with `gh variable set`;
- sets `ADMIN_API_KEY` with `gh secret set` without printing the value.

If the federated credential already exists and only the GitHub variables/secrets
need to be refreshed, skip the Azure pipeline setup step:

```bash
scripts/bootstrap-github-azure.sh --skip-pipeline-config
```

For a preview that does not modify Azure or GitHub:

```bash
scripts/bootstrap-github-azure.sh --skip-pipeline-config --dry-run
```

Set `ADMIN_API_KEY` in the shell to override the value discovered from the local
`azd` environment:

```bash
ADMIN_API_KEY='<strong-secret>' scripts/bootstrap-github-azure.sh
```

## Removing Existing Resources

If the old resources are in a dedicated resource group, delete the group:

```bash
az group delete --name <old-resource-group>
```

If the group has shared resources, delete only the old Web App, plan, registry,
storage account, and related managed identities after confirming they are not
used elsewhere.

## Database Provider

The API supports two EF Core providers:

- `Database:Provider=Sqlite`
- `Database:Provider=SqlServer`

Local development defaults to SQLite. Aspire publish mode provisions Azure SQL,
injects `ConnectionStrings__Catalog`, and sets `Database__Provider=SqlServer`.

Each provider has its own EF Core migration assembly:

- SQLite: `Elsa.Platform.PackageCatalog.Persistence.SqliteMigrations`
- SQL Server/Azure SQL: `Elsa.Platform.PackageCatalog.Persistence.SqlServerMigrations`

The API selects the matching migration assembly with the provider and applies
migrations at startup outside the `Testing` environment.

SQL Server connections default to a 120-second connect timeout with provider
retry enabled. This gives Azure SQL room to complete slow post-login handshakes,
for example after an idle/serverless database resumes. Override
`Database__SqlServer__ConnectTimeoutSeconds` or an explicit `Connect Timeout`
connection-string value only when the target database has a known lower latency
profile.

SQLite remains fine for local development and single-process test runs. For
production and App Service scale-out, use Azure SQL. SQLite on shared App
Service storage or Azure Files is not a good production target because SQLite
depends on filesystem locking, and WAL mode does not support clients on
different machines through a network filesystem.
